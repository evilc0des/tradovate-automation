use chrono::Utc;
use uuid::Uuid;

use crate::config::AppConfig;
use crate::features::FeatureSnapshot;
use crate::models::{MarketDataMessage, TradeSignal};

mod deterministic;
mod ema_momentum;
mod heikin_ashi;
mod tape_burst_scalper;

pub use deterministic::DeterministicStrategy;
pub use ema_momentum::EmaMomentumStrategy;
pub use heikin_ashi::HeikinAshiStrategy;
pub use tape_burst_scalper::TapeBurstScalperStrategy;

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

    /// Returns `true` if the strategy believes it holds an open position for
    /// `instrument`.  Used by the main loop to decide whether to emit a flatten
    /// signal when entering an event blackout window.
    fn has_open_position(&self, instrument: &str) -> bool;
}

// ── Factory ───────────────────────────────────────────────────────────────────

pub fn build_strategy(cfg: &AppConfig) -> Box<dyn Strategy> {
    match cfg.strategy_name.as_str() {
        "ema-momentum" => Box::new(EmaMomentumStrategy::new(cfg.cooldown_ms)),
        "heikin-ashi" => Box::new(HeikinAshiStrategy::new(cfg.cooldown_ms)),
        "tape-burst-scalper" => Box::new(TapeBurstScalperStrategy::new(cfg.cooldown_ms)),
        _ => Box::new(DeterministicStrategy::new(cfg.cooldown_ms)),
    }
}

// ── Shared signal builders ────────────────────────────────────────────────────

/// Builds a new-entry signal (default case).
pub(crate) fn build_signal(
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
        instruction: Some("entry".to_string()),
    }
}

/// Builds a strategy-driven exit signal (target, stop, flow-failure, etc.).
pub(crate) fn build_exit_signal(
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
        instruction: Some("exit".to_string()),
    }
}

/// Builds a forced-flatten signal that instructs the bridge to close all open
/// positions for `instrument`.  Emitted by the main loop when entering a
/// blackout window while the strategy holds a tracked position.
pub(crate) fn build_flatten_signal(
    cfg: &AppConfig,
    msg: &MarketDataMessage,
    strategy_id: &str,
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
        side: "Sell".to_string(), // advisory; bridge uses instruction=flatten to close all
        quantity: 1,
        order_type: "Market".to_string(),
        reason: reason.to_string(),
        instruction: Some("flatten".to_string()),
    }
}
