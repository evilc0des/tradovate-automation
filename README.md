# NinjaTrader Tradovate Execution Bridge

A guarded local trading system for NinjaTrader 8 connected to Tradovate, with a Rust-based external strategy service.

## Overview

This project is designed for a setup where:

- NinjaTrader 8 is connected to a Tradovate account
- NinjaTrader acts as the local broker-connected runtime
- NinjaTrader ingests market data from its own feeds and connection stack
- market data is forwarded from NinjaTrader to a local external Rust strategy service
- the Rust strategy service processes market data and generates normalized trade signals
- NinjaTrader receives those trade signals back and executes them through a guarded execution bridge
- the entire critical path runs locally on the same machine

This architecture exists because:

- direct Tradovate API access is not available
- browser automation is too fragile for reliable trading
- NinjaTrader provides programmatic hooks for market data and order execution
- strategy logic is easier to maintain and scale in Rust than inside NinjaScript alone

## Core Idea

Use NinjaTrader as:

- the market data source
- the broker-connected execution shell
- the account and order state observer

Use the external Rust service as:

- the strategy engine
- the signal generation engine
- the market-state computation engine
- the feature pipeline for model-based or rule-based strategies

Use the execution bridge as:

- the safety gate between strategy intent and live order submission
- the reconciliation layer between expected and actual trading state
- the persistence and journaling layer
- the runtime that can disarm itself when anything becomes unsafe

## Primary Goals

- local-first low-latency architecture
- strategy logic in Rust
- guarded execution through NinjaTrader
- deterministic signal handling
- duplicate-signal prevention
- recoverable restart and reconnect behavior
- strong safety and risk controls
- explicit reconciliation between strategy intent and platform reality

## Non-Goals

- cloud-native distributed deployment in v1
- HFT-grade infrastructure
- multi-broker support in v1
- remote strategy hosting in v1
- rich web dashboard in v1
- broker API abstraction beyond NinjaTrader in v1

## System Architecture

## 1. NinjaTrader Market Data Publisher

NinjaTrader captures market data and internal runtime events, then publishes normalized local messages outward to the Rust strategy service.

Examples of data that may be forwarded:
- Level 1 ticks
- bid/ask updates
- last trade updates
- bar updates
- instrument/session metadata
- optional order book derived data if available
- connection state events
- account state snapshots if needed for strategy awareness

## 2. Rust Strategy Service

A local Rust process consumes market data from NinjaTrader and performs:

- market state aggregation
- feature engineering
- rule-based signal generation
- model-based inference if needed later
- signal throttling and signal normalization
- strategy-side deduplication if needed
- emission of normalized execution intents back to NinjaTrader

## 3. NinjaTrader Execution Bridge

NinjaTrader receives normalized signals from the Rust service and:

- validates schema
- validates account/instrument/session/risk rules
- deduplicates by signal ID
- submits orders through NinjaTrader
- observes fills, rejections, and position changes
- persists state
- reconciles expected state vs actual account/platform state
- enters safe mode or disarms when uncertainty appears

## Local Deployment Assumption

Everything runs on the same machine:

- NinjaTrader Desktop
- Rust strategy service
- local config files
- local persistence
- local logs
- local signal transport
- local market data transport

No cloud dependency is required for the core execution path in v1.

## Recommended Communication Pattern

### Market Data Path
NinjaTrader -> local transport -> Rust strategy service

### Signal Path
Rust strategy service -> local transport -> NinjaTrader execution bridge

### Recommended v1 transports
For simplicity, choose one of:
- localhost TCP
- named pipes
- localhost WebSocket
- file-based fallback only for signals, not market data

### Recommended stance
- use a streaming local transport for market data
- use structured normalized messages
- do not use watched-folder transport for high-frequency market data
- file-based transport can still be used for testing or low-frequency signal injection

## Suggested Message Flow

1. NinjaTrader receives market data from broker/platform connection
2. NinjaTrader normalizes market data into internal outbound message DTOs
3. NinjaTrader publishes those DTOs to the local Rust strategy service
4. Rust strategy service updates local market state
5. Rust strategy service generates a normalized trade signal
6. Rust strategy service sends the signal back to NinjaTrader
7. NinjaTrader execution bridge validates and risk-checks the signal
8. NinjaTrader submits the order
9. NinjaTrader tracks order lifecycle and fills
10. NinjaTrader reconciles expected vs actual state
11. If a mismatch appears, the bridge disarms or enters safe mode

## Why Rust For The Strategy Service

Rust is a good fit for the external strategy service because it offers:

- strong performance
- low runtime overhead
- excellent control over memory and concurrency
- safer systems programming than C/C++
- a good fit for stateful market data pipelines
- a strong foundation for future low-latency local services

Rust should be used for:

- market data ingestion service
- internal event pipeline
- strategy logic
- signal generation
- optional backtest-compatible core logic shared later

## Why NinjaTrader Still Matters

Even though strategy logic lives in Rust, NinjaTrader still remains important because it provides:

- connection to Tradovate
- market data access
- account state visibility
- order routing path
- execution lifecycle callbacks
- desktop-local integration point

## First Milestone

Implement a minimal but safe vertical slice:

### NinjaTrader side
- publish one normalized market data stream locally
- receive one normalized signal stream locally
- validate incoming signals
- deduplicate by signal ID
- apply risk checks
- submit a simple market order in simulation
- log order lifecycle events
- persist state across restart

### Rust side
- consume normalized market data
- maintain simple market state
- generate a basic rule-based signal
- emit normalized trade signals back to NinjaTrader
- log outgoing signals

## Initial Scope Recommendation

Keep v1 narrow:

- one machine only
- one strategy service process
- one account
- small set of instruments
- market orders first
- simulation first
- strong safety rules
- no remote control plane
- no production-grade GUI yet

## Safety Philosophy

This codebase is about guarded execution.

It must assume that any of these can happen:

- reconnect issues
- duplicate signals
- delayed callbacks
- partial fills
- stale internal state
- corrupted persistence
- manual intervention in NinjaTrader
- strategy-side signal duplication
- order submission ambiguity

Therefore:
- every critical action must be logged
- every signal must have a unique signal ID
- every restart must perform recovery checks
- live trading must be off by default
- on uncertainty, the bridge must fail closed

## Repository Direction

This repository should eventually contain:

- NinjaTrader-side bridge code for market data publication and guarded execution
- Rust external strategy service
- normalized message schemas
- local transport interfaces
- risk and reconciliation logic
- persistence and journaling
- simulation-first test harnesses
- configuration and operational docs