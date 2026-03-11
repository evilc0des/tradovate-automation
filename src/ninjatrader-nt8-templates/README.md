# NinjaTrader 8 Template Scripts

These templates are intended to be copied into NinjaTrader's custom script folder.

## Files
- `BridgeRunnerStrategy.cs`: minimal strategy host that wires market-data publishing and signal intake.

## Copy target
- `Documents\NinjaTrader 8\bin\Custom\NinjaScript\Strategies\BridgeRunnerStrategy.cs`

## Important
- This template runs with `LiveTradingEnabled=false` for SIM-only testing.
- Current bridge order submission path in this repo is simulated (`SimulatedOrderSubmissionGateway`).
- For real NinjaTrader order routing (even in SIM account), add a NinjaTrader-native order gateway implementation and inject it into `ExecutionBridge`.
