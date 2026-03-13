# HeikinAshiStrategy

**Strategy ID:** `heikin-ashi-v1`

## Overview

A trend-following reversal strategy that recalculates Heikin Ashi candles from real OHLC bar data. It requires **two consecutive same-colour HA candles** before emitting a signal, filtering out single-bar noise and only committing to established short-term trends.

## Entry Rules

| Side | Condition |
|------|-----------|
| **Buy**  | Two consecutive bullish HA candles (`HA close > HA open`, streak ≥ +2) |
| **Sell** | Two consecutive bearish HA candles (`HA close < HA open`, streak ≤ −2) |

A signal in the same direction as the last emitted signal is suppressed until a reversal occurs.

## Heikin Ashi Formulas

$$\text{HA close} = \frac{\text{open} + \text{high} + \text{low} + \text{close}}{4}$$

$$\text{HA open} = \frac{\text{prev HA open} + \text{prev HA close}}{2}$$

$$\text{HA high} = \max(\text{high},\ \text{HA open},\ \text{HA close})$$

$$\text{HA low} = \min(\text{low},\ \text{HA open},\ \text{HA close})$$

**Bootstrap (first bar):** `HA open = (real open + real close) / 2`

## Configuration Parameters

| Parameter | Source | Description |
|-----------|--------|-------------|
| `cooldown_ms` | `AppConfig` | Minimum milliseconds between signals per instrument |
| `force_trade_once` | `AppConfig` | If `true`, emits one signal immediately regardless of market state |
| `force_trade_side` | `AppConfig` | Side used for the forced one-shot signal (`Buy` or `Sell`) |

## Usage

```bash
cargo run -- --strategy heikin-ashi
```

## Signal Reason Format

```
heikin-ashi streak=<n> ha_open=<value> ha_close=<value> ha_high=<value> ha_low=<value>
```

## Notes

- Only `BarUpdate` events are processed; quote-only ticks are ignored.
- State (`prev_ha_open`, `prev_ha_close`, `streak`, `last_signal_side`) is tracked independently per instrument.
- The reversal guard (`last_signal_side`) prevents re-entering the same direction without an opposing candle sequence first.
