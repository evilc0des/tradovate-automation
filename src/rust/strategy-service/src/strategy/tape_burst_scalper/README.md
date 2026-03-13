# TapeBurstScalperStrategy

**Strategy ID:** `tape-burst-scalper-v1`

## Overview

An event-driven micro-structure scalper that enters when a short-lived burst of
aggressive order flow appears **and price immediately responds**.  The core insight
is that aggression + movement = continuation candidate, while aggression + no
movement = absorption (often a reversal setup).  The strategy therefore measures
both sides and only fires when both conditions are met.

Typical hold time: 1–20 s.  Hard cap: controlled by `--tape-time-stop-ms` (default 8 s).

## Entry Rules

Entries fire when **all** of the following hold simultaneously over the primary
2-second rolling window.

### Long setup

| Check | Condition |
|-------|-----------|
| Micro-delta | `(buy_vol − sell_vol)_2s ≥ TAPE_MICRO_DELTA_MIN` |
| Aggression ratio | `buy_vol_2s / sell_vol_2s ≥ TAPE_AGGRESSION_RATIO_MIN` |
| Tape speed | `pps_2s / pps_5s ≥ TAPE_SPEED_FACTOR_MIN` |
| Price direction | `price_change_2s > 0` (price actually went up) |
| Uptick majority | `upticks_2s ≥ downticks_2s` |
| Response ratio | `(Δprice_ticks) / (buy_vol / 10) ≥ TAPE_PRICE_RESPONSE_MIN_TICKS` |
| Spread | `spread ≤ tick_size × 2` |
| Near-wall | `near_ask_size < TAPE_WALL_MIN_SIZE` |

### Short setup (exact mirror)

| Check | Condition |
|-------|-----------|
| Micro-delta | `(buy_vol − sell_vol)_2s ≤ −TAPE_MICRO_DELTA_MIN` |
| Aggression ratio | `sell_vol_2s / buy_vol_2s ≥ TAPE_AGGRESSION_RATIO_MIN` |
| Tape speed | same as long |
| Price direction | `price_change_2s < 0` |
| Downtick majority | `downticks_2s ≥ upticks_2s` |
| Response ratio | `(−Δprice_ticks) / (sell_vol / 10) ≥ TAPE_PRICE_RESPONSE_MIN_TICKS` |
| Spread | same as long |
| Near-wall | `near_bid_size < TAPE_WALL_MIN_SIZE` |

## Exit Rules

Exits are evaluated on **every tick** once a position is open, in this priority order:

| Rule | Long condition | Short condition |
|------|----------------|-----------------|
| **Target** | `(last − entry) / tick ≥ TAPE_TARGET_TICKS` | `(entry − last) / tick ≥ TAPE_TARGET_TICKS` |
| **Stop** | `(entry − last) / tick ≥ TAPE_STOP_TICKS` | `(last − entry) / tick ≥ TAPE_STOP_TICKS` |
| **Flow-failure** | `micro_delta_1s ≤ −TAPE_FLIP_DELTA` | `micro_delta_1s ≥ TAPE_FLIP_DELTA` |
| **Time-stop** | `elapsed_ms ≥ TAPE_TIME_STOP_MS` | same |

Exit signals are emitted as market orders in the opposite direction.

## Configuration Parameters

| CLI flag | Env var | Default | Description |
|----------|---------|---------|-------------|
| `--tape-tick-size` | `TAPE_TICK_SIZE` | `0.25` | Price units per tick (NQ/MNQ = 0.25) |
| `--tape-micro-delta-min` | `TAPE_MICRO_DELTA_MIN` | `40` | Minimum buy−sell volume imbalance (contracts) |
| `--tape-aggression-ratio-min` | `TAPE_AGGRESSION_RATIO_MIN` | `1.8` | Minimum buy/sell (or sell/buy) volume ratio |
| `--tape-speed-factor-min` | `TAPE_SPEED_FACTOR_MIN` | `1.5` | Minimum pps / baseline-pps multiplier |
| `--tape-price-response-min-ticks` | `TAPE_PRICE_RESPONSE_MIN_TICKS` | `0.5` | Minimum ticks moved per 10 aggressive contracts |
| `--tape-target-ticks` | `TAPE_TARGET_TICKS` | `2.0` | Profit target in ticks |
| `--tape-stop-ticks` | `TAPE_STOP_TICKS` | `2.0` | Hard stop in ticks |
| `--tape-time-stop-ms` | `TAPE_TIME_STOP_MS` | `8000` | Max hold time before time-stop exit (ms) |
| `--tape-flip-delta` | `TAPE_FLIP_DELTA` | `20` | Flow-failure: 1-s delta magnitude that triggers early exit |
| `--tape-session-start-utc` | `TAPE_SESSION_START_UTC` | `00:00` | Entry window start (HH:MM UTC). `00:00` = no filter |
| `--tape-session-end-utc` | `TAPE_SESSION_END_UTC` | `23:59` | Entry window end (HH:MM UTC). `23:59` = no filter |
| `--tape-wall-min-size` | `TAPE_WALL_MIN_SIZE` | `1000` | L1 wall proxy: suppress entry when near-touch ≥ this size |
| `--cooldown-ms` | `COOLDOWN_MS` | `2000` | Minimum ms between consecutive entry signals |

## Usage

```bash
cargo run -- \
  --strategy tape-burst-scalper \
  --instruments "MNQ 06-26" \
  --tape-tick-size 0.25 \
  --tape-micro-delta-min 40 \
  --tape-aggression-ratio-min 1.8 \
  --tape-speed-factor-min 1.5 \
  --tape-price-response-min-ticks 0.5 \
  --tape-target-ticks 2.0 \
  --tape-stop-ticks 2.0 \
  --tape-time-stop-ms 8000 \
  --tape-session-start-utc "13:30" \
  --tape-session-end-utc "20:00" \
  --cooldown-ms 500
```

## Signal Reason Format

### Entry
```
tape-burst-long delta2s=<n> aggR=<ratio> speedF=<factor> pchg=<price> up=<n> dn=<n> resp=<ratio> spread=<price> pps2s=<n> pps5s=<n>
tape-burst-short delta2s=<n> ...
```

### Exit
```
tape-burst-exit reason=<target|stop|flow-failure|time-stop> entry_side=<Buy|Sell> entry=<price> last=<price> elapsed_ms=<n> delta1s=<n> delta2s=<n>
```

## Rolling Windows

The strategy uses four rolling windows, all computed from event-time timestamps
(not wall-clock time), so simulation/replay behaves identically to live trading:

| Window | Use |
|--------|-----|
| 500 ms | Fine-grained burst detection (available in `TapeFeatures`) |
| 1 s | Flow-failure exit check |
| 2 s | Primary entry condition window |
| 5 s | Tape-speed baseline (`prints_per_sec_5s`) |

## Data Requirements

This strategy requires **tick-level market data** — L1 quotes and trade prints —
forwarded from NinjaTrader via the existing NDJSON TCP transport.  Bar data is
ignored.  The richer the aggressor-side classification from NinjaTrader, the
better the buy/sell volume attribution.  When `aggressorSide` is absent or
`"Unknown"`, the bridge infers it from the current bid/ask:
`price ≥ ask → buy aggressor`, `price ≤ bid → sell aggressor`.

## Migration Path: MNQ → NQ

Start on MNQ.  Before promoting to NQ:

1. Run ≥ 500 completed round-trips in simulation.
2. Friction-adjusted expectancy ≥ 0.3 ticks per trade.
3. Maximum intra-session drawdown ≤ 10 stops.
4. Median hold time within the 1–20 s band.

NQ's tick value is ~5× less forgiving than MNQ.  Do not skip this gate.

## Notes

- Position state (entry price, entry time) is tracked per-instrument in memory
  only.  It is lost on process restart.  The bridge's safety layer handles
  reconciliation and prevents stale orders from persisting.
- Exit signals use the same market-order path as entries.  They are subject to
  the same deduplication and staleness checks in the NinjaTrader bridge.
- Session-window filters apply to **entries only**.  If a position is already
  open when the session window closes, the exit logic continues to run normally.
- L2 DOM depth, iceberg detection, and queue-position modeling are deliberately
  excluded from v1.  These can be added as additional `TapeFeatures` fields
  once L2 data is plumbed through the market data transport.
