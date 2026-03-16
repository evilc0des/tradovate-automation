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

    // ── Event blackout ────────────────────────────────────────────────────────

    /// Radius in minutes around each session/news event where new entries are
    /// blocked.  Signals with `instruction = "exit"` or `"flatten"` bypass
    /// this gate.  Default 3 means ±3 minutes.
    pub event_blackout_radius_mins: i64,

    /// Enable the external news calendar feed.  When `false`, only the static
    /// session events (Asia/London/NY open+close) apply.
    pub news_enabled: bool,

    /// URL for the weekly high-impact news calendar JSON.
    /// Default: Forex Factory public feed.
    pub news_api_url: String,

    /// How often (in seconds) to refresh the news calendar from the API.
    pub news_api_poll_secs: u64,

    /// Seconds after the last successful fetch before the feed is considered
    /// stale.  When stale the fail-safe policy blocks all new entries.
    pub news_api_stale_secs: u64,
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
            event_blackout_radius_mins: cli.event_blackout_radius_mins,
            news_enabled: cli.news_enabled,
            news_api_url: cli.news_api_url.trim().to_string(),
            news_api_poll_secs: cli.news_api_poll_secs,
            news_api_stale_secs: cli.news_api_stale_secs,
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

// ── tests ─────────────────────────────────────────────────────────────────────

#[cfg(test)]
mod tests {
    use super::*;

    fn valid_config() -> AppConfig {
        AppConfig {
            market_data_bind: "127.0.0.1:7001".to_string(),
            signal_bind: "127.0.0.1:7002".to_string(),
            allowed_account: "SIM123".to_string(),
            allowed_instruments: vec!["MES 06-26".to_string()],
            cooldown_ms: 500,
            force_trade_once: false,
            force_trade_side: "Buy".to_string(),
            strategy_name: "deterministic".to_string(),
            tape_tick_size: 0.25,
            tape_micro_delta_min: 10,
            tape_aggression_ratio_min: 2.0,
            tape_speed_factor_min: 1.5,
            tape_price_response_min_ticks: 0.5,
            tape_target_ticks: 4.0,
            tape_stop_ticks: 2.0,
            tape_time_stop_ms: 30_000,
            tape_flip_delta: 5,
            tape_session_start_utc: "00:00".to_string(),
            tape_session_end_utc: "23:59".to_string(),
            tape_wall_min_size: 1000,
            event_blackout_radius_mins: 3,
            news_enabled: false,
            news_api_url: "https://nfs.faireconomy.media/ff_calendar_thisweek.json".to_string(),
            news_api_poll_secs: 3600,
            news_api_stale_secs: 86400,
        }
    }

    #[test]
    fn valid_config_passes_validate() {
        assert!(valid_config().validate().is_ok());
    }

    #[test]
    fn invalid_market_data_bind_fails() {
        let mut cfg = valid_config();
        cfg.market_data_bind = "not-an-address".to_string();
        assert!(cfg.validate().is_err());
    }

    #[test]
    fn invalid_signal_bind_fails() {
        let mut cfg = valid_config();
        cfg.signal_bind = "bad:addr:extra".to_string();
        assert!(cfg.validate().is_err());
    }

    #[test]
    fn empty_account_fails() {
        let mut cfg = valid_config();
        cfg.allowed_account = "   ".to_string();
        assert!(cfg.validate().is_err());
    }

    #[test]
    fn empty_instruments_fails() {
        let mut cfg = valid_config();
        cfg.allowed_instruments = vec![];
        assert!(cfg.validate().is_err());
    }

    #[test]
    fn cooldown_ms_over_limit_fails() {
        let mut cfg = valid_config();
        cfg.cooldown_ms = 60_001;
        assert!(cfg.validate().is_err());
    }

    #[test]
    fn cooldown_ms_at_limit_passes() {
        let mut cfg = valid_config();
        cfg.cooldown_ms = 60_000;
        assert!(cfg.validate().is_ok());
    }

    #[test]
    fn force_trade_once_with_bad_side_fails() {
        let mut cfg = valid_config();
        cfg.force_trade_once = true;
        cfg.force_trade_side = "Long".to_string(); // invalid
        assert!(cfg.validate().is_err());
    }

    #[test]
    fn force_trade_once_false_ignores_side_value() {
        let mut cfg = valid_config();
        cfg.force_trade_once = false;
        cfg.force_trade_side = "NotAValidSide".to_string();
        assert!(cfg.validate().is_ok());
    }

    #[test]
    fn force_trade_once_with_sell_passes() {
        let mut cfg = valid_config();
        cfg.force_trade_once = true;
        cfg.force_trade_side = "Sell".to_string();
        assert!(cfg.validate().is_ok());
    }

    #[test]
    fn zero_tape_tick_size_fails() {
        let mut cfg = valid_config();
        cfg.tape_tick_size = 0.0;
        assert!(cfg.validate().is_err());
    }

    #[test]
    fn negative_tape_target_ticks_fails() {
        let mut cfg = valid_config();
        cfg.tape_target_ticks = -1.0;
        assert!(cfg.validate().is_err());
    }

    #[test]
    fn negative_tape_stop_ticks_fails() {
        let mut cfg = valid_config();
        cfg.tape_stop_ticks = -0.01;
        assert!(cfg.validate().is_err());
    }

    #[test]
    fn all_valid_non_deterministic_strategies_pass() {
        for name in &["ema-momentum", "heikin-ashi", "tape-burst-scalper"] {
            let mut cfg = valid_config();
            cfg.strategy_name = name.to_string();
            assert!(cfg.validate().is_ok(), "strategy {name} should pass validate()");
        }
    }

    #[test]
    fn unknown_strategy_name_fails() {
        let mut cfg = valid_config();
        cfg.strategy_name = "super-secret-strategy".to_string();
        assert!(cfg.validate().is_err());
    }
}

