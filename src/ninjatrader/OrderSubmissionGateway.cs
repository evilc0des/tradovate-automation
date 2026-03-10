using System;

namespace NinjaTraderTradovateBridge;

public interface IOrderSubmissionGateway
{
    OrderSubmissionResult SubmitMarketOrder(TradeSignal signal);
}

public sealed class SimulatedOrderSubmissionGateway : IOrderSubmissionGateway
{
    public OrderSubmissionResult SubmitMarketOrder(TradeSignal signal)
    {
        var orderId = $"SIM-{Guid.NewGuid():N}";
        return new OrderSubmissionResult
        {
            Accepted = true,
            OrderId = orderId,
            Detail = $"Simulated order accepted [{signal.Side} {signal.Quantity} {signal.Instrument}]",
            SignalIdTag = signal.SignalId,
            CorrelationIdTag = signal.CorrelationId,
        };
    }
}

public sealed class OrderSubmissionResult
{
    public bool Accepted { get; init; }
    public string OrderId { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string SignalIdTag { get; init; } = string.Empty;
    public string CorrelationIdTag { get; init; } = string.Empty;
}
