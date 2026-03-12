use crate::state::MarketState;

#[derive(Debug, Clone)]
pub struct FeatureSnapshot {
    pub bid: f64,
    pub ask: f64,
    pub last: f64,
    pub spread: f64,
    pub mid_price: f64,
    pub momentum_3: Option<f64>,
    pub session_ticks: u64,
    /// 5-period EMA of last trade prices.
    pub ema_fast: Option<f64>,
    /// 20-period EMA of last trade prices.
    pub ema_slow: Option<f64>,
    /// fast EMA minus slow EMA; positive = uptrend regime, negative = downtrend.
    pub ema_gap: Option<f64>,
}

pub fn compute_features(state: &MarketState, instrument: &str) -> Option<FeatureSnapshot> {
    let snapshot = state.get(instrument)?;
    let bid = snapshot.bid?;
    let ask = snapshot.ask?;
    let last = snapshot.last?;

    let ema_fast = snapshot.ema_fast.value();
    let ema_slow = snapshot.ema_slow.value();
    let ema_gap = match (ema_fast, ema_slow) {
        (Some(f), Some(s)) => Some(f - s),
        _ => None,
    };

    Some(FeatureSnapshot {
        bid,
        ask,
        last,
        spread: ask - bid,
        mid_price: (ask + bid) / 2.0,
        momentum_3: snapshot.momentum_3(),
        session_ticks: snapshot.session.tick_count,
        ema_fast,
        ema_slow,
        ema_gap,
    })
}
