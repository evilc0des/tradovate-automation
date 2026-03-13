# DeterministicStrategy

**Strategy ID:** `deterministic-v1`

## Overview

The original connectivity-test strategy, retained for backward compatibility and quick sanity checks. It emits a trade signal when the last trade price breaks outside the current bid-ask spread.

## Entry Rules

| Side | Condition |
|------|-----------|
| **Buy**  | `last_price > ask` — last trade printed above the offer |
| **Sell** | `last_price < bid` — last trade printed below the bid  |

No signal is emitted when `last_price` is within the spread.

## Parameters

| Parameter | Source | Description |
|-----------|--------|-------------|
| `cooldown_ms` | `AppConfig` | Minimum milliseconds between signals per instrument |
| `force_trade_once` | `AppConfig` | If `true`, emits one signal immediately regardless of market state |
| `force_trade_side` | `AppConfig` | Side used for the forced one-shot signal (`Buy` or `Sell`) |

## Usage

Select this strategy by omitting a recognised strategy name (it is the default fallback), or by running:

```bash
cargo run -- --strategy deterministic
```

## Notes

- Primarily useful for verifying end-to-end connectivity between the strategy service and the NinjaTrader bridge.
- Not intended for live trading; there is no directional edge in a spread-breakout rule on tick data.
