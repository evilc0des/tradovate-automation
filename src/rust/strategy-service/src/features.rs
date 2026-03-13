use chrono::{DateTime, Utc};

use crate::state::MarketState;

/// Tape micro-structure features derived from rolling time windows.
/// All `prints_per_sec` values use the window length as the denominator
/// (e.g. `count_2s / 2.0`) for stable rate estimates.
#[allow(dead_code)]
#[derive(Debug, Clone)]
pub struct TapeFeatures {
    // ── 2-second window ──────────────────────────────────────────────────────
    pub buy_vol_2s: u64,
    pub sell_vol_2s: u64,
    /// Signed buy-minus-sell volume in the 2-second window.
    pub micro_delta_2s: i64,
    pub print_count_2s: u64,
    pub prints_per_sec_2s: f64,
    /// Raw price change (last – first) within the 2-second window.
    pub price_change_2s: f64,
    pub upticks_2s: u64,
    pub downticks_2s: u64,
    // ── 1-second window ──────────────────────────────────────────────────────
    pub micro_delta_1s: i64,
    pub print_count_1s: u64,
    pub prints_per_sec_1s: f64,
    // ── 500-millisecond window ────────────────────────────────────────────────
    pub micro_delta_500ms: i64,
    pub print_count_500ms: u64,
    pub prints_per_sec_500ms: f64,
    // ── 5-second baseline window ──────────────────────────────────────────────
    pub print_count_5s: u64,
    /// Baseline prints-per-second: `print_count_5s / 5.0`.
    pub prints_per_sec_5s: f64,
    // ── L1 depth proxies (most-recent quote snapshot) ─────────────────────────
    pub near_ask_size: Option<u32>,
    pub near_bid_size: Option<u32>,
}

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
    /// Tape micro-structure features; `None` until the first trade print
    /// has been received for this instrument.
    pub tape: Option<TapeFeatures>,
}

/// Compute a feature snapshot for `instrument` at event time `now`.
/// Returns `None` when the instrument has no bid, ask, and last yet.
pub fn compute_features(
    state: &MarketState,
    instrument: &str,
    now: DateTime<Utc>,
) -> Option<FeatureSnapshot> {
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

    // ── Tape features ─────────────────────────────────────────────────────────
    let tape = if snapshot.tape.prints.is_empty() {
        None
    } else {
        let w2s   = snapshot.tape.window_metrics(now - chrono::Duration::seconds(2));
        let w1s   = snapshot.tape.window_metrics(now - chrono::Duration::seconds(1));
        let w500  = snapshot.tape.window_metrics(now - chrono::Duration::milliseconds(500));
        let w5s   = snapshot.tape.window_metrics(now - chrono::Duration::seconds(5));

        Some(TapeFeatures {
            buy_vol_2s:          w2s.buy_vol,
            sell_vol_2s:         w2s.sell_vol,
            micro_delta_2s:      w2s.micro_delta(),
            print_count_2s:      w2s.print_count,
            prints_per_sec_2s:   w2s.print_count as f64 / 2.0,
            price_change_2s:     w2s.price_change,
            upticks_2s:          w2s.upticks,
            downticks_2s:        w2s.downticks,
            micro_delta_1s:      w1s.micro_delta(),
            print_count_1s:      w1s.print_count,
            prints_per_sec_1s:   w1s.print_count as f64,
            micro_delta_500ms:   w500.micro_delta(),
            print_count_500ms:   w500.print_count,
            prints_per_sec_500ms: w500.print_count as f64 / 0.5,
            print_count_5s:      w5s.print_count,
            prints_per_sec_5s:   w5s.print_count as f64 / 5.0,
            near_ask_size:       snapshot.tape.near_ask_size(),
            near_bid_size:       snapshot.tape.near_bid_size(),
        })
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
        tape,
    })
}
