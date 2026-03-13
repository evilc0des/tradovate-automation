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
