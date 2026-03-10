# Architecture

## Runtime Components
1. NinjaTrader Market Data Publisher
2. Rust Strategy Service
3. NinjaTrader Guarded Execution Bridge

## Data Paths
- Market Data: NinjaTrader -> TCP (`127.0.0.1:9100`) -> Rust
- Signals: Rust -> TCP (`127.0.0.1:9101`) -> NinjaTrader

## Framing and Serialization
- Newline-delimited JSON (NDJSON)
- One JSON object per line
- UTF-8 payloads

## Mandatory Envelope Fields
- `messageType`
- `version`
- `timestamp`
- `sourceId`
- `correlationId`

## Safety Model
- Default startup state is disarmed.
- Signal validation, deduplication, and risk checks run before submission.
- On uncertainty, transition to safe mode and block order routing.
