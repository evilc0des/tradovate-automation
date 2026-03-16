use std::collections::HashMap;
use std::time::{Duration, Instant};

use crate::config::AppConfig;
use crate::features::FeatureSnapshot;
use crate::models::MarketDataMessage;

use super::{build_exit_signal, build_signal, Strategy, TradeSignal};

// ── EmaMomentumStrategy ───────────────────────────────────────────────────────
//
// Trend-following crossover strategy.
//
// Entry rules:
//   Buy  – 5-period EMA crosses above 20-period EMA (momentum turning up).
//   Sell – 5-period EMA crosses below 20-period EMA (momentum turning down).
//
// Exit rules (candle-close based, bypass cooldown):
//   Exit Long  – bar closes below the 20-period slow EMA.
//   Exit Short – bar closes above the 20-period slow EMA.
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
    /// Currently tracked position per instrument: "Long" or "Short".
    /// Absent means flat (no known open position).
    open_position: HashMap<String, String>,
}

impl EmaMomentumStrategy {
    pub fn new(cooldown_ms: u64) -> Self {
        Self {
            cooldown: Duration::from_millis(cooldown_ms),
            last_emitted_by_instrument: HashMap::new(),
            forced_trade_emitted: false,
            prev_fast_above_slow: HashMap::new(),
            open_position: HashMap::new(),
        }
    }
}

impl Strategy for EmaMomentumStrategy {
    fn strategy_id(&self) -> &str {
        "ema-momentum-v1"
    }

    fn has_open_position(&self, instrument: &str) -> bool {
        self.open_position.contains_key(instrument)
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

        // ATR may not be warm until the first bar arrives; use it only when
        // available.  Exit threshold defaults to 0.0 (i.e. any breach
        // triggers) until ATR is populated so we don't silently miss exits.
        let atr_threshold = f.atr.map(|a| 0.2 * a).unwrap_or(0.0);

        // ── Candle-close exit check (BarUpdate only) ──────────────────────────
        // Evaluated before entry logic; exits bypass the cooldown gate.
        if msg.event_type == "BarUpdate" {
            if let Some(bar_close) = msg.last_price {
                let exit_side = match self.open_position.get(&msg.instrument).map(String::as_str) {
                    Some("Long")  if bar_close < slow - atr_threshold => Some("Sell"),
                    Some("Short") if bar_close > slow + atr_threshold => Some("Buy"),
                    _ => None,
                };
                if let Some(side) = exit_side {
                    self.open_position.remove(&msg.instrument);
                    // Reset regime so the next entry requires a fresh crossover.
                    self.prev_fast_above_slow.remove(&msg.instrument);
                    return Some(build_exit_signal(
                        cfg,
                        msg,
                        self.strategy_id(),
                        side,
                        &format!(
                            "ema-exit bar_close={:.4} slow_ema={:.4} atr={:.4} threshold={:.4}",
                            bar_close, slow, f.atr.unwrap_or(0.0), atr_threshold
                        ),
                    ));
                }
            }
        }

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

        // Record the new position so the exit check has something to act on.
        let position = if side == "Buy" { "Long" } else { "Short" };
        self.open_position
            .insert(msg.instrument.clone(), position.to_string());

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
