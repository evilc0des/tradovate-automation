# EmaMomentumStrategy

**Strategy ID:** `ema-momentum-v1`

## Overview

A trend-following crossover strategy. It tracks two exponential moving averages (EMA) of the last-trade price and fires when the faster EMA crosses the slower EMA, signalling a momentum shift.

## Entry Rules

| Side | Condition |
|------|-----------|
| **Buy**  | 5-period EMA crosses **above** 20-period EMA |
| **Sell** | 5-period EMA crosses **below** 20-period EMA |

No signal is emitted:
- Until both EMAs have at least one value (requires at least one trade print).
- On the very first bar for an instrument (regime is recorded but not traded).
- While the same directional regime continues without a crossover.
- During the per-instrument cooldown window after the last emitted signal.

## EMA Parameters

| EMA | Period | Label in features |
|-----|--------|-------------------|
| Fast | 5 bars | `ema_fast` |
| Slow | 20 bars | `ema_slow` |

Both EMAs are computed by the shared `FeatureSnapshot` from `MarketState`.

## Configuration Parameters

| Parameter | Source | Description |
|-----------|--------|-------------|
| `cooldown_ms` | `AppConfig` | Minimum milliseconds between signals per instrument |
| `force_trade_once` | `AppConfig` | If `true`, emits one signal immediately regardless of market state |
| `force_trade_side` | `AppConfig` | Side used for the forced one-shot signal (`Buy` or `Sell`) |

## Usage

```bash
cargo run -- --strategy ema-momentum
```

## Signal Reason Format

```
ema-crossover fast=<value> slow=<value> gap=<value> spread=<value> sessionTicks=<n>
```

## Notes

- The regime (`fast_above_slow`) is updated on every tick, including ticks where cooldown suppresses the signal, ensuring crossovers are never silently missed.
- Suitable for instruments with active trade printing; instruments that only move via quotes will never warm the EMAs.
