use std::collections::HashMap;
use std::time::{Duration, Instant};

use crate::config::AppConfig;
use crate::features::FeatureSnapshot;
use crate::models::MarketDataMessage;

use super::{build_signal, Strategy, TradeSignal};

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
