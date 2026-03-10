using System;
using System.Text.Json.Serialization;

namespace NinjaTraderTradovateBridge;

public sealed class TradeSignal
{
    [JsonPropertyName("messageType")]
    public string MessageType { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [JsonPropertyName("sourceId")]
    public string SourceId { get; set; } = string.Empty;

    [JsonPropertyName("correlationId")]
    public string CorrelationId { get; set; } = string.Empty;

    [JsonPropertyName("signalId")]
    public string SignalId { get; set; } = string.Empty;

    [JsonPropertyName("strategyId")]
    public string StrategyId { get; set; } = string.Empty;

    [JsonPropertyName("account")]
    public string Account { get; set; } = string.Empty;

    [JsonPropertyName("instrument")]
    public string Instrument { get; set; } = string.Empty;

    [JsonPropertyName("side")]
    public string Side { get; set; } = string.Empty;

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }

    [JsonPropertyName("orderType")]
    public string OrderType { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}

public sealed class SignalAck
{
    [JsonPropertyName("messageType")]
    public string MessageType { get; set; } = "SignalAck";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "v1";

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("sourceId")]
    public string SourceId { get; set; } = "ninjatrader.bridge";

    [JsonPropertyName("correlationId")]
    public string CorrelationId { get; set; } = string.Empty;

    [JsonPropertyName("signalId")]
    public string SignalId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("detail")]
    public string Detail { get; set; } = string.Empty;
}

public sealed class ErrorEnvelope
{
    [JsonPropertyName("messageType")]
    public string MessageType { get; set; } = "ErrorEnvelope";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "v1";

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("sourceId")]
    public string SourceId { get; set; } = "ninjatrader.bridge";

    [JsonPropertyName("correlationId")]
    public string CorrelationId { get; set; } = string.Empty;

    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("severity")]
    public string Severity { get; set; } = "Error";

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("details")]
    public string? Details { get; set; }

    [JsonPropertyName("retryable")]
    public bool Retryable { get; set; }
}
