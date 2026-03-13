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
- [x] Add graceful shutdown handling
- [x] Add error classification and logging
- [x] Add configuration validation in Rust

## Phase 5 - Rust Market State and Strategy Pipeline

- [x] Create internal market data models in Rust
- [x] Build market state store in Rust
- [x] Track best bid/ask
- [x] Track last trade
- [x] Track rolling bars if needed
- [x] Track simple session state
- [x] Add feature computation layer
- [x] Add rule-based strategy engine interface
- [x] Implement first simple strategy:
  - [x] minimal deterministic rule-based signal
- [x] Add signal cooldown / throttling logic
- [x] Add strategy-side deduplication safeguards
- [x] Add signal emitter module

## Phase 6 - Signal Transport Back To NinjaTrader

- [x] Build Rust-side signal publishing flow
- [x] Build NinjaTrader-side signal intake transport
- [x] Parse incoming signal messages in NinjaTrader
- [x] Validate schema
- [x] Validate semantic rules
- [x] Reject malformed messages safely
- [x] Add signal source tracking
- [x] Add end-to-end correlation ID support
- [x] Add local transport reconnect handling

## Phase 7 - NinjaTrader Execution Bridge Core

- [x] Build normalized signal DTO in NinjaTrader
- [x] Build deduplication by signal ID
- [x] Persist processed signal IDs
- [x] Build safety state manager
- [x] Build risk engine
- [x] Build config validation
- [x] Restrict allowed accounts
- [x] Restrict allowed instruments
- [x] Restrict max order quantity
- [x] Restrict live trading by config switch
- [x] Enforce session windows
- [x] Enforce staleness checks
- [x] Build simple market order submission path
- [x] Tag all orders with signal ID / correlation ID
- [x] Log submission attempts and outcomes

## Phase 8 - Order and Execution Tracking

- [x] Capture order lifecycle events from NinjaTrader
- [x] Capture execution events from NinjaTrader
- [x] Track order accepted
- [x] Track order rejected
- [x] Track partial fills
- [x] Track full fills
- [x] Track cancel events
- [x] Track connection-related execution ambiguity
- [x] Persist execution journal
- [x] Build internal actual-state snapshots

## Phase 9 - Reconciliation and Recovery

- [x] Build expected-state models
- [x] Build actual-state models
- [x] Compare expected positions vs actual positions
- [x] Compare expected working orders vs actual working orders
- [x] Detect ambiguous post-submit states
- [x] Detect startup mismatches
- [x] Detect reconnect mismatches
- [x] Generate reconciliation reports
- [x] Transition to safe mode on mismatch
- [x] Add startup recovery flow
- [x] Add reconnect recovery flow
- [x] Require explicit re-arming when needed

## Phase 10 - Persistence

- [x] Persist processed signal IDs
- [x] Persist safety state snapshot
- [x] Persist expected position snapshot
- [x] Persist execution journal
- [x] Persist startup/shutdown markers
- [x] Handle corrupted persistence files safely
- [x] Start disarmed on critical persistence corruption

## Phase 11 - Transport Hardening

- [x] Choose and finalize local transport for v1
- [x] Add message framing
- [x] Add heartbeats
- [x] Add connection lifecycle logging
- [x] Add reconnect strategy for non-critical paths
- [x] Ensure no blind resubmission of ambiguous trade signals
- [x] Add backpressure handling for market data
- [x] Add bounded queues where needed
- [x] Add safe drop/coalesce policy for excessive market data volume

## Phase 12 - Rust Strategy Extensibility

- [x] Add pluggable strategy trait/interface in Rust
- [x] Separate strategy logic from transport logic
- [x] Separate feature pipeline from signal output
- [x] Add support for multiple strategy modules later
- [x] Keep v1 running with one active strategy
- [ ] Add deterministic replay mode for stored market data later

## Phase 13 - Observability

- [x] Add structured logs on NinjaTrader side
- [x] Add structured logs on Rust side
- [x] Log market data publisher status
- [x] Log strategy signal generation
- [x] Log signal receipt and validation
- [x] Log risk decisions
- [x] Log order submissions
- [x] Log order lifecycle
- [x] Log reconciliation outcomes
- [x] Log safety state transitions
- [x] Add per-day log files if practical

## Phase 14 - Testing

- [x] Unit tests for NinjaTrader-side pure logic:
  - [x] signal validation
  - [x] risk rules
  - [x] deduplication
  - [x] reconciliation
  - [x] config validation
- [x] Unit tests for Rust-side logic:
  - [x] market data parsing
  - [x] state aggregation
  - [x] feature computation
  - [x] signal generation
  - [x] config validation
- [ ] Integration tests for local transport
- [ ] Integration tests for end-to-end message flow
- [ ] Simulation tests for market-data-in -> signal-out -> order-submit flow
- [ ] Manual tests for disconnect/reconnect scenarios
- [ ] Manual tests for duplicate signal scenarios
- [ ] Manual tests for restart recovery scenarios

## Phase 15 - First Working Vertical Slice

- [x] NinjaTrader publishes one simple normalized market data stream
- [x] Rust service consumes the stream
- [x] Rust service computes a simple deterministic strategy rule
- [x] Rust service emits one normalized trade signal
- [x] NinjaTrader receives the signal
- [x] NinjaTrader validates and risk-checks the signal
- [x] NinjaTrader submits a simulation market order
- [x] NinjaTrader logs and persists the outcome

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