//! Event blackout calendar: session open/close windows + news event windows.
//!
//! All times are UTC.  Session events are daily recurring; news events are
//! one-shot timestamps loaded from an external calendar feed.
//!
//! The fail-safe policy is: when `EventCalendar::feed_healthy` is `false`,
//! the caller (main loop) should treat all entry signals as blocked.

use chrono::{DateTime, TimeZone, Utc};

// ── Domain types ──────────────────────────────────────────────────────────────

/// A daily-recurring session boundary event (open or close of a major session).
#[derive(Debug, Clone)]
pub struct SessionEvent {
    /// Human-readable label, e.g. `"london-open"`.
    pub label: &'static str,
    /// Hour in UTC (0–23).
    pub hour: u32,
    /// Minute in UTC (0–59).
    pub minute: u32,
}

/// A one-shot high-impact news event from an external calendar feed.
#[derive(Debug, Clone)]
pub struct NewsEvent {
    /// Title from the calendar feed (e.g. `"Non-Farm Employment Change"`).
    pub title: String,
    /// Scheduled release time in UTC.
    pub timestamp_utc: DateTime<Utc>,
}

// ── Session defaults ──────────────────────────────────────────────────────────

/// Default UTC session open/close events:
/// - Asia   open 00:00, close 09:00
/// - London open 08:00, close 16:30
/// - New York open 13:30, close 20:00
pub fn default_session_events() -> Vec<SessionEvent> {
    vec![
        SessionEvent { label: "asia-open",    hour: 0,  minute: 0  },
        SessionEvent { label: "asia-close",   hour: 9,  minute: 0  },
        SessionEvent { label: "london-open",  hour: 8,  minute: 0  },
        SessionEvent { label: "london-close", hour: 16, minute: 30 },
        SessionEvent { label: "ny-open",      hour: 13, minute: 30 },
        SessionEvent { label: "ny-close",     hour: 20, minute: 0  },
    ]
}

// ── Blackout predicates ───────────────────────────────────────────────────────

/// Returns `Some(label)` if `now` falls within `±radius_mins` of any session event.
pub fn in_session_blackout<'a>(
    events: &'a [SessionEvent],
    now: DateTime<Utc>,
    radius_mins: i64,
) -> Option<&'a str> {
    let radius = chrono::Duration::minutes(radius_mins);
    for event in events {
        let Some(naive) = now.date_naive().and_hms_opt(event.hour, event.minute, 0) else {
            continue;
        };
        let event_dt = Utc.from_utc_datetime(&naive);
        if now >= event_dt - radius && now <= event_dt + radius {
            return Some(event.label);
        }
    }
    None
}

/// Returns `Some(title)` if `now` falls within `±radius_mins` of any news event.
pub fn in_news_blackout(
    events: &[NewsEvent],
    now: DateTime<Utc>,
    radius_mins: i64,
) -> Option<String> {
    let radius = chrono::Duration::minutes(radius_mins);
    for event in events {
        if now >= event.timestamp_utc - radius && now <= event.timestamp_utc + radius {
            return Some(event.title.clone());
        }
    }
    None
}

// ── Calendar state ────────────────────────────────────────────────────────────

/// Owns session and news events and tracks the health of the external news feed.
#[derive(Debug)]
pub struct EventCalendar {
    /// Static daily-recurring session events.
    pub session_events: Vec<SessionEvent>,
    /// One-shot news events refreshed from the external feed.
    pub news_events: Vec<NewsEvent>,
    /// `false` when the last feed fetch failed or the feed has gone stale.
    /// Callers should treat a stale feed as a permanent blackout (fail-safe).
    pub feed_healthy: bool,
    last_successful_fetch: Option<std::time::Instant>,
}

impl EventCalendar {
    pub fn new() -> Self {
        Self {
            session_events: default_session_events(),
            news_events: Vec::new(),
            feed_healthy: true, // optimistic until first failure
            last_successful_fetch: None,
        }
    }

    /// Replace the news event list with a fresh batch and mark the feed healthy.
    pub fn apply_news(&mut self, events: Vec<NewsEvent>) {
        self.news_events = events;
        self.last_successful_fetch = Some(std::time::Instant::now());
        self.feed_healthy = true;
    }

    /// Record a feed fetch failure and mark the feed unhealthy.
    pub fn mark_feed_failed(&mut self) {
        self.feed_healthy = false;
    }

    /// Mark the feed stale if the last successful fetch is older than `stale_secs`.
    pub fn check_staleness(&mut self, stale_secs: u64) {
        if let Some(last) = self.last_successful_fetch {
            if last.elapsed().as_secs() > stale_secs {
                self.feed_healthy = false;
            }
        }
    }

    /// Returns `Some(reason)` if `now` is inside any blackout window, otherwise `None`.
    ///
    /// Session events are checked first; news events second.
    pub fn in_any_blackout(&self, now: DateTime<Utc>, radius_mins: i64) -> Option<String> {
        if let Some(label) = in_session_blackout(&self.session_events, now, radius_mins) {
            return Some(format!("session:{}", label));
        }
        if let Some(title) = in_news_blackout(&self.news_events, now, radius_mins) {
            return Some(format!("news:{}", title));
        }
        None
    }
}

impl Default for EventCalendar {
    fn default() -> Self {
        Self::new()
    }
}

// ── tests ─────────────────────────────────────────────────────────────────────

#[cfg(test)]
mod tests {
    use super::*;

    fn utc(h: u32, m: u32) -> DateTime<Utc> {
        Utc.with_ymd_and_hms(2026, 3, 16, h, m, 0).unwrap()
    }

    // ── Session blackout ──────────────────────────────────────────────────────

    #[test]
    fn session_blackout_inside_window() {
        let events = default_session_events();
        // london-open at 08:00 ±3min → window [07:57, 08:03]
        assert_eq!(in_session_blackout(&events, utc(8, 1), 3), Some("london-open"));
    }

    #[test]
    fn session_blackout_outside_window() {
        let events = default_session_events();
        assert!(in_session_blackout(&events, utc(10, 0), 3).is_none());
    }

    #[test]
    fn session_blackout_at_event_time() {
        let events = default_session_events();
        assert_eq!(in_session_blackout(&events, utc(13, 30), 3), Some("ny-open"));
    }

    #[test]
    fn session_blackout_at_lower_boundary() {
        let events = default_session_events();
        // exactly −3 min from ny-open
        assert_eq!(in_session_blackout(&events, utc(13, 27), 3), Some("ny-open"));
    }

    #[test]
    fn session_blackout_at_upper_boundary() {
        let events = default_session_events();
        // exactly +3 min from ny-open
        assert_eq!(in_session_blackout(&events, utc(13, 33), 3), Some("ny-open"));
    }

    #[test]
    fn session_blackout_one_minute_past_upper_boundary() {
        let events = default_session_events();
        // +4 min from ny-open — outside window
        assert!(in_session_blackout(&events, utc(13, 34), 3).is_none());
    }

    #[test]
    fn session_blackout_asia_open_at_midnight() {
        let events = default_session_events();
        assert_eq!(in_session_blackout(&events, utc(0, 2), 3), Some("asia-open"));
    }

    #[test]
    fn session_blackout_london_close() {
        let events = default_session_events();
        assert_eq!(in_session_blackout(&events, utc(16, 29), 3), Some("london-close"));
    }

    // ── News blackout ─────────────────────────────────────────────────────────

    #[test]
    fn news_blackout_inside_window() {
        let event_time = Utc.with_ymd_and_hms(2026, 3, 6, 13, 30, 0).unwrap();
        let events = vec![NewsEvent {
            title: "Non-Farm Employment Change".to_string(),
            timestamp_utc: event_time,
        }];
        let during = Utc.with_ymd_and_hms(2026, 3, 6, 13, 32, 0).unwrap();
        assert!(in_news_blackout(&events, during, 3).is_some());
    }

    #[test]
    fn news_blackout_outside_window() {
        let event_time = Utc.with_ymd_and_hms(2026, 3, 6, 13, 30, 0).unwrap();
        let events = vec![NewsEvent {
            title: "Non-Farm Employment Change".to_string(),
            timestamp_utc: event_time,
        }];
        let outside = Utc.with_ymd_and_hms(2026, 3, 6, 14, 0, 0).unwrap();
        assert!(in_news_blackout(&events, outside, 3).is_none());
    }

    #[test]
    fn news_blackout_before_event() {
        let event_time = Utc.with_ymd_and_hms(2026, 3, 6, 14, 0, 0).unwrap();
        let events = vec![NewsEvent { title: "CPI".to_string(), timestamp_utc: event_time }];
        let two_min_before = Utc.with_ymd_and_hms(2026, 3, 6, 13, 58, 0).unwrap();
        assert!(in_news_blackout(&events, two_min_before, 3).is_some());
    }

    // ── Calendar state ────────────────────────────────────────────────────────

    #[test]
    fn calendar_new_is_healthy() {
        assert!(EventCalendar::new().feed_healthy);
    }

    #[test]
    fn calendar_mark_failed_sets_unhealthy() {
        let mut cal = EventCalendar::new();
        cal.mark_feed_failed();
        assert!(!cal.feed_healthy);
    }

    #[test]
    fn calendar_apply_news_restores_health() {
        let mut cal = EventCalendar::new();
        cal.mark_feed_failed();
        cal.apply_news(vec![]);
        assert!(cal.feed_healthy);
    }

    #[test]
    fn calendar_in_any_blackout_session() {
        let cal = EventCalendar::new();
        let result = cal.in_any_blackout(utc(8, 0), 3);
        assert_eq!(result, Some("session:london-open".to_string()));
    }

    #[test]
    fn calendar_in_any_blackout_news() {
        let mut cal = EventCalendar::new();
        let nfp_time = Utc.with_ymd_and_hms(2026, 1, 6, 18, 30, 0).unwrap(); // 13:30 ET = 18:30 UTC
        cal.apply_news(vec![NewsEvent {
            title: "Non-Farm Employment Change".to_string(),
            timestamp_utc: nfp_time,
        }]);
        let during = Utc.with_ymd_and_hms(2026, 1, 6, 18, 31, 0).unwrap();
        let result = cal.in_any_blackout(during, 3);
        assert!(result.map(|s| s.starts_with("news:")).unwrap_or(false));
    }

    #[test]
    fn calendar_clear_mid_session() {
        let cal = EventCalendar::new();
        // 11:00 UTC is not near any default session event
        assert!(cal.in_any_blackout(utc(11, 0), 3).is_none());
    }
}
