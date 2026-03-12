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
            entry.ema_fast.update(value);
            entry.ema_slow.update(value);
        }

        if entry.session.first_seen.is_none() {
            entry.session.first_seen = Some(timestamp);
        }
        entry.session.last_seen = Some(timestamp);
        entry.session.tick_count = entry.session.tick_count.saturating_add(1);
    }

    pub fn get(&self, instrument: &str) -> Option<&InstrumentState> {
        self.by_instrument.get(instrument)
    }
}

impl InstrumentState {
    pub fn momentum_3(&self) -> Option<f64> {
        self.rolling.momentum_n(3)
    }
}
