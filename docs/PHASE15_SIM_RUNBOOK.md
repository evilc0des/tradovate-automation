# Phase 15 SIM Runbook

This runbook executes the first working vertical slice in simulation mode:
- C# side publishes normalized market data
- Rust consumes data and generates deterministic signal
- NinjaTrader execution bridge validates signal and submits simulation order
- bridge lifecycle and persistence artifacts are written

For NinjaTrader packaging and exact startup workflow, see `docs/NINJATRADER_PACKAGING_AND_RUN_GUIDE.md`.

## Scope and Safety

- Account: simulation only (`SIM101`)
- Order type: market order
- Quantity: `1`
- Live trading: disabled (`LiveTradingEnabled=false`)
- Fail mode: fail-closed (bridge can disarm on unsafe conditions)

## Preconditions

1. NinjaTrader connection configured for simulation account.
2. Repo builds locally in current workspace.
3. Localhost ports available:
- `19200` for market-data ingress into Rust
- `19201` for signal intake into bridge
4. No other process bound to those ports.

## Fast Validation (Automated)

Run the Phase 15 smoke mode:

```powershell
dotnet run --project .\src\ninjatrader-test-host\NinjaTraderBridge.TestHost.csproj -- --phase15-smoke
```

Expected output includes:
- Rust startup and `signal dispatched`
- bridge logs showing signal source `rust.strategy`
- simulated order accepted
- checklist lines all `True`:
  - `[PHASE15] rust_signal_received=True`
  - `[PHASE15] rust_signal_source_valid=True`
  - `[PHASE15] simulation_order_submitted=True`
  - `[PHASE15] persistence_execution_journal=True`
  - `[PHASE15] persistence_expected_state=True`
  - `[PHASE15] persistence_actual_state=True`

## NinjaTrader Manual SIM Run

Use this when validating in the real NinjaTrader runtime (outside the test host).

### 1. Configure Bridge for SIM

Ensure configuration values are equivalent to:
- `LiveTradingEnabled=false`
- `DisarmOnStartup=true` (recommended for manual run)
- `AllowedAccount=SIM101`
- `AllowedInstruments` includes your SIM instrument
- `AllowedSignalSources` includes `rust.strategy`
- `MaxOrderQuantity=1`
- `MaxSignalAgeMs=3000`

### 2. Start Rust Strategy Service

From `src/rust/strategy-service`:

```powershell
$env:MARKET_DATA_BIND="127.0.0.1:19200"
$env:SIGNAL_BIND="127.0.0.1:19201"
$env:ALLOWED_ACCOUNT="SIM101"
$env:ALLOWED_INSTRUMENTS="MES 06-26"
cargo run
```

Expected:
- service starts and binds both sockets
- log lines appear when market-data client connects and when signal is dispatched

### 3. Start NinjaTrader Bridge Components

- Start market-data publisher in NinjaTrader strategy/add-on host
- Start signal intake transport on `127.0.0.1:19201`
- Verify bridge starts disarmed if configured so
- Explicitly arm bridge before test

Expected:
- connection lifecycle logs
- heartbeat frames on market-data channel

### 4. Execute One Vertical Slice

- Feed one short burst of market data for one allowed instrument
- Wait for deterministic Rust signal
- Confirm bridge receives, validates, and submits one simulation order

Expected:
- signal accepted ack
- order accepted in simulation
- lifecycle journal updated

### 5. Verify Persistence Artifacts

Check files under `state/` for new entries:
- execution journal (`*.ndjson`)
- processed signal IDs
- expected state snapshot
- actual state snapshot
- safety state

Expected:
- files exist
- latest signal ID and order ID present in journal/snapshots

## Failure Handling

If no signal arrives:
1. Verify market-data frames are `MarketDataMessage` with `messageType="MarketDataMessage"` and `version="v1"`.
2. Verify instrument exactly matches allowed list.
3. Verify Rust process logs market-data connection and parse success.

If signal is rejected:
1. Check `AllowedSignalSources` includes `rust.strategy`.
2. Check account/instrument/quantity constraints.
3. Check staleness (`MaxSignalAgeMs`) and session-window constraints.

If bridge disarms:
1. Inspect logs for explicit disarm reason.
2. Resolve root cause (mismatch/corruption/ambiguity).
3. Re-arm explicitly and rerun test.

## Exit Criteria for Phase 15

All conditions hold in one run:
- market data published from NinjaTrader-side path
- Rust consumed stream and emitted deterministic signal
- bridge accepted signal and submitted simulation order
- lifecycle outcome persisted and observable in state files

## Recommended Next Step

Proceed to Phase 16 simulation soak and resilience drills:
- repeated reconnect tests
- duplicate-signal replay checks
- restart recovery checks
- extended-duration SIM stability run
