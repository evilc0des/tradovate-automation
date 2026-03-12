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
    /// Which strategy module to activate.  Accepted values: `deterministic`, `ema-momentum`.
    pub strategy_name: String,
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
        if self.force_trade_once && self.force_trade_side != "Buy" && self.force_trade_side != "Sell" {
            return Err(anyhow!("--force-trade-side must be Buy or Sell when --force-trade-once is set"));
        }

        // strategy_name is already constrained by the ValueEnum — this is a belt-and-suspenders check.
        const VALID_STRATEGIES: &[&str] = &["deterministic", "ema-momentum"];
        if !VALID_STRATEGIES.contains(&self.strategy_name.as_str()) {
            return Err(anyhow!(
                "--strategy must be one of: {}",
                VALID_STRATEGIES.join(", ")
            ));
        }

        Ok(())
    }
}

