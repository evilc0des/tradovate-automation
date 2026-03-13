using System;
using NinjaTraderTradovateBridge;

namespace NinjaTraderBridge.UnitTests;

/// <summary>
/// Shared test helpers: null logger, default config, and signal factories.
/// </summary>
internal static class Helpers
{
    public static IBridgeLogger NullLogger { get; } = new NullBridgeLogger();

    public static BridgeConfig DefaultConfig(
        bool liveEnabled = false,
        bool disarmOnStartup = false,
        string account = "SIM101",
        string[]? instruments = null,
        string[]? sources = null,
        int maxQty = 1,
        int maxAgeMs = 5000,
        string sessionStart = "00:00",
        string sessionEnd = "23:59")
    =>
        new BridgeConfig
        {
            LiveTradingEnabled = liveEnabled,
            DisarmOnStartup = disarmOnStartup,
            AllowedAccount = account,
            AllowedInstruments = instruments ?? new[] { "MES 06-26" },
            AllowedSignalSources = sources ?? new[] { "rust.strategy" },
            MaxOrderQuantity = maxQty,
            MaxSignalAgeMs = maxAgeMs,
            SessionStartUtc = sessionStart,
            SessionEndUtc = sessionEnd,
        };

    public static TradeSignal ValidSignal(
        string? signalId = null,
        string instrument = "MES 06-26",
        string side = "Buy",
        string account = "SIM101",
        string source = "rust.strategy",
        int quantity = 1,
        DateTimeOffset? timestamp = null)
    =>
        new TradeSignal
        {
            MessageType = "TradeSignal",
            Version = "v1",
            Timestamp = timestamp ?? DateTimeOffset.UtcNow,
            SourceId = source,
            CorrelationId = Guid.NewGuid().ToString("N"),
            SignalId = signalId ?? Guid.NewGuid().ToString("N"),
            StrategyId = "test-strategy",
            Account = account,
            Instrument = instrument,
            Side = side,
            Quantity = quantity,
            OrderType = "Market",
            Reason = "unit test",
        };
}

internal sealed class NullBridgeLogger : IBridgeLogger
{
    public void Info(string message) { }
    public void Warn(string message) { }
    public void Error(string message, Exception? exception = null) { }
}

/// <summary>
/// Order gateway that always accepts orders with a predictable ID.
/// </summary>
internal sealed class AlwaysAcceptGateway : IOrderSubmissionGateway
{
    public string LastOrderId { get; private set; } = string.Empty;

    public OrderSubmissionResult SubmitMarketOrder(TradeSignal signal)
    {
        LastOrderId = $"TST-{signal.SignalId[..8]}";
        return new OrderSubmissionResult
        {
            Accepted = true,
            OrderId = LastOrderId,
            Detail = "accepted by test gateway",
            SignalIdTag = signal.SignalId,
            CorrelationIdTag = signal.CorrelationId,
        };
    }
}

/// <summary>
/// Order gateway that always rejects orders.
/// </summary>
internal sealed class AlwaysRejectGateway : IOrderSubmissionGateway
{
    public OrderSubmissionResult SubmitMarketOrder(TradeSignal signal) =>
        new OrderSubmissionResult
        {
            Accepted = false,
            OrderId = string.Empty,
            Detail = "rejected by test gateway",
            SignalIdTag = signal.SignalId,
            CorrelationIdTag = signal.CorrelationId,
        };
}
