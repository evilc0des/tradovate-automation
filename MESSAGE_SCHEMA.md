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

## Validation Notes
- `version` is fixed to `v1`.
- `orderType` supports only `Market` in v1.
- `side` must be `Buy` or `Sell`.
- `quantity` must be integer >= 1.
- `signalId` must be globally unique for deduplication.
