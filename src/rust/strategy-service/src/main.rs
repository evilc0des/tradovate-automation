mod cli;
mod config;
mod errors;
mod features;
mod models;
mod state;
mod strategy;
mod transport;

use anyhow::{Context, Result};
use clap::Parser;
use std::collections::HashSet;
use tokio::io::{AsyncBufReadExt, BufReader};
use tokio::net::TcpListener;
use tracing::debug;
use tracing::{error, info, warn};
use tracing_subscriber::EnvFilter;

use crate::cli::Cli;
use crate::config::AppConfig;
use crate::errors::{log_error, log_message, ErrorKind};
use crate::models::{BarUpdateMessage, InboundEnvelope, MarketDataMessage, QuoteUpdateMessage, TradePrintMessage};
use crate::state::MarketState;
use crate::strategy::build_strategy;

#[tokio::main]
async fn main() -> Result<()> {
    let env_filter = EnvFilter::try_from_default_env().unwrap_or_else(|_| EnvFilter::new("info"));
    tracing_subscriber::fmt()
        .with_env_filter(env_filter)
        .init();

    let cfg = AppConfig::from_cli(Cli::parse());
    if let Err(err) = cfg.validate() {
        log_error(ErrorKind::Config, "config_validation", &err);
        return Err(err);
    }

    info!(
        market_data_bind = %cfg.market_data_bind,
        signal_bind = %cfg.signal_bind,
        strategy = %cfg.strategy_name,
        force_trade_once = cfg.force_trade_once,
        force_trade_side = %cfg.force_trade_side,
        "starting strategy service"
    );

    let listener = TcpListener::bind(&cfg.market_data_bind)
        .await
        .with_context(|| format!("failed to bind market data endpoint {}", cfg.market_data_bind))?;

    let mut market_state = MarketState::default();
    let mut strategy = build_strategy(&cfg);
    let mut warned_instruments: HashSet<String> = HashSet::new();
    info!(strategy_id = %strategy.strategy_id(), "active strategy loaded");

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

                    let envelope = match serde_json::from_str::<InboundEnvelope>(trimmed) {
                        Ok(env) => env,
                        Err(err) => {
                            warn!(error = %err, raw = trimmed, "invalid market data frame");
                            log_message(ErrorKind::Parse, "market_data_parse", trimmed);
                            continue;
                        }
                    };

                    if envelope.version != "v1" {
                        warn!(message_type = %envelope.message_type, version = %envelope.version, "dropping unsupported envelope version");
                        log_message(ErrorKind::Protocol, "market_data_envelope", "unsupported version");
                        continue;
                    }

                    if envelope.message_type == "HeartbeatMessage" {
                        debug!("heartbeat frame received");
                        continue;
                    }

                    let (msg, is_bar) = if envelope.message_type == "MarketDataMessage" {
                        match serde_json::from_str::<MarketDataMessage>(trimmed) {
                            Ok(msg) => (msg, false),
                            Err(err) => {
                                warn!(error = %err, raw = trimmed, "invalid MarketDataMessage payload");
                                log_message(ErrorKind::Parse, "market_data_parse", trimmed);
                                continue;
                            }
                        }
                    } else if envelope.message_type == "QuoteUpdateMessage" {
                        match serde_json::from_str::<QuoteUpdateMessage>(trimmed) {
                            Ok(quote) => (MarketDataMessage {
                                message_type: "MarketDataMessage".to_string(),
                                version: quote.version,
                                timestamp: quote.timestamp,
                                source_id: quote.source_id,
                                correlation_id: quote.correlation_id,
                                instrument: quote.instrument,
                                event_type: "QuoteUpdate".to_string(),
                                last_price: None,
                                bid: Some(quote.bid),
                                ask: Some(quote.ask),
                                last_size: None,
                            }, false),
                            Err(err) => {
                                warn!(error = %err, raw = trimmed, "invalid QuoteUpdateMessage payload");
                                log_message(ErrorKind::Parse, "market_data_parse", trimmed);
                                continue;
                            }
                        }
                    } else if envelope.message_type == "TradePrintMessage" {
                        match serde_json::from_str::<TradePrintMessage>(trimmed) {
                            Ok(trade) => (MarketDataMessage {
                                message_type: "MarketDataMessage".to_string(),
                                version: trade.version,
                                timestamp: trade.timestamp,
                                source_id: trade.source_id,
                                correlation_id: trade.correlation_id,
                                instrument: trade.instrument,
                                event_type: "TradePrint".to_string(),
                                last_price: Some(trade.price),
                                bid: None,
                                ask: None,
                                last_size: Some(trade.size),
                            }, false),
                            Err(err) => {
                                warn!(error = %err, raw = trimmed, "invalid TradePrintMessage payload");
                                log_message(ErrorKind::Parse, "market_data_parse", trimmed);
                                continue;
                            }
                        }
                    } else if envelope.message_type == "BarUpdateMessage" {
                        match serde_json::from_str::<BarUpdateMessage>(trimmed) {
                            Ok(bar) => {
                                market_state.update_bar_close(&bar.instrument, bar.close);
                                debug!(
                                    instrument = %bar.instrument,
                                    interval = ?bar.interval,
                                    close = bar.close,
                                    "bar close — EMAs updated"
                                );
                                (MarketDataMessage {
                                    message_type: "MarketDataMessage".to_string(),
                                    version: bar.version,
                                    timestamp: bar.timestamp,
                                    source_id: bar.source_id,
                                    correlation_id: bar.correlation_id,
                                    instrument: bar.instrument,
                                    event_type: "BarUpdate".to_string(),
                                    last_price: Some(bar.close),
                                    bid: None,
                                    ask: None,
                                    last_size: None,
                                }, true)
                            }
                            Err(err) => {
                                warn!(error = %err, raw = trimmed, "invalid BarUpdateMessage payload");
                                log_message(ErrorKind::Parse, "market_data_parse", trimmed);
                                continue;
                            }
                        }
                    } else {
                        warn!(message_type = %envelope.message_type, "dropping unsupported envelope type");
                        log_message(ErrorKind::Protocol, "market_data_envelope", "unsupported messageType");
                        continue;
                    };

                    if msg.version != "v1" {
                        warn!(message_type = %msg.message_type, version = %msg.version, "dropping unsupported market data version");
                        log_message(ErrorKind::Protocol, "market_data_envelope", "unsupported MarketDataMessage version");
                        continue;
                    }

                    debug!(
                        source_id = %msg.source_id,
                        correlation_id = %msg.correlation_id,
                        event_type = %msg.event_type,
                        last_size = ?msg.last_size,
                        "accepted market data frame"
                    );

                    if !is_bar {
                        market_state.update_quote(&msg.instrument, msg.bid, msg.ask, msg.last_price, msg.timestamp);
                    }

                    if !cfg.allowed_instruments.iter().any(|i| i == &msg.instrument) {
                        if warned_instruments.insert(msg.instrument.clone()) {
                            warn!(
                                instrument = %msg.instrument,
                                allowed = ?cfg.allowed_instruments,
                                "received data for instrument not in allowed list — update --instruments flag"
                            );
                        }
                        continue;
                    }

                    let features = features::compute_features(&market_state, &msg.instrument);
                    if let Some(signal) = strategy.on_market_data(&cfg, &msg, features.as_ref()) {
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
