use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct InboundEnvelope {
    pub message_type: String,
    pub version: String,
}

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
    /// Bar candle fields — populated only for `BarUpdate` events.
    #[serde(default)]
    pub bar_open: Option<f64>,
    #[serde(default)]
    pub bar_high: Option<f64>,
    #[serde(default)]
    pub bar_low: Option<f64>,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct QuoteUpdateMessage {
    pub version: String,
    pub timestamp: DateTime<Utc>,
    pub source_id: String,
    pub correlation_id: String,
    pub instrument: String,
    pub bid: f64,
    pub ask: f64,
    /// Best bid size from the L1 snapshot; absent in older bridge versions.
    #[serde(default)]
    pub bid_size: Option<i32>,
    /// Best ask size from the L1 snapshot; absent in older bridge versions.
    #[serde(default)]
    pub ask_size: Option<i32>,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct TradePrintMessage {
    pub version: String,
    pub timestamp: DateTime<Utc>,
    pub source_id: String,
    pub correlation_id: String,
    pub instrument: String,
    pub price: f64,
    pub size: u64,
    /// Which side initiated the trade: `"Buy"`, `"Sell"`, or `"Unknown"`.
    /// Absent in older bridge versions — defaults to `None`.
    #[serde(default)]
    pub aggressor_side: Option<String>,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct BarUpdateMessage {
    pub version: String,
    pub timestamp: DateTime<Utc>,
    pub source_id: String,
    pub correlation_id: String,
    pub instrument: String,
    #[allow(dead_code)]
    pub bar_time: DateTime<Utc>,
    pub interval: Option<String>,
    pub open: f64,
    pub high: f64,
    pub low: f64,
    pub close: f64,
    #[allow(dead_code)]
    pub volume: u64,
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
