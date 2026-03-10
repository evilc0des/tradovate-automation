# Test Plan

## Goals
- Validate schema compatibility across runtimes.
- Validate deterministic strategy behavior.
- Validate guarded execution safety checks.

## Unit Tests
- Rust message parsing and state updates
- Rust signal generation and cooldown behavior
- NinjaTrader signal validation and dedup logic
- Risk checks for account, instrument, quantity, staleness

## Integration Tests
- NDJSON framing over localhost TCP
- Market data ingestion to signal emission
- Duplicate signal replay handling

## Resilience Scenarios
- Process restart with persisted dedup state
- Signal channel disconnect and reconnect
- Malformed frame handling without crash
- Simulated persistence corruption (must disarm)

## MVP Exit Criteria
- End-to-end simulation order path works.
- No order submission when disarmed.
- Duplicate signals are rejected.
- Safety logs include signal and correlation IDs.
