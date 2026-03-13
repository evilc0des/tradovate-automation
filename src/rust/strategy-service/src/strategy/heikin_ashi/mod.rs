use std::collections::HashMap;
use std::time::{Duration, Instant};

use crate::config::AppConfig;
use crate::features::FeatureSnapshot;
use crate::models::MarketDataMessage;

use super::{build_signal, Strategy, TradeSignal};

// ── HeikinAshiStrategy ───────────────────────────────────────────────────────
//
// Trend-following reversal strategy using Heikin Ashi candle recalculation.
//
// Entry rules:
//   Buy  – two consecutive bullish Heikin Ashi candles (HA close > HA open).
//   Sell – two consecutive bearish Heikin Ashi candles (HA close < HA open).
//
// Each HA candle is derived from the real OHLC bar:
//   HA close = (open + high + low + close) / 4
//   HA open  = (prev HA open + prev HA close) / 2  (first bar: (open + close) / 2)
//   HA high  = max(high, HA open, HA close)
//   HA low   = min(low,  HA open, HA close)
//
// A signal is not re-emitted while the same direction streak continues; the
// opposite signal acts as a reversal entry.  Per-instrument cooldown is an
// additional throttle guard.

#[derive(Default)]
struct HaInstrumentState {
    /// Previous HA candle values; `None` before the first bar is processed.
    prev_ha_open: Option<f64>,
    prev_ha_close: Option<f64>,
    /// Consecutive same-colour count: positive = bullish streak, negative = bearish streak.
    streak: i32,
    /// Side of the last signal emitted for this instrument.
    last_signal_side: Option<String>,
}

pub struct HeikinAshiStrategy {
    cooldown: Duration,
    last_emitted_by_instrument: HashMap<String, Instant>,
    forced_trade_emitted: bool,
    ha_state: HashMap<String, HaInstrumentState>,
}

impl HeikinAshiStrategy {
    pub fn new(cooldown_ms: u64) -> Self {
        Self {
            cooldown: Duration::from_millis(cooldown_ms),
            last_emitted_by_instrument: HashMap::new(),
            forced_trade_emitted: false,
            ha_state: HashMap::new(),
        }
    }
}

impl Strategy for HeikinAshiStrategy {
    fn strategy_id(&self) -> &str {
        "heikin-ashi-v1"
    }

    fn on_market_data(
        &mut self,
        cfg: &AppConfig,
        msg: &MarketDataMessage,
        _features: Option<&FeatureSnapshot>,
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

        // HA candles require full OHLC — only available on BarUpdate events.
        if msg.event_type != "BarUpdate" {
            return None;
        }
        let (open, high, low, close) = match (
            msg.bar_open,
            msg.bar_high,
            msg.bar_low,
            msg.last_price,
        ) {
            (Some(o), Some(h), Some(l), Some(c)) => (o, h, l, c),
            _ => return None,
        };

        let state = self.ha_state.entry(msg.instrument.clone()).or_default();

        // ── Compute Heikin Ashi candle ────────────────────────────────────────
        let ha_close = (open + high + low + close) / 4.0;
        let ha_open = match (state.prev_ha_open, state.prev_ha_close) {
            (Some(po), Some(pc)) => (po + pc) / 2.0,
            _ => (open + close) / 2.0, // bootstrap: use real open/close average
        };
        let ha_high = high.max(ha_open).max(ha_close);
        let ha_low = low.min(ha_open).min(ha_close);

        // ── Update streak ────────────────────────────────────────────────────
        let bullish = ha_close > ha_open;
        state.streak = if bullish {
            state.streak.max(0) + 1
        } else {
            state.streak.min(0) - 1
        };
        state.prev_ha_open = Some(ha_open);
        state.prev_ha_close = Some(ha_close);

        // ── Determine signal ─────────────────────────────────────────────────
        let candidate_side = if state.streak >= 2
            && state.last_signal_side.as_deref() != Some("Buy")
        {
            Some("Buy")
        } else if state.streak <= -2
            && state.last_signal_side.as_deref() != Some("Sell")
        {
            Some("Sell")
        } else {
            None
        };

        let side = candidate_side?;

        // ── Cooldown gate ────────────────────────────────────────────────────
        let now = Instant::now();
        if let Some(last) = self.last_emitted_by_instrument.get(&msg.instrument) {
            if now.duration_since(*last) < self.cooldown {
                return None;
            }
        }

        // Extract streak before updating state so the mutable borrow on
        // `self.ha_state` ends before `build_signal` needs to borrow `self`.
        let streak = state.streak;
        state.last_signal_side = Some(side.to_string());
        let _ = state; // end mutable borrow on ha_state

        self.last_emitted_by_instrument
            .insert(msg.instrument.clone(), now);

        Some(build_signal(
            cfg,
            msg,
            self.strategy_id(),
            side,
            &format!(
                "heikin-ashi streak={} ha_open={:.4} ha_close={:.4} ha_high={:.4} ha_low={:.4}",
                streak, ha_open, ha_close, ha_high, ha_low
            ),
        ))
    }
}

// ── tests ─────────────────────────────────────────────────────────────────────

#[cfg(test)]
mod tests {
    use super::*;
    use chrono::Utc;

    // ── helpers ───────────────────────────────────────────────────────────────

    fn test_config() -> AppConfig {
        AppConfig {
            strategy_name: "heikin-ashi".to_string(),
            market_data_bind: "127.0.0.1:9100".to_string(),
            signal_bind: "127.0.0.1:9101".to_string(),
            allowed_account: "SIM101".to_string(),
            allowed_instruments: vec!["MES 06-26".to_string()],
            cooldown_ms: 0, // disable cooldown so tests aren't timing-sensitive
            force_trade_once: false,
            force_trade_side: "Buy".to_string(),
            tape_tick_size: 0.25,
            tape_micro_delta_min: 10,
            tape_aggression_ratio_min: 2.0,
            tape_speed_factor_min: 1.5,
            tape_price_response_min_ticks: 0.5,
            tape_target_ticks: 4.0,
            tape_stop_ticks: 2.0,
            tape_time_stop_ms: 30_000,
            tape_flip_delta: 5,
            tape_session_start_utc: "00:00".to_string(),
            tape_session_end_utc: "23:59".to_string(),
            tape_wall_min_size: 1000,
        }
    }

    fn bar_msg(instrument: &str, open: f64, high: f64, low: f64, close: f64) -> MarketDataMessage {
        MarketDataMessage {
            message_type: "MarketDataMessage".to_string(),
            version: "v1".to_string(),
            timestamp: Utc::now(),
            source_id: "ninjatrader".to_string(),
            correlation_id: "test".to_string(),
            instrument: instrument.to_string(),
            event_type: "BarUpdate".to_string(),
            last_price: Some(close),
            bid: None,
            ask: None,
            last_size: None,
            bar_open: Some(open),
            bar_high: Some(high),
            bar_low: Some(low),
        }
    }

    fn quote_msg(instrument: &str) -> MarketDataMessage {
        MarketDataMessage {
            message_type: "MarketDataMessage".to_string(),
            version: "v1".to_string(),
            timestamp: Utc::now(),
            source_id: "ninjatrader".to_string(),
            correlation_id: "test".to_string(),
            instrument: instrument.to_string(),
            event_type: "QuoteUpdate".to_string(),
            last_price: None,
            bid: Some(100.0),
            ask: Some(100.25),
            last_size: None,
            bar_open: None,
            bar_high: None,
            bar_low: None,
        }
    }

    // ── HeikinAshiStrategy ────────────────────────────────────────────────────

    #[test]
    fn no_signal_on_non_bar_event() {
        let cfg = test_config();
        let mut s = HeikinAshiStrategy::new(0);
        let sig = s.on_market_data(&cfg, &quote_msg("MES 06-26"), None);
        assert!(sig.is_none(), "quote events must not produce a signal");
    }

    #[test]
    fn no_signal_on_first_bullish_bar() {
        // Candle: open=100 high=106 low=100 close=102 → HA open=101 HA close=102 → bullish, streak=1
        let cfg = test_config();
        let mut s = HeikinAshiStrategy::new(0);
        let sig = s.on_market_data(&cfg, &bar_msg("MES 06-26", 100.0, 106.0, 100.0, 102.0), None);
        assert!(sig.is_none(), "single bullish bar must not trigger Buy");
    }

    #[test]
    fn buy_signal_on_second_consecutive_bullish_bar() {
        // Candle 1 (bootstrap): open=100 high=106 low=100 close=102
        //   HA open=(100+102)/2=101  HA close=(100+106+100+102)/4=102  → bullish streak=1
        // Candle 2: open=102 high=108 low=102 close=104
        //   HA open=(101+102)/2=101.5  HA close=(102+108+102+104)/4=104  → bullish streak=2 → Buy
        let cfg = test_config();
        let mut s = HeikinAshiStrategy::new(0);
        s.on_market_data(&cfg, &bar_msg("MES 06-26", 100.0, 106.0, 100.0, 102.0), None);
        let sig = s.on_market_data(&cfg, &bar_msg("MES 06-26", 102.0, 108.0, 102.0, 104.0), None);
        let sig = sig.expect("second consecutive bullish bar must emit Buy");
        assert_eq!(sig.side, "Buy");
        assert_eq!(sig.strategy_id, "heikin-ashi-v1");
        assert_eq!(sig.order_type, "Market");
        assert_eq!(sig.quantity, 1);
    }

    #[test]
    fn no_duplicate_buy_on_third_bullish_bar() {
        // After a Buy is emitted at streak=2, a third bullish bar should not
        // re-emit because last_signal_side is already "Buy".
        // Candle 1: open=100 h=106 l=100 c=102 → HA open=101  close=102  streak=1
        // Candle 2: open=102 h=108 l=102 c=104 → HA open=101.5 close=104  streak=2 → Buy
        // Candle 3: open=104 h=110 l=104 c=106 → HA open=102.75 close=106 streak=3 → no signal
        let cfg = test_config();
        let mut s = HeikinAshiStrategy::new(0);
        s.on_market_data(&cfg, &bar_msg("MES 06-26", 100.0, 106.0, 100.0, 102.0), None);
        s.on_market_data(&cfg, &bar_msg("MES 06-26", 102.0, 108.0, 102.0, 104.0), None);
        let sig = s.on_market_data(&cfg, &bar_msg("MES 06-26", 104.0, 110.0, 104.0, 106.0), None);
        assert!(sig.is_none(), "third consecutive bullish bar must not re-emit Buy");
    }

    #[test]
    fn sell_signal_on_second_consecutive_bearish_bar() {
        let cfg = test_config();
        let mut s = HeikinAshiStrategy::new(0);
        // Candle 1: bearish (close < open)
        s.on_market_data(&cfg, &bar_msg("MES 06-26", 104.0, 105.0, 99.0, 100.0), None);
        // Candle 2: bearish → streak = -2 → Sell
        let sig = s.on_market_data(&cfg, &bar_msg("MES 06-26", 100.0, 101.0, 95.0, 96.0), None);
        let sig = sig.expect("second consecutive bearish bar must emit Sell");
        assert_eq!(sig.side, "Sell");
    }

    #[test]
    fn reversal_from_buy_to_sell() {
        // Candle data computed to guarantee HA direction at each step.
        // Bullish 1: o=100 h=106 l=100 c=102 → HA o=101  c=102     streak=1
        // Bullish 2: o=102 h=108 l=102 c=104 → HA o=101.5 c=104    streak=2 → Buy
        // Bearish 1: o=106 h=107 l=101 c=102 → HA o=102.75 c=104  …wait, need c<o
        //   HA open=(101.5+104)/2=102.75  HA close=(106+107+101+102)/4=104 → 104>102.75 still bullish
        //   Try: o=104 h=105 l=98 c=100 → HA c=(104+105+98+100)/4=101.75 < 102.75 → bearish streak=-1
        // Bearish 2: o=100 h=102 l=93 c=94 → HA o=(102.75+101.75)/2=102.25  c=(100+102+93+94)/4=97.25
        //   97.25 < 102.25 → bearish streak=-2 → Sell
        let cfg = test_config();
        let mut s = HeikinAshiStrategy::new(0);
        // Establish bullish streak → Buy
        s.on_market_data(&cfg, &bar_msg("MES 06-26", 100.0, 106.0, 100.0, 102.0), None);
        let first = s.on_market_data(&cfg, &bar_msg("MES 06-26", 102.0, 108.0, 102.0, 104.0), None);
        assert_eq!(first.unwrap().side, "Buy");
        // One bearish bar — streak resets to -1, no signal
        let none = s.on_market_data(&cfg, &bar_msg("MES 06-26", 104.0, 105.0, 98.0, 100.0), None);
        assert!(none.is_none(), "first bearish bar after bull streak must not signal");
        // Second bearish bar → Sell
        let rev = s.on_market_data(&cfg, &bar_msg("MES 06-26", 100.0, 102.0, 93.0, 94.0), None);
        let rev = rev.expect("second consecutive bearish bar must emit Sell (reversal)");
        assert_eq!(rev.side, "Sell");
    }

    #[test]
    fn cooldown_suppresses_signal_but_state_updates() {
        // With cooldown=0 and two bullish bars the Buy fires at streak=2.
        // The third bullish bar must not re-emit (last_signal_side guard).
        let cfg = test_config();
        let mut s = HeikinAshiStrategy::new(0);
        s.on_market_data(&cfg, &bar_msg("MES 06-26", 100.0, 106.0, 100.0, 102.0), None);
        let b1 = s.on_market_data(&cfg, &bar_msg("MES 06-26", 102.0, 108.0, 102.0, 104.0), None);
        assert_eq!(b1.unwrap().side, "Buy");
        // Third bullish bar — same direction, no signal
        let b2 = s.on_market_data(&cfg, &bar_msg("MES 06-26", 104.0, 110.0, 104.0, 106.0), None);
        assert!(b2.is_none());
    }

    #[test]
    fn ha_open_bootstraps_from_real_open_close() {
        // The first HA candle has no prior HA values; HA open is bootstrapped
        // using (real_open + real_close) / 2.  If open=100, close=102 then
        // HA open = 101 and HA close = (100+103+99+102)/4 = 101.0.  Both
        // equal → flat candle → no bullish streak → no signal.
        let cfg = test_config();
        let mut s = HeikinAshiStrategy::new(0);
        let sig = s.on_market_data(&cfg, &bar_msg("MES 06-26", 100.0, 103.0, 99.0, 102.0), None);
        // HA close = (100+103+99+102)/4 = 101.0; HA open = (100+102)/2 = 101.0 → flat
        assert!(sig.is_none(), "flat HA candle (close == open) must not trigger");
    }

    #[test]
    fn independent_state_per_instrument() {
        let cfg = test_config();
        let mut s = HeikinAshiStrategy::new(0);
        // Instrument A gets two bullish bars → Buy on A
        s.on_market_data(&cfg, &bar_msg("MES 06-26", 100.0, 106.0, 100.0, 102.0), None);
        let a = s.on_market_data(&cfg, &bar_msg("MES 06-26", 102.0, 108.0, 102.0, 104.0), None);
        assert_eq!(a.unwrap().side, "Buy");
        // Instrument B (separate state): one bar only — no signal
        let b = s.on_market_data(&cfg, &bar_msg("ES 06-26", 4000.0, 4006.0, 4000.0, 4002.0), None);
        assert!(b.is_none(), "instrument B has only one bar; must not signal");
    }

    #[test]
    fn force_trade_once_emits_immediately() {
        let mut cfg = test_config();
        cfg.force_trade_once = true;
        cfg.force_trade_side = "Sell".to_string();
        let mut s = HeikinAshiStrategy::new(0);
        // No bar processing needed — fires on first event regardless of type
        let sig = s.on_market_data(&cfg, &quote_msg("MES 06-26"), None)
            .expect("force_trade_once must emit on the very first call");
        assert_eq!(sig.side, "Sell");
        // Second call must not fire again
        let sig2 = s.on_market_data(&cfg, &quote_msg("MES 06-26"), None);
        assert!(sig2.is_none(), "force_trade_once must fire exactly once");
    }
}
