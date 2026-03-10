# Development Instructions

## Prerequisites
- Windows machine with NinjaTrader 8 installed
- Rust toolchain (stable)
- .NET SDK compatible with NinjaTrader custom tooling

## Repository Layout
- `src/ninjatrader`: NinjaTrader execution bridge and transport adapters
- `src/rust/strategy-service`: Rust strategy runtime
- `src/shared-schemas`: Canonical JSON Schemas
- `tests`: Integration and fixture tests

## Local Configuration
1. Copy `.env.example` to `.env`.
2. Confirm localhost ports are free.
3. Keep `LIVE_TRADING_ENABLED=false` during development.

## Rust Service
1. Open terminal in `src/rust/strategy-service`.
2. Run `cargo build`.
3. Run `cargo run`.

## NinjaTrader Side
1. Import or reference code from `src/ninjatrader` in your NinjaTrader custom project.
2. Configure bindings to match `.env` port values.
3. Start in simulation account only.

## Safety Defaults
- Live trading disabled by default.
- Fail closed when validation fails or state is uncertain.
- All signals require unique `signalId` and bounded staleness.
