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
}

pub fn compute_features(state: &MarketState, instrument: &str) -> Option<FeatureSnapshot> {
    let snapshot = state.get(instrument)?;
    let bid = snapshot.bid?;
    let ask = snapshot.ask?;
    let last = snapshot.last?;

    Some(FeatureSnapshot {
        bid,
        ask,
        last,
        spread: ask - bid,
        mid_price: (ask + bid) / 2.0,
        momentum_3: snapshot.momentum_3(),
        session_ticks: snapshot.session.tick_count,
    })
}
