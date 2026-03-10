# NinjaTrader Bridge Skeleton

This folder contains the NinjaTrader-side execution and market-data publisher scaffolding.

## Implemented modules
- `BridgeConfig.cs`
- `BridgeLogger.cs`
- `Dtos.cs`
- `SignalValidator.cs`
- `DedupStore.cs`
- `RiskEngine.cs`
- `ExecutionBridge.cs`
- `MarketDataDtos.cs`
- `MarketDataEvents.cs`
- `MarketDataNormalizer.cs`
- `MarketDataTransport.cs`
- `MarketDataPublisher.cs`
- `NinjaTraderEventAdapter.cs`
- `SimulationMarketDataFeed.cs`
- `SignalIntakeTransport.cs`
- `ExecutionJournal.cs`
- `ActualStateSnapshotStore.cs`
- `OrderLifecycleTracker.cs`
- `OrderSubmissionGateway.cs`
- `SafetyStateManager.cs`
- `ExpectedStateSnapshotStore.cs`
- `ReconciliationEngine.cs`
- `PersistenceHealthMonitor.cs`
- `RuntimeMarkersStore.cs`

## Publisher flow
1. NinjaTrader callback data is mapped into event records via `NinjaTraderEventAdapter`.
2. `MarketDataNormalizer` transforms events into v1 contract DTOs.
3. `MarketDataPublisher` applies quote coalescing and lifecycle logging.
4. `NdjsonTcpMarketDataTransport` sends NDJSON over localhost TCP and resets on disconnect.
5. Optional instrument/session metadata can be published for strategy context.

## Integration note
Wire NinjaScript callbacks (quote/trade/bar/connection) to `NinjaTraderEventAdapter` methods.
Use `SimulationMarketDataFeed` for simulation-safe test mode before live callback wiring.

## Signal intake flow
1. `SignalIntakeTransport` listens on the configured signal TCP endpoint.
2. Inbound NDJSON lines are parsed into `TradeSignal`.
3. Malformed payloads return `ErrorEnvelope`.
4. Valid payloads route through `ExecutionBridge` and return `SignalAck`.
5. Source IDs are tracked and logged on first sighting.

## Phase 8 lifecycle flow
1. `ExecutionBridge` records accepted/rejected submission outcomes.
2. `OrderLifecycleTracker` records accepted/rejected/partial/full/canceled/ambiguous events.
3. `ExecutionJournal` appends NDJSON lifecycle events.
4. `ActualStateSnapshotStore` maintains persisted last-known order states.

## Phase 9 reconciliation flow
1. `ExpectedStateSnapshotStore` tracks expected working orders and expected positions.
2. `ActualStateSnapshotStore` provides observed order state snapshots.
3. `ReconciliationEngine` compares expected vs actual positions and working orders.
4. `ExecutionBridge.RunStartupRecoveryCheck()` and `RunReconnectRecoveryCheck()` emit persisted reports and disarm on mismatch.
5. `ExecutionBridge.Arm()` is required for explicit re-arming after mismatch disarm.

## Phase 10 persistence hardening
1. Persistence stores report critical load corruption through `PersistenceHealthMonitor`.
2. Bridge startup records a `Startup` runtime marker via `RuntimeMarkersStore`.
3. On critical persistence corruption, the bridge starts disarmed (fail closed).
4. `ExecutionBridge.Shutdown()` records a `Shutdown` runtime marker.
5. Processed IDs, safety state, expected state, actual state, and execution journal are all persisted on disk.
