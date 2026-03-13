use std::collections::{HashMap, VecDeque};

use chrono::{DateTime, Utc};

// ── Exponential Moving Average ────────────────────────────────────────────────

#[derive(Clone, Debug)]
pub struct Ema {
    alpha: f64,
    value: Option<f64>,
}

impl Ema {
    pub fn new(period: u32) -> Self {
        Self {
            alpha: 2.0 / (period as f64 + 1.0),
            value: None,
        }
    }

    pub fn update(&mut self, price: f64) {
        self.value = Some(match self.value {
            None => price,
            Some(prev) => prev + self.alpha * (price - prev),
        });
    }

    pub fn value(&self) -> Option<f64> {
        self.value
    }
}

// ── InstrumentState ───────────────────────────────────────────────────────────

#[derive(Clone, Debug)]
pub struct InstrumentState {
    pub bid: Option<f64>,
    pub ask: Option<f64>,
    pub last: Option<f64>,
    pub rolling: RollingBars,
    pub session: SessionState,
    /// 5-period EMA of last trade prices.
    pub ema_fast: Ema,
    /// 20-period EMA of last trade prices.
    pub ema_slow: Ema,
    /// 14-period EMA of True Range (ATR).
    pub atr_ema: Ema,
    /// Close of the previous completed bar; used for True Range computation.
    pub prev_bar_close: Option<f64>,
    /// Rolling tape micro-structure state for the tape-burst scalper.
    pub tape: TapeState,
}

impl Default for InstrumentState {
    fn default() -> Self {
        Self {
            bid: None,
            ask: None,
            last: None,
            rolling: RollingBars::default(),
            session: SessionState::default(),
            ema_fast: Ema::new(5),
            ema_slow: Ema::new(20),
            atr_ema: Ema::new(14),
            prev_bar_close: None,
            tape: TapeState::default(),
        }
    }
}

#[derive(Clone, Debug)]
pub struct RollingBars {
    max_len: usize,
    closes: VecDeque<f64>,
}

impl Default for RollingBars {
    fn default() -> Self {
        Self {
            max_len: 32,
            closes: VecDeque::new(),
        }
    }
}

impl RollingBars {
    pub fn push_close(&mut self, close: f64) {
        self.closes.push_back(close);
        while self.closes.len() > self.max_len {
            let _ = self.closes.pop_front();
        }
    }

    pub fn momentum_n(&self, n: usize) -> Option<f64> {
        if self.closes.len() < n || n == 0 {
            return None;
        }
        let newest = self.closes.back()?;
        let oldest = self.closes.get(self.closes.len() - n)?;
        Some(newest - oldest)
    }
}

#[derive(Clone, Debug, Default)]
pub struct SessionState {
    pub first_seen: Option<DateTime<Utc>>,
    pub last_seen: Option<DateTime<Utc>>,
    pub tick_count: u64,
}

#[derive(Clone, Debug, Default)]
pub struct MarketState {
    by_instrument: HashMap<String, InstrumentState>,
}

impl MarketState {
    pub fn update_quote(
        &mut self,
        instrument: &str,
        bid: Option<f64>,
        ask: Option<f64>,
        last: Option<f64>,
        timestamp: DateTime<Utc>,
    ) {
        let entry = self.by_instrument.entry(instrument.to_string()).or_default();
        if let Some(value) = bid {
            entry.bid = Some(value);
        }
        if let Some(value) = ask {
            entry.ask = Some(value);
        }
        if let Some(value) = last {
            entry.last = Some(value);
            entry.rolling.push_close(value);
        }

        if entry.session.first_seen.is_none() {
            entry.session.first_seen = Some(timestamp);
        }
        entry.session.last_seen = Some(timestamp);
        entry.session.tick_count = entry.session.tick_count.saturating_add(1);
    }

    /// Update EMAs and ATR from a completed bar (high, low, close).
    /// Must be called instead of (or in addition to) `update_quote` for
    /// bar-based strategies.
    pub fn update_bar_close(&mut self, instrument: &str, high: f64, low: f64, close: f64) {
        let entry = self.by_instrument.entry(instrument.to_string()).or_default();
        entry.ema_fast.update(close);
        entry.ema_slow.update(close);

        // True Range = max(high-low, |high-prev_close|, |low-prev_close|)
        let tr = match entry.prev_bar_close {
            Some(prev) => {
                let hl = high - low;
                let hc = (high - prev).abs();
                let lc = (low  - prev).abs();
                hl.max(hc).max(lc)
            }
            None => high - low, // first bar: fall back to simple range
        };
        entry.atr_ema.update(tr);
        entry.prev_bar_close = Some(close);
    }

    pub fn get(&self, instrument: &str) -> Option<&InstrumentState> {
        self.by_instrument.get(instrument)
    }

    /// Record a trade print in the rolling tape for `instrument`.
    pub fn update_tape_print(
        &mut self,
        instrument: &str,
        timestamp: DateTime<Utc>,
        price: f64,
        size: u64,
        aggressor_side: Option<&str>,
    ) {
        let entry = self.by_instrument.entry(instrument.to_string()).or_default();
        entry.tape.push_print(timestamp, price, size, aggressor_side);
    }

    /// Record a quote snapshot in the rolling tape for `instrument`.
    pub fn update_tape_quote(
        &mut self,
        instrument: &str,
        timestamp: DateTime<Utc>,
        bid: f64,
        ask: f64,
        bid_size: u32,
        ask_size: u32,
    ) {
        let entry = self.by_instrument.entry(instrument.to_string()).or_default();
        entry.tape.push_quote(timestamp, bid, ask, bid_size, ask_size);
    }
}

impl InstrumentState {
    pub fn momentum_3(&self) -> Option<f64> {
        self.rolling.momentum_n(3)
    }
}

// ── Tape State ────────────────────────────────────────────────────────────────

/// Hard cap on events stored per instrument to bound memory usage.
const TAPE_MAX_EVENTS: usize = 2_000;

#[derive(Clone, Debug)]
pub struct TapePrint {
    pub timestamp: DateTime<Utc>,
    pub price: f64,
    pub size: u64,
    /// `true` = buy-side aggressor (hit the ask),
    /// `false` = sell-side aggressor (hit the bid).
    pub is_buy: bool,
}

#[allow(dead_code)]
#[derive(Clone, Debug)]
pub struct TapeQuote {
    pub timestamp: DateTime<Utc>,
    pub bid: f64,
    pub ask: f64,
    pub bid_size: u32,
    pub ask_size: u32,
}

/// Per-instrument rolling tape — stores recent prints and quotes for
/// micro-structure analysis by the tape-burst scalper.
#[derive(Clone, Debug, Default)]
pub struct TapeState {
    pub prints: VecDeque<TapePrint>,
    pub quotes: VecDeque<TapeQuote>,
    /// Most-recently-seen best bid (used for aggressor-side inference).
    pub current_bid: Option<f64>,
    /// Most-recently-seen best ask.
    pub current_ask: Option<f64>,
}

impl TapeState {
    /// Record a trade print.  `aggressor_side` should be `"Buy"`, `"Sell"`,
    /// or absent / `"Unknown"`.  When the side is unknown, it is inferred
    /// from the current bid/ask: `price >= ask` → buy; `price <= bid` → sell;
    /// otherwise defaults to buy.
    pub fn push_print(
        &mut self,
        timestamp: DateTime<Utc>,
        price: f64,
        size: u64,
        aggressor_side: Option<&str>,
    ) {
        let is_buy = match aggressor_side {
            Some("Buy") => true,
            Some("Sell") => false,
            _ => match (self.current_bid, self.current_ask) {
                (_, Some(ask)) if price >= ask => true,
                (Some(bid), _) if price <= bid => false,
                _ => true, // ambiguous — default to buy
            },
        };
        self.prints
            .push_back(TapePrint { timestamp, price, size, is_buy });
        while self.prints.len() > TAPE_MAX_EVENTS {
            self.prints.pop_front();
        }
    }

    /// Record a quote snapshot and update the current bid/ask used for
    /// aggressor-side inference on subsequent prints.
    pub fn push_quote(
        &mut self,
        timestamp: DateTime<Utc>,
        bid: f64,
        ask: f64,
        bid_size: u32,
        ask_size: u32,
    ) {
        self.current_bid = Some(bid);
        self.current_ask = Some(ask);
        self.quotes
            .push_back(TapeQuote { timestamp, bid, ask, bid_size, ask_size });
        while self.quotes.len() > TAPE_MAX_EVENTS {
            self.quotes.pop_front();
        }
    }

    /// Compute aggregated metrics for all prints with `timestamp >= since`.
    pub fn window_metrics(&self, since: DateTime<Utc>) -> TapeWindowMetrics {
        let mut buy_vol: u64 = 0;
        let mut sell_vol: u64 = 0;
        let mut count: u64 = 0;
        let mut first_price: Option<f64> = None;
        let mut last_price: Option<f64> = None;
        let mut upticks: u64 = 0;
        let mut downticks: u64 = 0;
        let mut prev: Option<f64> = None;

        for p in self.prints.iter().filter(|p| p.timestamp >= since) {
            if p.is_buy {
                buy_vol += p.size;
            } else {
                sell_vol += p.size;
            }
            count += 1;
            if first_price.is_none() {
                first_price = Some(p.price);
            }
            last_price = Some(p.price);
            if let Some(pr) = prev {
                match p.price.partial_cmp(&pr) {
                    Some(std::cmp::Ordering::Greater) => upticks += 1,
                    Some(std::cmp::Ordering::Less) => downticks += 1,
                    _ => {}
                }
            }
            prev = Some(p.price);
        }

        TapeWindowMetrics {
            buy_vol,
            sell_vol,
            print_count: count,
            price_change: match (first_price, last_price) {
                (Some(f), Some(l)) => l - f,
                _ => 0.0,
            },
            upticks,
            downticks,
        }
    }

    /// Size of the most-recently-seen best ask (L1 wall proxy for long entries).
    pub fn near_ask_size(&self) -> Option<u32> {
        self.quotes.back().map(|q| q.ask_size)
    }

    /// Size of the most-recently-seen best bid (L1 wall proxy for short entries).
    pub fn near_bid_size(&self) -> Option<u32> {
        self.quotes.back().map(|q| q.bid_size)
    }
}

/// Aggregated metrics over a tape rolling window.
#[derive(Clone, Debug, Default)]
pub struct TapeWindowMetrics {
    pub buy_vol: u64,
    pub sell_vol: u64,
    pub print_count: u64,
    /// Last-price minus first-price within the window (raw price units, not ticks).
    pub price_change: f64,
    pub upticks: u64,
    pub downticks: u64,
}

impl TapeWindowMetrics {
    /// Signed volume imbalance: buy-aggressor volume minus sell-aggressor volume.
    pub fn micro_delta(&self) -> i64 {
        self.buy_vol as i64 - self.sell_vol as i64
    }
}

// ── tests ─────────────────────────────────────────────────────────────────────

#[cfg(test)]
mod tests {
    use super::*;
    use chrono::Utc;

    // ── Ema ───────────────────────────────────────────────────────────────────

    #[test]
    fn ema_seeds_on_first_value() {
        let mut ema = Ema::new(5);
        assert!(ema.value().is_none());
        ema.update(100.0);
        assert_eq!(ema.value(), Some(100.0));
    }

    #[test]
    fn ema_moves_toward_new_price() {
        let mut ema = Ema::new(5);
        ema.update(100.0); // seed
        ema.update(120.0); // should move toward 120
        let val = ema.value().unwrap();
        assert!(val > 100.0 && val < 120.0, "EMA must be between seed and new price, got {val}");
    }

    #[test]
    fn ema_alpha_for_period_5_is_correct() {
        // alpha = 2 / (5 + 1) = 0.333…
        // After seed=100, update=120: next = 100 + 0.333*(120-100) = 106.667
        let mut e = Ema::new(5);
        e.update(100.0);
        e.update(120.0);
        let expected = 100.0 + (2.0 / 6.0) * 20.0;
        let actual = e.value().unwrap();
        assert!((actual - expected).abs() < 1e-9, "EMA value mismatch: {actual} vs {expected}");
    }

    // ── RollingBars ───────────────────────────────────────────────────────────

    #[test]
    fn rolling_bars_momentum_none_when_insufficient_data() {
        let mut rb = RollingBars::default();
        rb.push_close(100.0);
        rb.push_close(101.0);
        assert!(rb.momentum_n(3).is_none(), "momentum_3 needs 3 closes");
    }

    #[test]
    fn rolling_bars_momentum_computes_correctly() {
        let mut rb = RollingBars::default();
        rb.push_close(100.0);
        rb.push_close(102.0);
        rb.push_close(105.0);
        let m = rb.momentum_n(3).unwrap();
        assert!((m - 5.0).abs() < 1e-9, "momentum_3 = newest - oldest = 105 - 100 = 5");
    }

    #[test]
    fn rolling_bars_caps_at_max_len() {
        let mut rb = RollingBars::default();
        for i in 0..40 {
            rb.push_close(i as f64);
        }
        // Default max_len = 32; we should have 32 entries.
        assert_eq!(rb.closes.len(), 32);
        // The oldest entry should be 40 - 32 = 8.
        assert_eq!(rb.closes.front(), Some(&8.0));
    }

    // ── MarketState ───────────────────────────────────────────────────────────

    #[test]
    fn update_quote_stores_bid_ask_last() {
        let mut state = MarketState::default();
        let ts = Utc::now();
        state.update_quote("MES 06-26", Some(4990.0), Some(4991.0), Some(4990.5), ts);
        let inst = state.get("MES 06-26").unwrap();
        assert_eq!(inst.bid, Some(4990.0));
        assert_eq!(inst.ask, Some(4991.0));
        assert_eq!(inst.last, Some(4990.5));
    }

    #[test]
    fn update_quote_increments_tick_count() {
        let mut state = MarketState::default();
        let ts = Utc::now();
        state.update_quote("MES 06-26", Some(100.0), Some(101.0), Some(100.5), ts);
        state.update_quote("MES 06-26", Some(100.0), Some(101.0), Some(100.5), ts);
        let inst = state.get("MES 06-26").unwrap();
        assert_eq!(inst.session.tick_count, 2);
    }

    #[test]
    fn update_bar_close_warms_emas() {
        let mut state = MarketState::default();
        state.update_bar_close("MES 06-26", 101.0, 99.0, 100.0);
        let inst = state.get("MES 06-26").unwrap();
        assert_eq!(inst.ema_fast.value(), Some(100.0));
        assert_eq!(inst.ema_slow.value(), Some(100.0));
    }

    #[test]
    fn update_bar_close_computes_atr_from_second_bar() {
        let mut state = MarketState::default();
        // First bar: no prev_close → TR = high - low = 2
        state.update_bar_close("MES 06-26", 101.0, 99.0, 100.0);
        // Second bar: prev_close=100, high=102, low=99
        // TR = max(102-99=3, |102-100|=2, |99-100|=1) = 3
        state.update_bar_close("MES 06-26", 102.0, 99.0, 100.5);
        let inst = state.get("MES 06-26").unwrap();
        let atr = inst.atr_ema.value().unwrap();
        // After seed=2, update=3: atr = 2 + alpha*(3-2) = 2 + (2/15) ≈ 2.133
        let expected_seed = 2.0_f64;
        let alpha = 2.0_f64 / 15.0;
        let expected = expected_seed + alpha * (3.0 - expected_seed);
        assert!((atr - expected).abs() < 1e-9, "ATR={atr}, expected {expected}");
    }

    #[test]
    fn instruments_are_tracked_independently() {
        let mut state = MarketState::default();
        let ts = Utc::now();
        state.update_quote("MES 06-26", Some(100.0), Some(101.0), Some(100.5), ts);
        state.update_quote("NQ 06-26", Some(19000.0), Some(19001.0), Some(19000.5), ts);
        assert_eq!(state.get("MES 06-26").unwrap().bid, Some(100.0));
        assert_eq!(state.get("NQ 06-26").unwrap().bid, Some(19000.0));
    }

    #[test]
    fn get_returns_none_for_unknown_instrument() {
        let state = MarketState::default();
        assert!(state.get("UNKNOWN").is_none());
    }

    // ── TapeState ─────────────────────────────────────────────────────────────

    #[test]
    fn tape_print_known_buy_side_stored_correctly() {
        let mut tape = TapeState::default();
        let ts = Utc::now();
        tape.push_print(ts, 100.5, 10, Some("Buy"));
        let p = tape.prints.back().unwrap();
        assert!(p.is_buy);
        assert_eq!(p.size, 10);
    }

    #[test]
    fn tape_print_unknown_side_inferred_from_quote() {
        let mut tape = TapeState::default();
        let ts = Utc::now();
        tape.push_quote(ts, 100.0, 101.0, 10, 10);
        // price == ask → buy aggressor
        tape.push_print(ts, 101.0, 5, None);
        assert!(tape.prints.back().unwrap().is_buy);
        // price == bid → sell aggressor
        tape.push_print(ts, 100.0, 5, None);
        assert!(!tape.prints.back().unwrap().is_buy);
    }

    #[test]
    fn tape_window_metrics_buy_sell_aggregation() {
        let mut tape = TapeState::default();
        let t0 = Utc::now();
        tape.push_print(t0, 100.0, 5, Some("Buy"));
        tape.push_print(t0, 100.0, 3, Some("Sell"));
        tape.push_print(t0, 100.0, 2, Some("Buy"));

        // Since-epoch should include all prints.
        let metrics = tape.window_metrics(t0 - chrono::Duration::seconds(1));
        assert_eq!(metrics.buy_vol, 7);
        assert_eq!(metrics.sell_vol, 3);
        assert_eq!(metrics.print_count, 3);
        assert_eq!(metrics.micro_delta(), 4);
    }

    #[test]
    fn tape_window_metrics_excludes_old_prints() {
        let mut tape = TapeState::default();
        let old_ts = Utc::now() - chrono::Duration::seconds(10);
        let recent_ts = Utc::now();
        tape.push_print(old_ts, 100.0, 100, Some("Buy")); // outside window
        tape.push_print(recent_ts, 100.0, 1, Some("Sell")); // inside window

        let metrics = tape.window_metrics(Utc::now() - chrono::Duration::seconds(5));
        assert_eq!(metrics.buy_vol, 0);
        assert_eq!(metrics.sell_vol, 1);
        assert_eq!(metrics.print_count, 1);
    }

    #[test]
    fn tape_caps_at_max_events() {
        let mut tape = TapeState::default();
        let ts = Utc::now();
        for _ in 0..(TAPE_MAX_EVENTS + 50) {
            tape.push_print(ts, 100.0, 1, Some("Buy"));
        }
        assert_eq!(tape.prints.len(), TAPE_MAX_EVENTS);
    }
}
