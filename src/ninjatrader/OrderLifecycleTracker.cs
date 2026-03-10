using System;

namespace NinjaTraderTradovateBridge;

public sealed class OrderLifecycleTracker
{
    private readonly ExecutionJournal _journal;
    private readonly ActualStateSnapshotStore _actualState;
    private readonly IBridgeLogger _logger;

    public OrderLifecycleTracker(ExecutionJournal journal, ActualStateSnapshotStore actualState, IBridgeLogger logger)
    {
        _journal = journal;
        _actualState = actualState;
        _logger = logger;
    }

    public void TrackAccepted(TradeSignal signal, OrderSubmissionResult submission)
    {
        Track(signal, submission.OrderId, "Accepted", signal.Quantity, submission.Detail);
    }

    public void TrackRejected(TradeSignal signal, string detail)
    {
        Track(signal, string.Empty, "Rejected", 0, detail);
    }

    public void TrackPartialFill(TradeSignal signal, string orderId, int filledQuantity, string detail)
    {
        Track(signal, orderId, "PartialFill", filledQuantity, detail);
    }

    public void TrackFullFill(TradeSignal signal, string orderId, int filledQuantity, string detail)
    {
        Track(signal, orderId, "FullFill", filledQuantity, detail);
    }

    public void TrackCanceled(TradeSignal signal, string orderId, int filledQuantity, string detail)
    {
        Track(signal, orderId, "Canceled", filledQuantity, detail);
    }

    public void TrackExecutionAmbiguity(TradeSignal signal, string orderId, string detail)
    {
        Track(signal, orderId, "Ambiguous", 0, detail);
    }

    private void Track(TradeSignal signal, string orderId, string status, int filledQuantity, string detail)
    {
        _journal.Append(new ExecutionJournalEntry
        {
            EventType = status,
            SignalId = signal.SignalId,
            CorrelationId = signal.CorrelationId,
            OrderId = orderId,
            Instrument = signal.Instrument,
            Side = signal.Side,
            Quantity = signal.Quantity,
            FilledQuantity = filledQuantity,
            Detail = detail,
        });

        if (!string.IsNullOrWhiteSpace(orderId))
        {
            _actualState.UpsertOrder(new OrderStateSnapshot
            {
                OrderId = orderId,
                SignalId = signal.SignalId,
                CorrelationId = signal.CorrelationId,
                Instrument = signal.Instrument,
                Side = signal.Side,
                Quantity = signal.Quantity,
                FilledQuantity = filledQuantity,
                Status = status,
                Detail = detail,
                UpdatedUtc = DateTimeOffset.UtcNow,
            });
        }

        _logger.Info($"Lifecycle event status={status} signalId={signal.SignalId} orderId={orderId} filled={filledQuantity}");
    }
}
