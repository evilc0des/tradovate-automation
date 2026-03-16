//! Forex Factory weekly calendar adapter.
//!
//! Fetches the FF weekly JSON endpoint, filters for USD High-impact events,
//! and returns `Vec<NewsEvent>`.  Returns an error on any network or parse
//! failure; callers should mark the feed stale and apply the fail-safe policy.

use anyhow::Result;
use chrono::{DateTime, Utc};
use serde::Deserialize;

use crate::events::NewsEvent;

// ── Forex Factory payload shape ───────────────────────────────────────────────

/// Raw event field layout from the FF weekly JSON endpoint.
#[derive(Debug, Deserialize)]
struct FfCalendarEvent {
    title: String,
    country: String,
    /// RFC 3339 with UTC offset, e.g. `"2026-03-06T13:30:00-05:00"`.
    date: String,
    /// `"High"` | `"Medium"` | `"Low"` | `"Holiday"`.
    impact: String,
}

// ── Public API ────────────────────────────────────────────────────────────────

/// Fetch and parse news events from `url`.
///
/// Only USD, High-impact events are returned.  The `date` field is parsed as
/// RFC 3339 (including the timezone offset published by FF) and converted to UTC.
pub async fn fetch_news_events(url: &str) -> Result<Vec<NewsEvent>> {
    let client = reqwest::Client::builder()
        .timeout(std::time::Duration::from_secs(15))
        .build()?;

    let raw: Vec<FfCalendarEvent> = client
        .get(url)
        .header("User-Agent", "tradovate-automation/1.0")
        .send()
        .await?
        .error_for_status()?
        .json()
        .await?;

    let events = raw
        .into_iter()
        .filter(|e| e.country == "USD" && e.impact == "High")
        .filter_map(|e| {
            DateTime::parse_from_rfc3339(&e.date)
                .ok()
                .map(|dt| NewsEvent {
                    title: e.title,
                    timestamp_utc: dt.with_timezone(&Utc),
                })
        })
        .collect();

    Ok(events)
}

// ── tests ─────────────────────────────────────────────────────────────────────

#[cfg(test)]
mod tests {
    use super::*;

    /// Validates filter logic against a realistic FF payload fixture.
    #[test]
    fn filters_usd_high_only() {
        let raw: Vec<FfCalendarEvent> = serde_json::from_str(FIXTURE).unwrap();
        let events: Vec<NewsEvent> = raw
            .into_iter()
            .filter(|e| e.country == "USD" && e.impact == "High")
            .filter_map(|e| {
                DateTime::parse_from_rfc3339(&e.date)
                    .ok()
                    .map(|dt| NewsEvent {
                        title: e.title,
                        timestamp_utc: dt.with_timezone(&Utc),
                    })
            })
            .collect();

        // NFP and CPI are USD High; Unemployment Rate is Medium; ECB is EUR
        assert_eq!(events.len(), 2);
        assert_eq!(events[0].title, "Non-Farm Employment Change");
        assert_eq!(events[1].title, "CPI m/m");
    }

    #[test]
    fn rfc3339_with_negative_offset_converts_to_utc() {
        // 13:30 ET (-05:00) == 18:30 UTC
        let raw: Vec<FfCalendarEvent> = serde_json::from_str(FIXTURE).unwrap();
        let nfp = raw
            .into_iter()
            .filter(|e| e.title == "Non-Farm Employment Change")
            .filter_map(|e| DateTime::parse_from_rfc3339(&e.date).ok())
            .next()
            .unwrap();
        let utc = nfp.with_timezone(&Utc);
        assert_eq!(utc.hour(), 18);
        assert_eq!(utc.minute(), 30);
    }

    use chrono::Timelike;

    const FIXTURE: &str = r#"[
        {"title":"Non-Farm Employment Change","country":"USD","date":"2026-03-06T13:30:00-05:00","impact":"High","forecast":"190K","previous":"143K"},
        {"title":"Unemployment Rate","country":"USD","date":"2026-03-06T13:30:00-05:00","impact":"Medium","forecast":"4.1%","previous":"4.0%"},
        {"title":"ECB Press Conference","country":"EUR","date":"2026-03-07T13:45:00+01:00","impact":"High","forecast":"","previous":""},
        {"title":"CPI m/m","country":"USD","date":"2026-03-12T12:30:00-04:00","impact":"High","forecast":"0.3%","previous":"0.2%"}
    ]"#;
}
