use anyhow::{Context, Result};
use tokio::io::AsyncWriteExt;
use tokio::net::TcpStream;

use crate::models::TradeSignal;

pub async fn send_signal(signal_bind: &str, signal: &TradeSignal) -> Result<()> {
    let mut stream = TcpStream::connect(signal_bind)
        .await
        .with_context(|| format!("failed to connect signal endpoint {signal_bind}"))?;

    let line = serde_json::to_string(signal).context("failed to serialize TradeSignal")?;
    stream
        .write_all(format!("{line}\n").as_bytes())
        .await
        .context("failed writing signal payload")?;

    stream.flush().await.context("failed flushing signal payload")?;
    Ok(())
}
