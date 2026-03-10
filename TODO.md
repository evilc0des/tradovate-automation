# TODO

## Phase 0 - Project Rescope and Foundations

- [ ] Update architecture to reflect a two-runtime system:
  - [ ] NinjaTrader runtime
  - [ ] Rust strategy runtime
- [ ] Define clear responsibility boundary between NinjaTrader and Rust
- [x] Define local transport for market data publishing
- [x] Define local transport for signal submission
- [x] Decide whether the same transport will be used for both directions
- [x] Add shared normalized schema definitions for:
  - [x] outbound market data messages
  - [x] inbound trade signals
  - [x] connection state events
  - [ ] optional account state snapshots
- [x] Define serialization format:
  - [x] JSON for v1
  - [ ] optional binary protocol later
- [x] Decide message framing rules for stream transport
- [x] Decide local port / named pipe / socket conventions
- [x] Add correlation IDs and source IDs to all important messages

## Phase 1 - Repository Structure

- [x] Define repository layout for mixed NinjaTrader + Rust development
- [x] Add directories for:
  - [x] `/docs`
  - [x] `/src/ninjatrader`
  - [x] `/src/rust/strategy-service`
  - [x] `/src/shared-schemas`
  - [x] `/tests`
- [x] Define how shared schemas are maintained across C# and Rust
- [x] Add protocol documentation
- [x] Add local environment configuration examples
- [x] Add dev-run instructions for both runtimes

## Phase 2 - Shared Message Contracts

- [x] Define `MarketDataMessage` schema
- [x] Define `BarUpdateMessage` schema
- [x] Define `QuoteUpdateMessage` schema
- [x] Define `TradePrintMessage` schema if needed
- [x] Define `ConnectionStateMessage` schema
- [x] Define `TradeSignal` schema
- [x] Define `SignalAck` or local acknowledgment flow if needed
- [x] Define error/event envelope model
- [x] Add version field to message schema
- [x] Add schema docs with examples
- [x] Add validation rules for all message types

## Phase 3 - NinjaTrader Market Data Publisher

- [x] Build NinjaTrader-side market data publishing module
- [x] Capture market data from NinjaTrader events
- [x] Normalize outgoing market data into internal DTOs
- [x] Publish tick/quote/bar data to local Rust service
- [x] Publish connection state changes
- [x] Publish instrument/session metadata if needed
- [x] Handle transport disconnects safely
- [x] Add throttling or coalescing where appropriate
- [x] Add logging for market data publisher lifecycle
- [x] Add simulation-safe test mode for publisher

## Phase 4 - Rust Strategy Service Foundation

- [x] Create Rust workspace or crate structure for strategy service
- [x] Add config loader in Rust
- [x] Add structured logging in Rust
- [x] Add local transport client/server implementation in Rust
- [x] Add inbound market data parser in Rust
- [x] Add internal event loop
- [x] Add normalized in-memory market state
- [ ] Add graceful shutdown handling
- [ ] Add error classification and logging
- [ ] Add configuration validation in Rust

## Phase 5 - Rust Market State and Strategy Pipeline

- [x] Create internal market data models in Rust
- [x] Build market state store in Rust
- [x] Track best bid/ask
- [x] Track last trade
- [ ] Track rolling bars if needed
- [ ] Track simple session state
- [ ] Add feature computation layer
- [x] Add rule-based strategy engine interface
- [x] Implement first simple strategy:
  - [x] minimal deterministic rule-based signal
- [x] Add signal cooldown / throttling logic
- [x] Add strategy-side deduplication safeguards
- [x] Add signal emitter module

## Phase 6 - Signal Transport Back To NinjaTrader

- [x] Build Rust-side signal publishing flow
- [ ] Build NinjaTrader-side signal intake transport
- [ ] Parse incoming signal messages in NinjaTrader
- [ ] Validate schema
- [ ] Validate semantic rules
- [ ] Reject malformed messages safely
- [ ] Add signal source tracking
- [ ] Add end-to-end correlation ID support
- [ ] Add local transport reconnect handling

## Phase 7 - NinjaTrader Execution Bridge Core

- [x] Build normalized signal DTO in NinjaTrader
- [x] Build deduplication by signal ID
- [ ] Persist processed signal IDs
- [ ] Build safety state manager
- [x] Build risk engine
- [x] Build config validation
- [x] Restrict allowed accounts
- [x] Restrict allowed instruments
- [x] Restrict max order quantity
- [x] Restrict live trading by config switch
- [ ] Enforce session windows
- [x] Enforce staleness checks
- [ ] Build simple market order submission path
- [ ] Tag all orders with signal ID / correlation ID
- [ ] Log submission attempts and outcomes

## Phase 8 - Order and Execution Tracking

- [ ] Capture order lifecycle events from NinjaTrader
- [ ] Capture execution events from NinjaTrader
- [ ] Track order accepted
- [ ] Track order rejected
- [ ] Track partial fills
- [ ] Track full fills
- [ ] Track cancel events
- [ ] Track connection-related execution ambiguity
- [ ] Persist execution journal
- [ ] Build internal actual-state snapshots

## Phase 9 - Reconciliation and Recovery

- [ ] Build expected-state models
- [ ] Build actual-state models
- [ ] Compare expected positions vs actual positions
- [ ] Compare expected working orders vs actual working orders
- [ ] Detect ambiguous post-submit states
- [ ] Detect startup mismatches
- [ ] Detect reconnect mismatches
- [ ] Generate reconciliation reports
- [ ] Transition to safe mode on mismatch
- [ ] Add startup recovery flow
- [ ] Add reconnect recovery flow
- [ ] Require explicit re-arming when needed

## Phase 10 - Persistence

- [ ] Persist processed signal IDs
- [ ] Persist safety state snapshot
- [ ] Persist expected position snapshot
- [ ] Persist execution journal
- [ ] Persist startup/shutdown markers
- [ ] Handle corrupted persistence files safely
- [ ] Start disarmed on critical persistence corruption

## Phase 11 - Transport Hardening

- [ ] Choose and finalize local transport for v1
- [ ] Add message framing
- [ ] Add heartbeats
- [ ] Add connection lifecycle logging
- [ ] Add reconnect strategy for non-critical paths
- [ ] Ensure no blind resubmission of ambiguous trade signals
- [ ] Add backpressure handling for market data
- [ ] Add bounded queues where needed
- [ ] Add safe drop/coalesce policy for excessive market data volume

## Phase 12 - Rust Strategy Extensibility

- [ ] Add pluggable strategy trait/interface in Rust
- [ ] Separate strategy logic from transport logic
- [ ] Separate feature pipeline from signal output
- [ ] Add support for multiple strategy modules later
- [ ] Keep v1 running with one active strategy
- [ ] Add deterministic replay mode for stored market data later

## Phase 13 - Observability

- [ ] Add structured logs on NinjaTrader side
- [ ] Add structured logs on Rust side
- [ ] Log market data publisher status
- [ ] Log strategy signal generation
- [ ] Log signal receipt and validation
- [ ] Log risk decisions
- [ ] Log order submissions
- [ ] Log order lifecycle
- [ ] Log reconciliation outcomes
- [ ] Log safety state transitions
- [ ] Add per-day log files if practical

## Phase 14 - Testing

- [ ] Unit tests for NinjaTrader-side pure logic:
  - [ ] signal validation
  - [ ] risk rules
  - [ ] deduplication
  - [ ] reconciliation
  - [ ] config validation
- [ ] Unit tests for Rust-side logic:
  - [ ] market data parsing
  - [ ] state aggregation
  - [ ] feature computation
  - [ ] signal generation
  - [ ] config validation
- [ ] Integration tests for local transport
- [ ] Integration tests for end-to-end message flow
- [ ] Simulation tests for market-data-in -> signal-out -> order-submit flow
- [ ] Manual tests for disconnect/reconnect scenarios
- [ ] Manual tests for duplicate signal scenarios
- [ ] Manual tests for restart recovery scenarios

## Phase 15 - First Working Vertical Slice

- [ ] NinjaTrader publishes one simple normalized market data stream
- [ ] Rust service consumes the stream
- [ ] Rust service computes a simple deterministic strategy rule
- [ ] Rust service emits one normalized trade signal
- [ ] NinjaTrader receives the signal
- [ ] NinjaTrader validates and risk-checks the signal
- [ ] NinjaTrader submits a simulation market order
- [ ] NinjaTrader logs and persists the outcome

## Phase 16 - Live Readiness Preparation

- [ ] Run extended simulation soak tests
- [ ] Confirm duplicate signal protection
- [ ] Confirm reconnect behavior
- [ ] Confirm startup recovery behavior
- [ ] Confirm no auto-arm by default
- [ ] Confirm live mode remains explicitly disabled by default
- [ ] Confirm reconciliation mismatch triggers safe mode
- [ ] Confirm transport failure does not cause unsafe execution

## Nice-to-Have Later

- [ ] Binary protocol for lower overhead
- [ ] shared IDL or codegen for Rust/C# message contracts
- [ ] replay tooling for recorded market data
- [ ] lightweight local dashboard
- [ ] alerting to Telegram/Slack
- [ ] model-based strategy inference in Rust
- [ ] deeper order book processing
- [ ] broker abstraction beyond NinjaTrader