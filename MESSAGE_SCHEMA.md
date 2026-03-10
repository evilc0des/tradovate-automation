# Message Schema

## Transport
- Protocol: TCP on localhost
- Framing: NDJSON (one JSON object per line)
- Encoding: UTF-8

## Shared Envelope
Every message must include:
- `messageType` (string)
- `version` (string, `v1`)
- `timestamp` (RFC3339 UTC)
- `sourceId` (string)
- `correlationId` (string)

## MarketDataMessage
```json
{
  "messageType": "MarketDataMessage",
  "version": "v1",
  "timestamp": "2026-03-10T14:00:00Z",
  "sourceId": "ninjatrader.bridge",
  "correlationId": "md-123",
  "instrument": "MES 06-26",
  "eventType": "QuoteUpdate",
  "lastPrice": 5080.25,
  "bid": 5080.00,
  "ask": 5080.25,
  "lastSize": 1
}
```

## QuoteUpdateMessage
```json
{
  "messageType": "QuoteUpdateMessage",
  "version": "v1",
  "timestamp": "2026-03-10T14:00:00Z",
  "sourceId": "ninjatrader.bridge",
  "correlationId": "quote-123",
  "instrument": "MES 06-26",
  "bid": 5080.00,
  "ask": 5080.25,
  "bidSize": 15,
  "askSize": 12
}
```

## TradePrintMessage
```json
{
  "messageType": "TradePrintMessage",
  "version": "v1",
  "timestamp": "2026-03-10T14:00:00Z",
  "sourceId": "ninjatrader.bridge",
  "correlationId": "trade-123",
  "instrument": "MES 06-26",
  "price": 5080.25,
  "size": 3,
  "aggressorSide": "Buy"
}
```

## BarUpdateMessage
```json
{
  "messageType": "BarUpdateMessage",
  "version": "v1",
  "timestamp": "2026-03-10T14:00:00Z",
  "sourceId": "ninjatrader.bridge",
  "correlationId": "bar-123",
  "instrument": "MES 06-26",
  "barTime": "2026-03-10T14:00:00Z",
  "interval": "1m",
  "open": 5079.75,
  "high": 5080.50,
  "low": 5079.50,
  "close": 5080.25,
  "volume": 321
}
```

## ConnectionStateMessage
```json
{
  "messageType": "ConnectionStateMessage",
  "version": "v1",
  "timestamp": "2026-03-10T14:00:00Z",
  "sourceId": "ninjatrader.bridge",
  "correlationId": "conn-123",
  "provider": "Tradovate",
  "state": "Connected",
  "details": "Primary feed healthy"
}
```

## TradeSignal
```json
{
  "messageType": "TradeSignal",
  "version": "v1",
  "timestamp": "2026-03-10T14:00:00Z",
  "sourceId": "rust.strategy",
  "correlationId": "sig-123",
  "signalId": "sig-123",
  "strategyId": "deterministic-v1",
  "account": "SIM101",
  "instrument": "MES 06-26",
  "side": "Buy",
  "quantity": 1,
  "orderType": "Market",
  "reason": "Last traded above ask threshold"
}
```

## SignalAck
```json
{
  "messageType": "SignalAck",
  "version": "v1",
  "timestamp": "2026-03-10T14:00:00Z",
  "sourceId": "ninjatrader.bridge",
  "correlationId": "sig-123",
  "signalId": "sig-123",
  "status": "Accepted",
  "detail": "Validated and queued"
}
```

## ErrorEnvelope
```json
{
  "messageType": "ErrorEnvelope",
  "version": "v1",
  "timestamp": "2026-03-10T14:00:00Z",
  "sourceId": "ninjatrader.bridge",
  "correlationId": "sig-123",
  "code": "SIG_VALIDATION_FAILED",
  "severity": "Error",
  "message": "Signal rejected due to stale timestamp",
  "details": "Signal age exceeded 3000ms",
  "retryable": false
}
```

## Validation Notes
- `version` is fixed to `v1`.
- `orderType` supports only `Market` in v1.
- `side` must be `Buy` or `Sell`.
- `quantity` must be integer >= 1.
- `signalId` must be globally unique for deduplication.
- `QuoteUpdateMessage` requires both bid/ask price and size.
- `TradePrintMessage` requires `price` and `size`.
- `BarUpdateMessage` requires full OHLCV payload.
- `ErrorEnvelope` should be emitted for validation, transport, and safety-state failures.
