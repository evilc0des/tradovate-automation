mod config;
mod errors;
mod features;
mod models;
mod state;
mod strategy;
mod transport;

use anyhow::{Context, Result};
use tokio::io::{AsyncBufReadExt, BufReader};
use tokio::net::TcpListener;
use tracing::debug;
use tracing::{error, info, warn};

use crate::config::AppConfig;
use crate::errors::{log_error, log_message, ErrorKind};
use crate::models::MarketDataMessage;
use crate::state::MarketState;
use crate::strategy::DeterministicStrategy;

#[tokio::main]
async fn main() -> Result<()> {
    tracing_subscriber::fmt()
        .with_env_filter("info")
        .init();

    let cfg = AppConfig::from_env();
    if let Err(err) = cfg.validate() {
        log_error(ErrorKind::Config, "config_validation", &err);
        return Err(err);
    }

    info!(market_data_bind = %cfg.market_data_bind, signal_bind = %cfg.signal_bind, "starting strategy service");

    let listener = TcpListener::bind(&cfg.market_data_bind)
        .await
        .with_context(|| format!("failed to bind market data endpoint {}", cfg.market_data_bind))?;

    let mut market_state = MarketState::default();
    let mut strategy = DeterministicStrategy::new(cfg.cooldown_ms);

    loop {
        tokio::select! {
            _ = tokio::signal::ctrl_c() => {
                info!("shutdown signal received; stopping strategy service");
                break;
            }
            accept_result = listener.accept() => {
                let (socket, peer) = accept_result.context("failed to accept connection")?;
                info!(%peer, "market data client connected");

                let mut reader = BufReader::new(socket);
                let mut line = String::new();

                loop {
                    line.clear();
                    let bytes = match reader
                        .read_line(&mut line)
                        .await
                        .context("failed to read market data frame") {
                            Ok(bytes) => bytes,
                            Err(err) => {
                                log_error(ErrorKind::Transport, "market_data_read", &err);
                                break;
                            }
                        };

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
                            log_message(ErrorKind::Parse, "market_data_parse", trimmed);
                            continue;
                        }
                    };

                    if msg.message_type != "MarketDataMessage" || msg.version != "v1" {
                        warn!(message_type = %msg.message_type, version = %msg.version, "dropping unsupported envelope");
                        log_message(ErrorKind::Protocol, "market_data_envelope", "unsupported messageType/version");
                        continue;
                    }

                    debug!(
                        source_id = %msg.source_id,
                        correlation_id = %msg.correlation_id,
                        event_type = %msg.event_type,
                        last_size = ?msg.last_size,
                        "accepted market data frame"
                    );

                    market_state.update_quote(&msg.instrument, msg.bid, msg.ask, msg.last_price, msg.timestamp);

                    if let Some(signal) = strategy.on_market_data(&cfg, &market_state, &msg) {
                        if let Err(err) = transport::send_signal(&cfg.signal_bind, &signal).await {
                            log_error(ErrorKind::Transport, "signal_dispatch", &err);
                            error!(error = %err, signal_id = %signal.signal_id, "failed to dispatch signal");
                        } else {
                            info!(signal_id = %signal.signal_id, instrument = %signal.instrument, side = %signal.side, "signal dispatched");
                        }
                    }
                }
            }
        }
    }

    info!("strategy service shutdown complete");
    Ok(())
}
