use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct MarketDataMessage {
    pub message_type: String,
    pub version: String,
    pub timestamp: DateTime<Utc>,
    pub source_id: String,
    pub correlation_id: String,
    pub instrument: String,
    pub event_type: String,
    pub last_price: Option<f64>,
    pub bid: Option<f64>,
    pub ask: Option<f64>,
    pub last_size: Option<u64>,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct TradeSignal {
    pub message_type: String,
    pub version: String,
    pub timestamp: DateTime<Utc>,
    pub source_id: String,
    pub correlation_id: String,
    pub signal_id: String,
    pub strategy_id: String,
    pub account: String,
    pub instrument: String,
    pub side: String,
    pub quantity: u32,
    pub order_type: String,
    pub reason: String,
}
