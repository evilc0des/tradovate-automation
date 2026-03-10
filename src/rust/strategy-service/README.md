# Rust Strategy Service

Consumes market data over TCP NDJSON and emits normalized trade signals over TCP NDJSON.

## Run
```powershell
cargo run
```

## Environment Variables
- `MARKET_DATA_BIND` (default `127.0.0.1:9100`)
- `SIGNAL_BIND` (default `127.0.0.1:9101`)
- `ALLOWED_ACCOUNT` (default `SIM101`)
- `ALLOWED_INSTRUMENTS` (comma-separated)
