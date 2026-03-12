# Rust Strategy Service

Consumes market data over TCP NDJSON and emits normalized trade signals over TCP NDJSON.

## Run
```powershell
cargo run
# or pick a strategy explicitly:
cargo run -- --strategy heikin-ashi
cargo run -- --strategy ema-momentum
cargo run -- --strategy deterministic
```

## Strategies

| Name | `--strategy` flag | Description |
|---|---|---|
| Deterministic | `deterministic` *(default)* | Emits when last trade price breaks outside bid-ask spread. Good for connectivity tests. |
| EMA Momentum | `ema-momentum` | Enters on 5-period / 20-period EMA crossover. Trend-following. |
| Heikin Ashi | `heikin-ashi` | Enters on two consecutive same-colour Heikin Ashi candles. Reverses on opposite signal. Requires `BarUpdate` frames. |

### Heikin Ashi rules

Heikin Ashi candles are recomputed from each incoming `BarUpdate` frame:

```
HA close = (open + high + low + close) / 4
HA open  = (prev HA open + prev HA close) / 2   [first bar: (open + close) / 2]
HA high  = max(high, HA open, HA close)
HA low   = min(low,  HA open, HA close)
```

- **Entry Buy** — two consecutive bullish HA candles (`HA close > HA open`)
- **Entry Sell / reversal** — two consecutive bearish HA candles (`HA close < HA open`)
- After a signal fires, no duplicate signal is emitted while the streak continues in the same direction.
- The per-instrument cooldown (`COOLDOWN_MS`) acts as an additional throttle guard.

## Environment Variables
- `STRATEGY` (default `deterministic`; values: `deterministic`, `ema-momentum`, `heikin-ashi`)
- `MARKET_DATA_BIND` (default `127.0.0.1:9100`)
- `SIGNAL_BIND` (default `127.0.0.1:9101`)
- `ALLOWED_ACCOUNT` (default `SIM101`)
- `ALLOWED_INSTRUMENTS` (comma-separated)
- `COOLDOWN_MS` (default `2000`)
- `FORCE_TRADE_ONCE` (default `false`; when `true`, emits one market signal on first allowed instrument tick)
- `FORCE_TRADE_SIDE` (default `Buy`; `Buy` or `Sell`, used when `FORCE_TRADE_ONCE=true`)
