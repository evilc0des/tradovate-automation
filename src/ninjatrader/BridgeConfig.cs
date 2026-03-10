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

    public string AllowedAccount { get; init; } = "SIM101";
    public string[] AllowedInstruments { get; init; } = ["MES 06-26"];
    public string[] AllowedSignalSources { get; init; } = ["rust.strategy"];
    public int SignalReadTimeoutMs { get; init; } = 15000;
}
