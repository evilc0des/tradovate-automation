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
}

impl StrategyArg {
    /// Canonical lowercase string used internally by `AppConfig` / factory.
    pub fn as_config_str(&self) -> &str {
        match self {
            StrategyArg::Deterministic => "deterministic",
            StrategyArg::EmaMomentum => "ema-momentum",
            StrategyArg::HeikinAshi => "heikin-ashi",
        }
    }
}
