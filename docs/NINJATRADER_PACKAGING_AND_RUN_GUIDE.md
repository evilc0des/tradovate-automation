# NinjaTrader Packaging and Run Guide

This guide covers:
- how to package the bridge as a NinjaTrader import ZIP
- how to run it in NinjaTrader (SIM workflow)
- how to start and run the Rust service

## Current Runtime Note

The core bridge code is in `src/ninjatrader/*.cs` and is runtime-agnostic C#.
A NinjaTrader wrapper strategy template is provided at `src/ninjatrader-nt8-templates/BridgeRunnerStrategy.cs`.

Important:
- this repo uses `SimulatedOrderSubmissionGateway` by default
- to place native NinjaTrader orders from bridge signals, set `NativeOrderSubmission=true` in `BridgeRunnerStrategy`
- keep it `false` for transport/safety-only simulation runs

## Prerequisites

1. NinjaTrader 8 installed.
2. Rust installed (`cargo --version` works).
3. .NET SDK installed (`dotnet --version` works).
4. NinjaTrader closed before copying files into `Custom` folder.

## Paths Used

- Repo root:
`D:\Dev\tradovate-automation\tradovate-automation`

- NinjaTrader custom folder:
`$env:USERPROFILE\Documents\NinjaTrader 8\bin\Custom`

## Step 1: Copy Bridge Source into NinjaTrader Custom

Run from repo root:

```powershell
$repo = "D:\Dev\tradovate-automation\tradovate-automation"
$ntCustom = Join-Path $env:USERPROFILE "Documents\NinjaTrader 8\bin\Custom"

$dstCore = Join-Path $ntCustom "NinjaScript\AddOns\TradovateBridge"
$dstStrategy = Join-Path $ntCustom "NinjaScript\Strategies"

New-Item -ItemType Directory -Force -Path $dstCore | Out-Null
New-Item -ItemType Directory -Force -Path $dstStrategy | Out-Null

Copy-Item "$repo\src\ninjatrader\*.cs" $dstCore -Force
Copy-Item "$repo\src\ninjatrader-nt8-templates\BridgeRunnerStrategy.cs" $dstStrategy -Force
```

Result:
- core bridge classes copied under `AddOns\TradovateBridge`
- strategy runner script copied under `Strategies`

## Step 2: Compile in NinjaTrader

1. Open NinjaTrader.
2. Open `New -> NinjaScript Editor`.
3. Press `F5` (Compile).
4. Resolve any compile errors shown in the Errors tab.

If compile fails due to file duplicates, remove older copies from `Custom\NinjaScript` and compile again.

## Step 3: Run the Rust Service

Open terminal at `src/rust/strategy-service` and run:

```powershell
$env:MARKET_DATA_BIND="127.0.0.1:19200"
$env:SIGNAL_BIND="127.0.0.1:19201"
$env:ALLOWED_ACCOUNT="SIM101"
$env:ALLOWED_INSTRUMENTS="MES 06-26"
$env:RUST_LOG="debug"
cargo run
```

If you want quieter logs, set `$env:RUST_LOG="info"`.

Expected Rust logs:
- startup bind message
- `market data client connected`
- `signal dispatched`

Keep this terminal open while testing NinjaTrader.

## Step 4: Start Strategy in NinjaTrader

1. Open a chart for the instrument (example `MES 06-26`).
2. Right click chart -> `Strategies...`.
3. Select `BridgeRunnerStrategy`.
4. Set parameters:
- `SignalHost=127.0.0.1`
- `SignalPort=19201`
- `MarketDataHost=127.0.0.1`
- `MarketDataPort=19200`
- `ArmOnStartup=true` for immediate test (or false and arm manually in code flow)
- `NativeOrderSubmission=true` to submit real NinjaTrader strategy orders from accepted signals
5. Enable strategy.

Expected NinjaTrader/Output logs:
- signal intake listening
- market-data transport connected
- discovered signal source `rust.strategy`
- accepted signal ack path logs

## Step 5: Verify End-to-End Activity

After a few ticks/trades on chart:

1. Rust console should show `signal dispatched`.
2. NinjaTrader side should log signal processing and acceptance.
3. State files in repo `state/` should update:
- processed signal IDs
- execution journal
- expected state snapshot
- actual state snapshot

## Step 6: Package as NinjaTrader ZIP (for import on same/another machine)

After successful compile in NinjaTrader:

1. In NinjaTrader Control Center: `Tools -> Export -> NinjaScript Add-On...`
2. Include these script files:
- `AddOns/TradovateBridge/*.cs` (all copied core files)
- `Strategies/BridgeRunnerStrategy.cs`
3. Set export name such as `TradovateBridge_Phase15.zip`.
4. Finish export.

To install on another machine:

1. Copy ZIP to target machine.
2. In NinjaTrader: `Tools -> Import -> NinjaScript Add-On...`
3. Select ZIP and complete import.
4. Compile (`F5`) once after import.
5. Start Rust service and then enable `BridgeRunnerStrategy`.

## Exact Startup Order (Recommended)

1. Start Rust service terminal (`cargo run` with env vars).
2. Start NinjaTrader.
3. Enable `BridgeRunnerStrategy` on chart.
4. Confirm connection logs.
5. Observe signal dispatch and bridge acceptance logs.

## Exact Shutdown Order (Recommended)

1. Disable `BridgeRunnerStrategy`.
2. Stop Rust service (`Ctrl+C`).
3. Close NinjaTrader.

## Troubleshooting

### No signals received in NinjaTrader
- Check Rust env ports match strategy ports (`19200` and `19201`).
- Check instrument name exact match (`MES 06-26`).
- Check strategy enabled and chart receiving ticks.

### Rust starts but no market data client connection
- Strategy is not running or port mismatch.
- Firewall blocking localhost port access.

### Signal rejected
- Verify source is `rust.strategy`.
- Verify account is `SIM101`.
- Verify instrument is in allowed list.
- Verify bridge is armed.

### Packaging import errors
- Re-export with only the required files.
- Remove stale conflicting script files from `Custom\NinjaScript`.
- Compile immediately after import.

## Optional: Host-Only Verification Before NinjaTrader

From repo root:

```powershell
dotnet run --project .\src\ninjatrader-test-host\NinjaTraderBridge.TestHost.csproj -- --phase15-smoke
```

Use this to verify local dependencies before opening NinjaTrader.
