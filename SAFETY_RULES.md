# Safety Rules

## Defaults
- `LIVE_TRADING_ENABLED` must default to `false`.
- Runtime starts disarmed unless explicitly armed by operator flow.

## Signal Acceptance
- Reject missing/invalid schema fields.
- Reject stale signals older than `MAX_SIGNAL_AGE_MS`.
- Reject duplicate `signalId`.
- Reject account or instrument outside allowlist.

## Risk Limits
- Enforce `MAX_ORDER_QUANTITY`.
- Block unsupported order types in v1 (market only).
- Require explicit strategy/action mapping.

## Runtime Uncertainty Triggers
- Transport disconnect during ambiguous order state
- Persistence corruption
- Reconciliation mismatch
- Unknown order status transition

## Fail-Closed Behavior
- Block new order submissions.
- Persist disarmed reason.
- Require explicit operator re-arm action.
