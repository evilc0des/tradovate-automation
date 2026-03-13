use std::net::SocketAddr;

use anyhow::{anyhow, Result};

use crate::cli::Cli;

#[derive(Clone, Debug)]
pub struct AppConfig {
    pub market_data_bind: String,
    pub signal_bind: String,
    pub allowed_account: String,
    pub allowed_instruments: Vec<String>,
    pub cooldown_ms: u64,
    pub force_trade_once: bool,
    pub force_trade_side: String,
    /// Which strategy module to activate.
    /// Accepted values: `deterministic`, `ema-momentum`, `heikin-ashi`,
    /// `tape-burst-scalper`.
    pub strategy_name: String,

    // ── Tape-burst scalper parameters ─────────────────────────────────────────

    /// Instrument tick size in price units (e.g. 0.25 for NQ / MNQ).
    /// Used to convert price moves to ticks for target and stop calculations.
    pub tape_tick_size: f64,

    /// Minimum signed volume imbalance (buy − sell contracts) over the
    /// 2-second window required to recognise a burst.
    pub tape_micro_delta_min: i64,

    /// Minimum aggressor-volume ratio (buy/sell for longs, sell/buy for shorts)
    /// over the 2-second window.
    pub tape_aggression_ratio_min: f64,

    /// Minimum speed factor: 2-second pps must exceed 5-second baseline pps
    /// by at least this multiplier before entry is allowed.
    pub tape_speed_factor_min: f64,

    /// Minimum price-response ratio in ticks per 10 aggressive contracts.
    /// Guards against aggression-without-movement (absorption) scenarios.
    pub tape_price_response_min_ticks: f64,

    /// Profit target in ticks for an open tape-burst position.
    pub tape_target_ticks: f64,

    /// Hard stop size in ticks for an open tape-burst position.
    pub tape_stop_ticks: f64,

    /// Maximum hold time in milliseconds before the time-stop exit fires.
    pub tape_time_stop_ms: u64,

    /// Absolute micro-delta magnitude in the 1-second window that triggers
    /// a flow-failure exit when it flips against the open position.
    pub tape_flip_delta: i64,

    /// Start of the allowed UTC trading session (format `"HH:MM"`).
    /// Entry signals are suppressed outside the `[start, end]` window.
    /// Default `"00:00"` applies no restriction.
    pub tape_session_start_utc: String,

    /// End of the allowed UTC trading session (format `"HH:MM"`).
    /// Default `"23:59"` applies no restriction.
    pub tape_session_end_utc: String,

    /// L1 best-ask (or best-bid) size threshold used as a lightweight
    /// near-wall proxy.  Entry is suppressed when the near-touch size meets
    /// or exceeds this value.  Set very high (default 1000) to disable.
    pub tape_wall_min_size: u32,
}

impl AppConfig {
    pub fn from_cli(cli: Cli) -> Self {
        Self {
            market_data_bind: cli.market_data_bind,
            signal_bind: cli.signal_bind,
            allowed_account: cli.account,
            allowed_instruments: cli
                .instruments
                .into_iter()
                .map(|s| s.trim().to_string())
                .filter(|s| !s.is_empty())
                .collect(),
            cooldown_ms: cli.cooldown_ms,
            force_trade_once: cli.force_trade_once,
            force_trade_side: cli.force_trade_side.trim().to_string(),
            strategy_name: cli.strategy.as_config_str().to_string(),
            tape_tick_size: cli.tape_tick_size,
            tape_micro_delta_min: cli.tape_micro_delta_min,
            tape_aggression_ratio_min: cli.tape_aggression_ratio_min,
            tape_speed_factor_min: cli.tape_speed_factor_min,
            tape_price_response_min_ticks: cli.tape_price_response_min_ticks,
            tape_target_ticks: cli.tape_target_ticks,
            tape_stop_ticks: cli.tape_stop_ticks,
            tape_time_stop_ms: cli.tape_time_stop_ms,
            tape_flip_delta: cli.tape_flip_delta,
            tape_session_start_utc: cli.tape_session_start_utc.trim().to_string(),
            tape_session_end_utc: cli.tape_session_end_utc.trim().to_string(),
            tape_wall_min_size: cli.tape_wall_min_size,
        }
    }

    pub fn validate(&self) -> Result<()> {
        let _market_addr: SocketAddr = self
            .market_data_bind
            .parse()
            .map_err(|_| anyhow!("--market-data-bind must be a valid host:port"))?;
        let _signal_addr: SocketAddr = self
            .signal_bind
            .parse()
            .map_err(|_| anyhow!("--signal-bind must be a valid host:port"))?;

        if self.allowed_account.trim().is_empty() {
            return Err(anyhow!("--account cannot be empty"));
        }
        if self.allowed_instruments.is_empty() {
            return Err(anyhow!("--instruments must include at least one instrument"));
        }
        if self.cooldown_ms > 60_000 {
            return Err(anyhow!("--cooldown-ms is too large for v1 (max 60000)"));
        }
        if self.force_trade_once
            && self.force_trade_side != "Buy"
            && self.force_trade_side != "Sell"
        {
            return Err(anyhow!(
                "--force-trade-side must be Buy or Sell when --force-trade-once is set"
            ));
        }

        // strategy_name is already constrained by the ValueEnum — this is a
        // belt-and-suspenders check.
        const VALID_STRATEGIES: &[&str] = &[
            "deterministic",
            "ema-momentum",
            "heikin-ashi",
            "tape-burst-scalper",
        ];
        if !VALID_STRATEGIES.contains(&self.strategy_name.as_str()) {
            return Err(anyhow!(
                "--strategy must be one of: {}",
                VALID_STRATEGIES.join(", ")
            ));
        }

        if self.tape_tick_size <= 0.0 {
            return Err(anyhow!("--tape-tick-size must be positive"));
        }
        if self.tape_target_ticks <= 0.0 {
            return Err(anyhow!("--tape-target-ticks must be positive"));
        }
        if self.tape_stop_ticks <= 0.0 {
            return Err(anyhow!("--tape-stop-ticks must be positive"));
        }

        Ok(())
    }
}

