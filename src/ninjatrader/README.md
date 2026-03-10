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

## Publisher flow
1. NinjaTrader callback data is mapped into event records via `NinjaTraderEventAdapter`.
2. `MarketDataNormalizer` transforms events into v1 contract DTOs.
3. `MarketDataPublisher` applies quote coalescing and lifecycle logging.
4. `NdjsonTcpMarketDataTransport` sends NDJSON over localhost TCP and resets on disconnect.
5. Optional instrument/session metadata can be published for strategy context.

## Integration note
Wire NinjaScript callbacks (quote/trade/bar/connection) to `NinjaTraderEventAdapter` methods.
Use `SimulationMarketDataFeed` for simulation-safe test mode before live callback wiring.
