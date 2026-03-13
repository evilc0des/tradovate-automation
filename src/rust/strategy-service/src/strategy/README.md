# Strategies

This directory contains all trading strategy implementations for the Rust strategy service. Each strategy lives in its own subdirectory with a dedicated `mod.rs` and `README.md`.

## Available Strategies

| Name (CLI `--strategy`) | Strategy ID | Description |
|-------------------------|-------------|-------------|
| `ema-momentum` | `ema-momentum-v1` | EMA 5/20 crossover trend-following |
| `heikin-ashi` | `heikin-ashi-v1` | Two-bar consecutive Heikin Ashi reversal |
| *(any other value)* | `deterministic-v1` | Spread-breakout connectivity test (default fallback) |

## Directory Structure

```
strategy/
├── mod.rs              ← Strategy trait, build_strategy factory, shared build_signal helper
├── README.md           ← This file
├── deterministic/
│   ├── mod.rs          ← DeterministicStrategy implementation
│   └── README.md
├── ema_momentum/
│   ├── mod.rs          ← EmaMomentumStrategy implementation
│   └── README.md
└── heikin_ashi/
    ├── mod.rs          ← HeikinAshiStrategy implementation + unit tests
    └── README.md
```

## Adding a New Strategy

1. Create a new subdirectory under `strategy/`, e.g. `strategy/my_strategy/`.
2. Add `mod.rs` implementing the `Strategy` trait (see any existing strategy for the pattern).
3. Add `README.md` documenting entry rules, parameters, and signal format.
4. Declare the submodule in `strategy/mod.rs`:
   ```rust
   mod my_strategy;
   pub use my_strategy::MyStrategy;
   ```
5. Add a match arm in the `build_strategy` factory in `strategy/mod.rs`:
   ```rust
   "my-strategy" => Box::new(MyStrategy::new(cfg.cooldown_ms)),
   ```

## Shared Utilities

`build_signal` is a `pub(crate)` helper defined in `mod.rs` that constructs a `TradeSignal` from a config, market data message, strategy ID, side, and reason string. All strategies use it via `super::build_signal(...)`.
