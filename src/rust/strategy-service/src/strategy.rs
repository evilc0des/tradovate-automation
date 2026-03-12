use std::collections::HashMap;
use std::time::{Duration, Instant};

use chrono::Utc;
use uuid::Uuid;

use crate::config::AppConfig;
use crate::features::FeatureSnapshot;
use crate::models::{MarketDataMessage, TradeSignal};

// ── Pluggable trait ───────────────────────────────────────────────────────────

/// Every strategy implements this trait.  The feature snapshot is pre-computed
/// by the main loop so strategies only ever see normalised, typed features —
/// never raw market state.
pub trait Strategy: Send {
    fn strategy_id(&self) -> &str;

    /// Called on every accepted market-data frame.
    /// `features` is `None` when there is insufficient state to compute them
    /// (e.g. no bid/ask/last yet for this instrument).
    fn on_market_data(
        &mut self,
        cfg: &AppConfig,
        msg: &MarketDataMessage,
        features: Option<&FeatureSnapshot>,
    ) -> Option<TradeSignal>;
}

// ── Factory ───────────────────────────────────────────────────────────────────

pub fn build_strategy(cfg: &AppConfig) -> Box<dyn Strategy> {
    match cfg.strategy_name.as_str() {
        "ema-momentum" => Box::new(EmaMomentumStrategy::new(cfg.cooldown_ms)),
        _ => Box::new(DeterministicStrategy::new(cfg.cooldown_ms)),
    }
}

// ── DeterministicStrategy ─────────────────────────────────────────────────────
//
// Original connectivity-test strategy kept for backward compatibility and
// quick sanity checks.  Emits a signal when the last trade price breaks
// outside the current bid-ask spread.

pub struct DeterministicStrategy {
    cooldown: Duration,
    last_emitted_by_instrument: HashMap<String, Instant>,
    forced_trade_emitted: bool,
}

impl DeterministicStrategy {
    pub fn new(cooldown_ms: u64) -> Self {
        Self {
            cooldown: Duration::from_millis(cooldown_ms),
            last_emitted_by_instrument: HashMap::new(),
            forced_trade_emitted: false,
        }
    }
}

impl Strategy for DeterministicStrategy {
    fn strategy_id(&self) -> &str {
        "deterministic-v1"
    }

    fn on_market_data(
        &mut self,
        cfg: &AppConfig,
        msg: &MarketDataMessage,
        features: Option<&FeatureSnapshot>,
    ) -> Option<TradeSignal> {
        if cfg.force_trade_once && !self.forced_trade_emitted {
            self.forced_trade_emitted = true;
            return Some(build_signal(
                cfg,
                msg,
                self.strategy_id(),
                &cfg.force_trade_side,
                "forced one-shot connectivity test",
            ));
        }

        let now = Instant::now();
        if let Some(last) = self.last_emitted_by_instrument.get(&msg.instrument) {
            if now.duration_since(*last) < self.cooldown {
                return None;
            }
        }

        let f = features?;

        let side = if f.last > f.ask {
            "Buy"
        } else if f.last < f.bid {
            "Sell"
        } else {
            return None;
        };

        self.last_emitted_by_instrument
            .insert(msg.instrument.clone(), now);

        Some(build_signal(
            cfg,
            msg,
            self.strategy_id(),
            side,
            &format!(
                "deterministic threshold rule spread={:.4} mid={:.4} momentum3={:?} sessionTicks={}",
                f.spread, f.mid_price, f.momentum_3, f.session_ticks
            ),
        ))
    }
}

// ── EmaMomentumStrategy ───────────────────────────────────────────────────────
//
// Trend-following crossover strategy.
//
// Entry rules:
//   Buy  – 5-period EMA crosses above 20-period EMA (momentum turning up).
//   Sell – 5-period EMA crosses below 20-period EMA (momentum turning down).
//
// Both EMAs are computed on last-trade prices via the shared MarketState.
// No signal is emitted until both EMAs have at least one value (requires >=1
// trade print).  A per-instrument cooldown prevents re-entry spam.

pub struct EmaMomentumStrategy {
    cooldown: Duration,
    last_emitted_by_instrument: HashMap<String, Instant>,
    forced_trade_emitted: bool,
    /// Previous EMA regime per instrument: `true` = fast was above slow.
    prev_fast_above_slow: HashMap<String, bool>,
}

impl EmaMomentumStrategy {
    pub fn new(cooldown_ms: u64) -> Self {
        Self {
            cooldown: Duration::from_millis(cooldown_ms),
            last_emitted_by_instrument: HashMap::new(),
            forced_trade_emitted: false,
            prev_fast_above_slow: HashMap::new(),
        }
    }
}

impl Strategy for EmaMomentumStrategy {
    fn strategy_id(&self) -> &str {
        "ema-momentum-v1"
    }

    fn on_market_data(
        &mut self,
        cfg: &AppConfig,
        msg: &MarketDataMessage,
        features: Option<&FeatureSnapshot>,
    ) -> Option<TradeSignal> {
        if cfg.force_trade_once && !self.forced_trade_emitted {
            self.forced_trade_emitted = true;
            return Some(build_signal(
                cfg,
                msg,
                self.strategy_id(),
                &cfg.force_trade_side,
                "forced one-shot connectivity test",
            ));
        }

        // Regime tracking must run unconditionally — even when features are
        // absent or incomplete — so that partial-data ticks don't corrupt the
        // crossover detector.  We only skip the regime *path* when both EMA
        // values are actually available.
        let ema_state = features.and_then(|f| {
            let fast = f.ema_fast?;
            let slow = f.ema_slow?;
            Some((f, fast, slow))
        });

        let (f, fast, slow) = match ema_state {
            Some(v) => v,
            None => return None, // EMAs not warm yet — nothing to decide
        };

        let fast_above = fast > slow;

        // Detect crossover vs previous regime.
        let crossover = match self.prev_fast_above_slow.get(&msg.instrument) {
            Some(&prev_above) => {
                if fast_above && !prev_above {
                    Some("Buy")   // crossed up
                } else if !fast_above && prev_above {
                    Some("Sell")  // crossed down
                } else {
                    None          // no change in regime
                }
            }
            None => None, // first bar — record regime but don't trade yet
        };

        // Always keep regime current — must happen BEFORE the cooldown gate so
        // that crossovers occurring during cooldown are not silently lost.
        self.prev_fast_above_slow
            .insert(msg.instrument.clone(), fast_above);

        let side = crossover?;

        let now = Instant::now();
        if let Some(last) = self.last_emitted_by_instrument.get(&msg.instrument) {
            if now.duration_since(*last) < self.cooldown {
                return None;
            }
        }

        self.last_emitted_by_instrument
            .insert(msg.instrument.clone(), now);

        Some(build_signal(
            cfg,
            msg,
            self.strategy_id(),
            side,
            &format!(
                "ema-crossover fast={:.4} slow={:.4} gap={:.4} spread={:.4} sessionTicks={}",
                fast,
                slow,
                f.ema_gap.unwrap_or(0.0),
                f.spread,
                f.session_ticks
            ),
        ))
    }
}

// ── shared helper ─────────────────────────────────────────────────────────────

fn build_signal(
    cfg: &AppConfig,
    msg: &MarketDataMessage,
    strategy_id: &str,
    side: &str,
    reason: &str,
) -> TradeSignal {
    let signal_id = Uuid::new_v4().to_string();
    TradeSignal {
        message_type: "TradeSignal".to_string(),
        version: "v1".to_string(),
        timestamp: Utc::now(),
        source_id: "rust.strategy".to_string(),
        correlation_id: signal_id.clone(),
        signal_id,
        strategy_id: strategy_id.to_string(),
        account: cfg.allowed_account.clone(),
        instrument: msg.instrument.clone(),
        side: side.to_string(),
        quantity: 1,
        order_type: "Market".to_string(),
        reason: reason.to_string(),
    }
}

