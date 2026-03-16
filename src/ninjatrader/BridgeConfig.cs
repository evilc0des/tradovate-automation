namespace NinjaTraderTradovateBridge;

public sealed class BridgeConfig
{
    public string MarketDataHost { get; init; } = "127.0.0.1";
    public int MarketDataPort { get; init; } = 9100;
    public string SignalHost { get; init; } = "127.0.0.1";
    public int SignalPort { get; init; } = 9101;

    public bool LiveTradingEnabled { get; init; } = false;
    public bool DisarmOnStartup { get; init; } = true;
    public int MaxSignalAgeMs { get; init; } = 3000;
    public int MaxOrderQuantity { get; init; } = 1;
    public string SessionStartUtc { get; init; } = "00:00";
    public string SessionEndUtc { get; init; } = "23:59";

    /// <summary>
    /// Radius in minutes around each session/news event where new <em>entry</em>
    /// signals are blocked.  Exit and flatten instructions bypass this gate.
    /// Default 3 means ±3 minutes.
    /// </summary>
    public int EventBlackoutRadiusMinutes { get; init; } = 3;

    /// <summary>
    /// Comma-separated HH:MM UTC times for session open/close events.
    /// Defaults encode: Asia open 00:00 / close 09:00,
    /// London open 08:00 / close 16:30, New York open 13:30 / close 20:00.
    /// </summary>
    public string SessionEventTimesUtc { get; init; } = "00:00,09:00,08:00,16:30,13:30,20:00";

    /// <summary>
    /// Additional HH:MM UTC times for high-impact news events (comma-separated).
    /// The Rust strategy-service handles live feed ingestion; this field is for
    /// manual or offline operator configuration.
    /// </summary>
    public string NewsEventTimesUtc { get; init; } = "";

    public string AllowedAccount { get; init; } = "SIM101";
    public string[] AllowedInstruments { get; init; } = new[] { "MES 06-26" };
    public string[] AllowedSignalSources { get; init; } = new[] { "rust.strategy" };
    public int SignalReadTimeoutMs { get; init; } = 15000;
    public int MarketDataReconnectMaxAttempts { get; init; } = 5;
    public int MarketDataReconnectBackoffMs { get; init; } = 300;
    public int MarketDataQueueCapacity { get; init; } = 1024;
    public int HeartbeatIntervalSeconds { get; init; } = 2;
    public int MaxSignalFrameBytes { get; init; } = 64 * 1024;
    public string ProcessedSignalStorePath { get; init; } = "state/processed-signal-ids.txt";
    public string SafetyStatePath { get; init; } = "state/safety-state.json";
    public string ExecutionJournalPath { get; init; } = "state/execution-journal.ndjson";
    public string ActualStateSnapshotPath { get; init; } = "state/actual-state.json";
    public string ExpectedStateSnapshotPath { get; init; } = "state/expected-state.json";
    public string ReconciliationReportPath { get; init; } = "state/reconciliation-report.json";
    public string RuntimeMarkersPath { get; init; } = "state/runtime-markers.ndjson";
    public string AmbiguousSignalStorePath { get; init; } = "state/ambiguous-signals.txt";
    public string LogDirectory { get; init; } = "logs";
}
