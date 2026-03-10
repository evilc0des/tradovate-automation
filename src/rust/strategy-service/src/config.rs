use std::env;
use std::net::SocketAddr;

use anyhow::{anyhow, Result};

#[derive(Clone, Debug)]
pub struct AppConfig {
    pub market_data_bind: String,
    pub signal_bind: String,
    pub allowed_account: String,
    pub allowed_instruments: Vec<String>,
    pub cooldown_ms: u64,
}

impl AppConfig {
    pub fn from_env() -> Self {
        let market_data_bind =
            env::var("MARKET_DATA_BIND").unwrap_or_else(|_| "127.0.0.1:9100".to_string());
        let signal_bind = env::var("SIGNAL_BIND").unwrap_or_else(|_| "127.0.0.1:9101".to_string());
        let allowed_account = env::var("ALLOWED_ACCOUNT").unwrap_or_else(|_| "SIM101".to_string());
        let allowed_instruments = env::var("ALLOWED_INSTRUMENTS")
            .unwrap_or_else(|_| "MES 06-26".to_string())
            .split(',')
            .map(|s| s.trim().to_string())
            .filter(|s| !s.is_empty())
            .collect();
        let cooldown_ms = env::var("COOLDOWN_MS")
            .ok()
            .and_then(|value| value.parse::<u64>().ok())
            .unwrap_or(2000);

        Self {
            market_data_bind,
            signal_bind,
            allowed_account,
            allowed_instruments,
            cooldown_ms,
        }
    }

    pub fn validate(&self) -> Result<()> {
        let _market_addr: SocketAddr = self
            .market_data_bind
            .parse()
            .map_err(|_| anyhow!("MARKET_DATA_BIND must be a valid host:port"))?;
        let _signal_addr: SocketAddr = self
            .signal_bind
            .parse()
            .map_err(|_| anyhow!("SIGNAL_BIND must be a valid host:port"))?;

        if self.allowed_account.trim().is_empty() {
            return Err(anyhow!("ALLOWED_ACCOUNT cannot be empty"));
        }
        if self.allowed_instruments.is_empty() {
            return Err(anyhow!("ALLOWED_INSTRUMENTS must include at least one instrument"));
        }
        if self.cooldown_ms > 60_000 {
            return Err(anyhow!("COOLDOWN_MS is too large for v1 (max 60000)"));
        }

        Ok(())
    }
}
