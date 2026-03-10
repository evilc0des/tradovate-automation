# NinjaTrader Bridge Test Host

Standalone console harness to validate bridge logic outside NinjaTrader without modifying core source files.

## Design
- Links bridge source files from `../ninjatrader` using `<Compile Include ... Link=... />`.
- Runs a local TCP capture server for market-data frames.
- Starts `SimulationMarketDataFeed` and prints NDJSON frames.
- Performs a smoke check on `ExecutionBridge` signal handling.

## Run
```powershell
dotnet run --project src/ninjatrader-test-host/NinjaTraderBridge.TestHost.csproj
```

## Run Rust End-To-End (One Command)
```powershell
dotnet run --project src/ninjatrader-test-host/NinjaTraderBridge.TestHost.csproj -- --rust-e2e
```

This mode:
- starts a local signal listener on `127.0.0.1:9101`
- starts the Rust strategy service via `cargo run`
- sends deterministic market data to `127.0.0.1:9100`
- prints the first received `TradeSignal` payload

## Run Phase 6 Intake Smoke
```powershell
dotnet run --project src/ninjatrader-test-host/NinjaTraderBridge.TestHost.csproj -- --signal-intake-smoke
```

This mode validates:
- accepted signal ack response
- malformed payload rejection (`ErrorEnvelope`)
- invalid-source semantic rejection
- reconnect handling across separate client sessions

## Run Phase 6 Intake Rust E2E
```powershell
dotnet run --project src/ninjatrader-test-host/NinjaTraderBridge.TestHost.csproj -- --signal-intake-rust-e2e
```

This mode validates Rust-generated `TradeSignal` intake and `SignalAck` processing on the NinjaTrader bridge side.

## Safety
- No order routing to NinjaTrader.
- `LiveTradingEnabled` is forced `false` in host config.
