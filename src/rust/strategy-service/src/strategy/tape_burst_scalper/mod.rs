use std::collections::HashMap;
use std::time::{Duration, Instant};

use chrono::{DateTime, Timelike, Utc};

use crate::config::AppConfig;
use crate::features::FeatureSnapshot;
use crate::models::MarketDataMessage;

use super::{build_exit_signal, build_signal, Strategy, TradeSignal};

// ── Internal decision type ────────────────────────────────────────────────────

/// Result of evaluating exit conditions while a position is open.
enum ExitDecision {
    /// Remain in the position — no exit condition met yet.
    Hold,
    /// Close the position immediately with this `side` and `reason`.
    Exit { side: &'static str, reason: String },
}

// ── Per-instrument position state ─────────────────────────────────────────────

struct OpenTrade {
    /// `"Buy"` for a long position, `"Sell"` for a short position.
    side: &'static str,
    entry_price: f64,
    entry_time: Instant,
}

#[derive(Default)]
struct InstrumentState {
    open_trade: Option<OpenTrade>,
    last_emitted: Option<Instant>,
}

// ── Strategy ──────────────────────────────────────────────────────────────────

pub struct TapeBurstScalperStrategy {
    cooldown: Duration,
    per_instrument: HashMap<String, InstrumentState>,
    forced_trade_emitted: bool,
}

impl TapeBurstScalperStrategy {
    pub fn new(cooldown_ms: u64) -> Self {
        Self {
            cooldown: Duration::from_millis(cooldown_ms),
            per_instrument: HashMap::new(),
            forced_trade_emitted: false,
        }
    }
}

// ── Session-window utility ────────────────────────────────────────────────────

/// Returns `true` if `event_time` (UTC) falls within the `[start, end]` window
/// specified as `"HH:MM"` strings.  Returns `true` (permissive) when either
/// string cannot be parsed, so misconfiguration fails open for entries rather
/// than silently blocking all trades.
fn within_session(event_time: DateTime<Utc>, start: &str, end: &str) -> bool {
    let parse_hhmm = |s: &str| -> Option<u32> {
        let (h, m) = s.split_once(':')?;
        let h: u32 = h.parse().ok()?;
        let m: u32 = m.parse().ok()?;
        if h > 23 || m > 59 {
            return None;
        }
        Some(h * 60 + m)
    };

    let (start_min, end_min) = match (parse_hhmm(start), parse_hhmm(end)) {
        (Some(s), Some(e)) => (s, e),
        _ => return true, // unparseable → no restriction
    };

    let now_min = event_time.hour() * 60 + event_time.minute();

    if start_min <= end_min {
        // Normal same-day window e.g. 08:30–17:00
        now_min >= start_min && now_min <= end_min
    } else {
        // Overnight window e.g. 18:00–17:00 (wraps midnight)
        now_min >= start_min || now_min <= end_min
    }
}

// ── Strategy implementation ───────────────────────────────────────────────────

impl Strategy for TapeBurstScalperStrategy {
    fn strategy_id(&self) -> &str {
        "tape-burst-scalper-v1"
    }

    fn has_open_position(&self, instrument: &str) -> bool {
        self.per_instrument
            .get(instrument)
            .map(|s| s.open_trade.is_some())
            .unwrap_or(false)
    }

    fn on_market_data(
        &mut self,
        cfg: &AppConfig,
        msg: &MarketDataMessage,
        features: Option<&FeatureSnapshot>,
    ) -> Option<TradeSignal> {
        // ── Forced one-shot (connectivity test) ───────────────────────────────
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

        let f = features?;
        let tape = f.tape.as_ref()?;
        let last = f.last;
        let tick = cfg.tape_tick_size;
        if tick <= 0.0 {
            return None; // misconfigured — guard against divide-by-zero
        }

        let state = self.per_instrument.entry(msg.instrument.clone()).or_default();

        // ── EXIT logic (highest priority — evaluated on every tick while open) ─
        //
        // We collect all the data we need from the immutable borrow of
        // `state.open_trade` first, then drop that borrow before mutating.
        let decision = if let Some(ref trade) = state.open_trade {
            let elapsed_ms = trade.entry_time.elapsed().as_millis() as u64;

            let (target_hit, stop_hit, flow_fail) = if trade.side == "Buy" {
                (
                    (last - trade.entry_price) / tick >= cfg.tape_target_ticks,
                    (trade.entry_price - last) / tick >= cfg.tape_stop_ticks,
                    tape.micro_delta_1s <= -(cfg.tape_flip_delta),
                )
            } else {
                // Short position: gains when price falls
                (
                    (trade.entry_price - last) / tick >= cfg.tape_target_ticks,
                    (last - trade.entry_price) / tick >= cfg.tape_stop_ticks,
                    tape.micro_delta_1s >= cfg.tape_flip_delta,
                )
            };

            let time_stop = elapsed_ms >= cfg.tape_time_stop_ms;

            if target_hit || stop_hit || flow_fail || time_stop {
                let side: &'static str = if trade.side == "Buy" { "Sell" } else { "Buy" };
                let tag = if target_hit {
                    "target"
                } else if stop_hit {
                    "stop"
                } else if flow_fail {
                    "flow-failure"
                } else {
                    "time-stop"
                };
                let reason = format!(
                    "tape-burst-exit reason={} entry_side={} entry={:.4} \
                     last={:.4} elapsed_ms={} delta1s={} delta2s={}",
                    tag,
                    trade.side,
                    trade.entry_price,
                    last,
                    elapsed_ms,
                    tape.micro_delta_1s,
                    tape.micro_delta_2s,
                );
                Some(ExitDecision::Exit { side, reason })
            } else {
                Some(ExitDecision::Hold)
            }
        } else {
            None // not in a position
        };

        match decision {
            Some(ExitDecision::Exit { side, reason }) => {
                state.open_trade = None;
                state.last_emitted = Some(Instant::now());
                return Some(build_exit_signal(cfg, msg, self.strategy_id(), side, &reason));
            }
            Some(ExitDecision::Hold) => return None, // still in position, no action
            None => {}                               // not in position — evaluate entry
        }

        // ── Session-window gate (entries only) ────────────────────────────────
        if !within_session(
            msg.timestamp,
            &cfg.tape_session_start_utc,
            &cfg.tape_session_end_utc,
        ) {
            return None;
        }

        // ── Inter-signal cooldown gate ─────────────────────────────────────────
        let now = Instant::now();
        if let Some(last_emit) = state.last_emitted {
            if now.duration_since(last_emit) < self.cooldown {
                return None;
            }
        }

        // ── Derived metrics ───────────────────────────────────────────────────

        // Speed factor: ratio of 2s pps to 5s baseline pps.
        // If the baseline is not yet established, suppress entry rather than
        // firing on early noisy prints.
        let speed_factor = if tape.prints_per_sec_5s > 0.01 {
            tape.prints_per_sec_2s / tape.prints_per_sec_5s
        } else {
            0.0
        };

        // Price-response ratio: ticks moved per 10 aggressive contracts.
        //   long  — price should move up proportionally to buy-side volume.
        //   short — price should move down proportionally to sell-side volume.
        let long_aggressive = tape.buy_vol_2s as f64;
        let long_response = if long_aggressive >= 10.0 {
            (tape.price_change_2s / tick) / (long_aggressive / 10.0)
        } else {
            0.0
        };

        let short_aggressive = tape.sell_vol_2s as f64;
        let short_response = if short_aggressive >= 10.0 {
            ((-tape.price_change_2s) / tick) / (short_aggressive / 10.0)
        } else {
            0.0
        };

        // L1 wall proxies
        let ask_not_walled = tape
            .near_ask_size
            .map(|s| s < cfg.tape_wall_min_size)
            .unwrap_or(true);
        let bid_not_walled = tape
            .near_bid_size
            .map(|s| s < cfg.tape_wall_min_size)
            .unwrap_or(true);

        // Spread must be ≤ 2 ticks (configurable via tick_size)
        let spread_ok = f.spread <= tick * 2.0;

        // Aggression ratios (capped at 99.9 to avoid Inf in log formatting)
        let long_agg_ratio = if tape.sell_vol_2s == 0 {
            f64::MAX
        } else {
            tape.buy_vol_2s as f64 / tape.sell_vol_2s as f64
        };
        let short_agg_ratio = if tape.buy_vol_2s == 0 {
            f64::MAX
        } else {
            tape.sell_vol_2s as f64 / tape.buy_vol_2s as f64
        };

        // ── LONG burst detection ──────────────────────────────────────────────
        //
        // All of the following must hold simultaneously:
        //  1. Positive micro delta ≥ threshold (more buy aggression than sell)
        //  2. Sufficient aggression ratio (buy volume dominates)
        //  3. Tape accelerating vs baseline
        //  4. Price actually moved up (has to be continuation, not absorption)
        //  5. More upticks than downticks in window
        //  6. Price-response ratio is acceptable (movement per volume)
        //  7. Spread is not blown out
        //  8. No large visible ask wall directly overhead
        let long_burst = tape.micro_delta_2s >= cfg.tape_micro_delta_min
            && long_agg_ratio >= cfg.tape_aggression_ratio_min
            && speed_factor >= cfg.tape_speed_factor_min
            && tape.price_change_2s > 0.0
            && tape.upticks_2s >= tape.downticks_2s
            && long_response >= cfg.tape_price_response_min_ticks
            && spread_ok
            && ask_not_walled;

        // ── SHORT burst detection (mirror of long) ────────────────────────────
        let short_burst = tape.micro_delta_2s <= -(cfg.tape_micro_delta_min)
            && short_agg_ratio >= cfg.tape_aggression_ratio_min
            && speed_factor >= cfg.tape_speed_factor_min
            && tape.price_change_2s < 0.0
            && tape.downticks_2s >= tape.upticks_2s
            && short_response >= cfg.tape_price_response_min_ticks
            && spread_ok
            && bid_not_walled;

        let (side, reason): (&'static str, String) = if long_burst {
            (
                "Buy",
                format!(
                    "tape-burst-long delta2s={} aggR={:.2} speedF={:.2} \
                     pchg={:.4} up={} dn={} resp={:.3} spread={:.4} \
                     pps2s={:.1} pps5s={:.1}",
                    tape.micro_delta_2s,
                    long_agg_ratio.min(99.9),
                    speed_factor,
                    tape.price_change_2s,
                    tape.upticks_2s,
                    tape.downticks_2s,
                    long_response,
                    f.spread,
                    tape.prints_per_sec_2s,
                    tape.prints_per_sec_5s,
                ),
            )
        } else if short_burst {
            (
                "Sell",
                format!(
                    "tape-burst-short delta2s={} aggR={:.2} speedF={:.2} \
                     pchg={:.4} up={} dn={} resp={:.3} spread={:.4} \
                     pps2s={:.1} pps5s={:.1}",
                    tape.micro_delta_2s,
                    short_agg_ratio.min(99.9),
                    speed_factor,
                    tape.price_change_2s,
                    tape.upticks_2s,
                    tape.downticks_2s,
                    short_response,
                    f.spread,
                    tape.prints_per_sec_2s,
                    tape.prints_per_sec_5s,
                ),
            )
        } else {
            return None; // burst quality score insufficient
        };

        // ── Record position state and emit entry signal ───────────────────────
        state.open_trade = Some(OpenTrade {
            side,
            entry_price: last,
            entry_time: Instant::now(),
        });
        state.last_emitted = Some(Instant::now());

        Some(build_signal(cfg, msg, self.strategy_id(), side, &reason))
    }
}
