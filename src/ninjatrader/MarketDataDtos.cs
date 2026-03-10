using System;
using System.Text.Json.Serialization;

namespace NinjaTraderTradovateBridge;

public abstract class MessageEnvelope
{
    [JsonPropertyName("messageType")]
    public string MessageType { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; init; } = "v1";

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("sourceId")]
    public string SourceId { get; init; } = "ninjatrader.bridge";

    [JsonPropertyName("correlationId")]
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString("N");
}

public sealed class MarketDataMessage : MessageEnvelope
{
    public MarketDataMessage()
    {
        MessageType = "MarketDataMessage";
    }

    [JsonPropertyName("instrument")]
    public string Instrument { get; init; } = string.Empty;

    [JsonPropertyName("eventType")]
    public string EventType { get; init; } = string.Empty;

    [JsonPropertyName("lastPrice")]
    public double? LastPrice { get; init; }

    [JsonPropertyName("bid")]
    public double? Bid { get; init; }

    [JsonPropertyName("ask")]
    public double? Ask { get; init; }

    [JsonPropertyName("lastSize")]
    public long? LastSize { get; init; }
}

public sealed class QuoteUpdateMessage : MessageEnvelope
{
    public QuoteUpdateMessage()
    {
        MessageType = "QuoteUpdateMessage";
    }

    [JsonPropertyName("instrument")]
    public string Instrument { get; init; } = string.Empty;

    [JsonPropertyName("bid")]
    public double Bid { get; init; }

    [JsonPropertyName("ask")]
    public double Ask { get; init; }

    [JsonPropertyName("bidSize")]
    public int BidSize { get; init; }

    [JsonPropertyName("askSize")]
    public int AskSize { get; init; }
}

public sealed class TradePrintMessage : MessageEnvelope
{
    public TradePrintMessage()
    {
        MessageType = "TradePrintMessage";
    }

    [JsonPropertyName("instrument")]
    public string Instrument { get; init; } = string.Empty;

    [JsonPropertyName("price")]
    public double Price { get; init; }

    [JsonPropertyName("size")]
    public int Size { get; init; }

    [JsonPropertyName("aggressorSide")]
    public string AggressorSide { get; init; } = "Unknown";
}

public sealed class BarUpdateMessage : MessageEnvelope
{
    public BarUpdateMessage()
    {
        MessageType = "BarUpdateMessage";
    }

    [JsonPropertyName("instrument")]
    public string Instrument { get; init; } = string.Empty;

    [JsonPropertyName("barTime")]
    public DateTimeOffset BarTime { get; init; }

    [JsonPropertyName("interval")]
    public string Interval { get; init; } = "1m";

    [JsonPropertyName("open")]
    public double Open { get; init; }

    [JsonPropertyName("high")]
    public double High { get; init; }

    [JsonPropertyName("low")]
    public double Low { get; init; }

    [JsonPropertyName("close")]
    public double Close { get; init; }

    [JsonPropertyName("volume")]
    public long Volume { get; init; }
}

public sealed class ConnectionStateMessage : MessageEnvelope
{
    public ConnectionStateMessage()
    {
        MessageType = "ConnectionStateMessage";
    }

    [JsonPropertyName("provider")]
    public string Provider { get; init; } = "Tradovate";

    [JsonPropertyName("state")]
    public string State { get; init; } = "Unknown";

    [JsonPropertyName("details")]
    public string? Details { get; init; }
}

public sealed class InstrumentSessionMetadataMessage : MessageEnvelope
{
    public InstrumentSessionMetadataMessage()
    {
        MessageType = "InstrumentSessionMetadataMessage";
    }

    [JsonPropertyName("instrument")]
    public string Instrument { get; init; } = string.Empty;

    [JsonPropertyName("sessionId")]
    public string SessionId { get; init; } = string.Empty;

    [JsonPropertyName("sessionStart")]
    public DateTimeOffset SessionStart { get; init; }

    [JsonPropertyName("sessionEnd")]
    public DateTimeOffset SessionEnd { get; init; }

    [JsonPropertyName("tickSize")]
    public double TickSize { get; init; }

    [JsonPropertyName("pointValue")]
    public double PointValue { get; init; }
}

public sealed class HeartbeatMessage : MessageEnvelope
{
    public HeartbeatMessage()
    {
        MessageType = "HeartbeatMessage";
    }

    [JsonPropertyName("channel")]
    public string Channel { get; init; } = "MarketData";

    [JsonPropertyName("status")]
    public string Status { get; init; } = "Alive";
}
