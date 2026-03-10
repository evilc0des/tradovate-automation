use std::env;

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

        Self {
            market_data_bind,
            signal_bind,
            allowed_account,
            allowed_instruments,
            cooldown_ms: 2000,
        }
    }
}
