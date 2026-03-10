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

    public string AllowedAccount { get; init; } = "SIM101";
    public string[] AllowedInstruments { get; init; } = ["MES 06-26"];
    public string[] AllowedSignalSources { get; init; } = ["rust.strategy"];
    public int SignalReadTimeoutMs { get; init; } = 15000;
    public string ProcessedSignalStorePath { get; init; } = "state/processed-signal-ids.txt";
    public string SafetyStatePath { get; init; } = "state/safety-state.json";
    public string ExecutionJournalPath { get; init; } = "state/execution-journal.ndjson";
    public string ActualStateSnapshotPath { get; init; } = "state/actual-state.json";
}
