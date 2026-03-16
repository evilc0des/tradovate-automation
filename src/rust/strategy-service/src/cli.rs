use clap::{Parser, ValueEnum};

/// Tradovate strategy service — connects to NinjaTrader market data and
/// emits trade signals via the configured strategy.
///
/// Every flag can also be set through the corresponding environment variable
/// shown in brackets.  CLI flags take priority over env vars.
#[derive(Debug, Parser)]
#[command(name = "strategy-service", version, about, long_about = None)]
pub struct Cli {
    /// Strategy to run.
    /// [env: STRATEGY]
    #[arg(
        short,
        long,
        value_enum,
        env = "STRATEGY",
        default_value = "deterministic"
    )]
    pub strategy: StrategyArg,

    /// Address to listen on for incoming NinjaTrader market data (host:port).
    /// [env: MARKET_DATA_BIND]
    #[arg(long, env = "MARKET_DATA_BIND", default_value = "127.0.0.1:9100")]
    pub market_data_bind: String,

    /// Address of the NinjaTrader signal intake endpoint (host:port).
    /// [env: SIGNAL_BIND]
    #[arg(long, env = "SIGNAL_BIND", default_value = "127.0.0.1:9101")]
    pub signal_bind: String,

    /// Tradovate / NinjaTrader account name that orders will be tagged with.
    /// [env: ALLOWED_ACCOUNT]
    #[arg(long, env = "ALLOWED_ACCOUNT", default_value = "SIM101")]
    pub account: String,

    /// Comma-separated list of instrument symbols the strategy may trade.
    /// [env: ALLOWED_INSTRUMENTS]
    #[arg(
        long,
        env = "ALLOWED_INSTRUMENTS",
        default_value = "MES 06-26",
        value_delimiter = ','
    )]
    pub instruments: Vec<String>,

    /// Minimum milliseconds between consecutive signals for the same instrument.
    /// [env: COOLDOWN_MS]
    #[arg(long, env = "COOLDOWN_MS", default_value = "2000")]
    pub cooldown_ms: u64,

    /// Emit exactly one signal immediately on the first market-data tick,
    /// regardless of strategy rules.  Useful for connectivity testing.
    /// [env: FORCE_TRADE_ONCE]
    #[arg(long, env = "FORCE_TRADE_ONCE", default_value = "false")]
    pub force_trade_once: bool,

    /// Side used with --force-trade-once.  Must be Buy or Sell.
    /// [env: FORCE_TRADE_SIDE]
    #[arg(long, env = "FORCE_TRADE_SIDE", default_value = "Buy")]
    pub force_trade_side: String,

    // ── Tape-burst scalper parameters ─────────────────────────────────────────

    /// Tick size for the trading instrument in price units (e.g. 0.25 for
    /// NQ / MNQ).  Used to convert price moves to tick counts for target and
    /// stop calculations.
    /// [env: TAPE_TICK_SIZE]
    #[arg(long, env = "TAPE_TICK_SIZE", default_value = "0.25")]
    pub tape_tick_size: f64,

    /// Minimum signed volume imbalance (buy − sell contracts) in the
    /// 2-second tape window required to recognise a burst.
    /// [env: TAPE_MICRO_DELTA_MIN]
    #[arg(long, env = "TAPE_MICRO_DELTA_MIN", default_value = "40")]
    pub tape_micro_delta_min: i64,

    /// Minimum aggressor-volume ratio (buy/sell for longs; sell/buy for shorts)
    /// over the 2-second window.
    /// [env: TAPE_AGGRESSION_RATIO_MIN]
    #[arg(long, env = "TAPE_AGGRESSION_RATIO_MIN", default_value = "1.8")]
    pub tape_aggression_ratio_min: f64,

    /// Tape-speed multiplier: 2-second prints-per-second must exceed
    /// the 5-second baseline by at least this factor.
    /// [env: TAPE_SPEED_FACTOR_MIN]
    #[arg(long, env = "TAPE_SPEED_FACTOR_MIN", default_value = "1.5")]
    pub tape_speed_factor_min: f64,

    /// Minimum price-response ratio in ticks per 10 aggressive contracts.
    /// Guards against aggression-without-movement (absorption) scenarios.
    /// [env: TAPE_PRICE_RESPONSE_MIN_TICKS]
    #[arg(long, env = "TAPE_PRICE_RESPONSE_MIN_TICKS", default_value = "0.5")]
    pub tape_price_response_min_ticks: f64,

    /// Profit target in ticks for an open tape-burst position.
    /// [env: TAPE_TARGET_TICKS]
    #[arg(long, env = "TAPE_TARGET_TICKS", default_value = "2.0")]
    pub tape_target_ticks: f64,

    /// Hard stop size in ticks for an open tape-burst position.
    /// [env: TAPE_STOP_TICKS]
    #[arg(long, env = "TAPE_STOP_TICKS", default_value = "2.0")]
    pub tape_stop_ticks: f64,

    /// Maximum hold time in milliseconds before the time-stop exit fires.
    /// [env: TAPE_TIME_STOP_MS]
    #[arg(long, env = "TAPE_TIME_STOP_MS", default_value = "8000")]
    pub tape_time_stop_ms: u64,

    /// Absolute micro-delta magnitude in the 1-second window that triggers
    /// a flow-failure exit when the delta flips against the open position.
    /// [env: TAPE_FLIP_DELTA]
    #[arg(long, env = "TAPE_FLIP_DELTA", default_value = "20")]
    pub tape_flip_delta: i64,

    /// Start of the allowed UTC trading session (HH:MM).  Entry signals are
    /// suppressed outside the [start, end] window.  Default "00:00" = no filter.
    /// [env: TAPE_SESSION_START_UTC]
    #[arg(long, env = "TAPE_SESSION_START_UTC", default_value = "00:00")]
    pub tape_session_start_utc: String,

    /// End of the allowed UTC trading session (HH:MM).  Default "23:59" = no filter.
    /// [env: TAPE_SESSION_END_UTC]
    #[arg(long, env = "TAPE_SESSION_END_UTC", default_value = "23:59")]
    pub tape_session_end_utc: String,

    /// L1 best-ask (or best-bid) size threshold used as a lightweight near-wall
    /// proxy.  Entry is suppressed when the near-touch size >= this value.
    /// Set very high (default 1000) to disable.
    /// [env: TAPE_WALL_MIN_SIZE]
    #[arg(long, env = "TAPE_WALL_MIN_SIZE", default_value = "1000")]
    pub tape_wall_min_size: u32,

    // ── Event blackout ────────────────────────────────────────────────────────

    /// Minutes before and after each session/news event where new entries are
    /// suppressed.  Exit and flatten signals are never blocked.  Default 3.
    /// [env: EVENT_BLACKOUT_RADIUS_MINS]
    #[arg(long, env = "EVENT_BLACKOUT_RADIUS_MINS", default_value = "3")]
    pub event_blackout_radius_mins: i64,

    /// Enable the external news calendar feed.  When false only the built-in
    /// session events (Asia/London/NY open+close) impose blackout windows.
    /// [env: NEWS_ENABLED]
    #[arg(long, env = "NEWS_ENABLED", default_value = "false")]
    pub news_enabled: bool,

    /// URL for the weekly high-impact news calendar JSON feed.
    /// [env: NEWS_API_URL]
    #[arg(long, env = "NEWS_API_URL",
          default_value = "https://nfs.faireconomy.media/ff_calendar_thisweek.json")]
    pub news_api_url: String,

    /// How often (seconds) to refresh the news calendar from the API.
    /// [env: NEWS_API_POLL_SECS]
    #[arg(long, env = "NEWS_API_POLL_SECS", default_value = "3600")]
    pub news_api_poll_secs: u64,

    /// Seconds since last successful fetch before the feed is considered stale.
    /// When stale the fail-safe policy blocks all new entries.
    /// [env: NEWS_API_STALE_SECS]
    #[arg(long, env = "NEWS_API_STALE_SECS", default_value = "86400")]
    pub news_api_stale_secs: u64,
}

/// Available strategy names.
#[derive(Debug, Clone, ValueEnum)]
pub enum StrategyArg {
    /// Original spread-threshold rule; good for connectivity tests.
    Deterministic,
    /// 5 / 20 EMA crossover trend-follow strategy.
    EmaMomentum,
    /// Heikin Ashi reversal strategy — enters on two consecutive same-colour candles.
    HeikinAshi,
    /// Event-driven tape burst scalper — enters on aggressive order-flow bursts
    /// with confirmed price response; exits on fixed target/stop, flow-failure,
    /// or time-stop.
    TapeBurstScalper,
}

impl StrategyArg {
    /// Canonical lowercase string used internally by `AppConfig` / factory.
    pub fn as_config_str(&self) -> &str {
        match self {
            StrategyArg::Deterministic => "deterministic",
            StrategyArg::EmaMomentum => "ema-momentum",
            StrategyArg::HeikinAshi => "heikin-ashi",
            StrategyArg::TapeBurstScalper => "tape-burst-scalper",
        }
    }
}
