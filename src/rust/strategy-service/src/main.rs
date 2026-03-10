mod config;
mod models;
mod state;
mod strategy;
mod transport;

use anyhow::{Context, Result};
use tokio::io::{AsyncBufReadExt, BufReader};
use tokio::net::TcpListener;
use tracing::{error, info, warn};

use crate::config::AppConfig;
use crate::models::MarketDataMessage;
use crate::state::MarketState;
use crate::strategy::DeterministicStrategy;

#[tokio::main]
async fn main() -> Result<()> {
    tracing_subscriber::fmt()
        .with_env_filter("info")
        .init();

    let cfg = AppConfig::from_env();
    info!(market_data_bind = %cfg.market_data_bind, signal_bind = %cfg.signal_bind, "starting strategy service");

    let listener = TcpListener::bind(&cfg.market_data_bind)
        .await
        .with_context(|| format!("failed to bind market data endpoint {}", cfg.market_data_bind))?;

    let mut market_state = MarketState::default();
    let mut strategy = DeterministicStrategy::new(cfg.cooldown_ms);

    loop {
        let (socket, peer) = listener.accept().await.context("failed to accept connection")?;
        info!(%peer, "market data client connected");

        let mut reader = BufReader::new(socket);
        let mut line = String::new();

        loop {
            line.clear();
            let bytes = reader
                .read_line(&mut line)
                .await
                .context("failed to read market data frame")?;

            if bytes == 0 {
                info!(%peer, "market data client disconnected");
                break;
            }

            let trimmed = line.trim();
            if trimmed.is_empty() {
                continue;
            }

            let parsed = serde_json::from_str::<MarketDataMessage>(trimmed);
            let msg = match parsed {
                Ok(msg) => msg,
                Err(err) => {
                    warn!(error = %err, raw = trimmed, "invalid market data frame");
                    continue;
                }
            };

            if msg.message_type != "MarketDataMessage" || msg.version != "v1" {
                warn!(message_type = %msg.message_type, version = %msg.version, "dropping unsupported envelope");
                continue;
            }

            // Keep state updates simple for MVP; strategy only needs quote + last.
            market_state.update_quote(&msg.instrument, msg.bid, msg.ask, msg.last_price);

            if let Some(signal) = strategy.on_market_data(&cfg, &market_state, &msg) {
                if let Err(err) = transport::send_signal(&cfg.signal_bind, &signal).await {
                    error!(error = %err, signal_id = %signal.signal_id, "failed to dispatch signal");
                } else {
                    info!(signal_id = %signal.signal_id, instrument = %signal.instrument, side = %signal.side, "signal dispatched");
                }
            }
        }
    }
}
