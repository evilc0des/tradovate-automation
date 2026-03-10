use std::collections::HashMap;
use std::time::{Duration, Instant};

use chrono::Utc;
use uuid::Uuid;

use crate::config::AppConfig;
use crate::features;
use crate::models::{MarketDataMessage, TradeSignal};
use crate::state::MarketState;

pub struct DeterministicStrategy {
    cooldown: Duration,
    last_emitted_by_instrument: HashMap<String, Instant>,
}

impl DeterministicStrategy {
    pub fn new(cooldown_ms: u64) -> Self {
        Self {
            cooldown: Duration::from_millis(cooldown_ms),
            last_emitted_by_instrument: HashMap::new(),
        }
    }

    pub fn on_market_data(
        &mut self,
        cfg: &AppConfig,
        state: &MarketState,
        msg: &MarketDataMessage,
    ) -> Option<TradeSignal> {
        if !cfg.allowed_instruments.iter().any(|i| i == &msg.instrument) {
            return None;
        }

        let now = Instant::now();
        if let Some(last) = self.last_emitted_by_instrument.get(&msg.instrument) {
            if now.duration_since(*last) < self.cooldown {
                return None;
            }
        }

        let f = features::compute_features(state, &msg.instrument)?;
        let bid = f.bid;
        let ask = f.ask;
        let last = f.last;

        let side = if last > ask {
            Some("Buy")
        } else if last < bid {
            Some("Sell")
        } else {
            None
        }?;

        self.last_emitted_by_instrument
            .insert(msg.instrument.clone(), now);

        let signal_id = Uuid::new_v4().to_string();
        Some(TradeSignal {
            message_type: "TradeSignal".to_string(),
            version: "v1".to_string(),
            timestamp: Utc::now(),
            source_id: "rust.strategy".to_string(),
            correlation_id: signal_id.clone(),
            signal_id,
            strategy_id: "deterministic-v1".to_string(),
            account: cfg.allowed_account.clone(),
            instrument: msg.instrument.clone(),
            side: side.to_string(),
            quantity: 1,
            order_type: "Market".to_string(),
            reason: format!(
                "deterministic threshold rule spread={:.4} mid={:.4} momentum3={:?} sessionTicks={}",
                f.spread, f.mid_price, f.momentum_3, f.session_ticks
            ),
        })
    }
}
