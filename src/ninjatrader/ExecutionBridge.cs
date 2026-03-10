using System;

namespace NinjaTraderTradovateBridge;

public sealed class ExecutionBridge
{
    private readonly BridgeConfig _config;
    private readonly SignalValidator _validator;
    private readonly DedupStore _dedupStore;
    private readonly RiskEngine _riskEngine;
    private readonly SafetyStateManager _safety;
    private readonly IOrderSubmissionGateway _orderGateway;
    private readonly IBridgeLogger _logger;
    private readonly OrderLifecycleTracker _lifecycleTracker;

    public string? LastOrderId { get; private set; }
    public TradeSignal? LastAcceptedSignal { get; private set; }

    public bool IsDisarmed => _safety.IsDisarmed;

    public ExecutionBridge(BridgeConfig config, IBridgeLogger? logger = null, IOrderSubmissionGateway? orderGateway = null)
    {
        _config = config;
        _logger = logger ?? new ConsoleBridgeLogger();
        _validator = new SignalValidator();
        _dedupStore = new DedupStore(config.ProcessedSignalStorePath, _logger);
        _riskEngine = new RiskEngine();
        _safety = new SafetyStateManager(config.SafetyStatePath, config.DisarmOnStartup, _logger);
        _orderGateway = orderGateway ?? new SimulatedOrderSubmissionGateway();
        var journal = new ExecutionJournal(config.ExecutionJournalPath, _logger);
        var actualState = new ActualStateSnapshotStore(config.ActualStateSnapshotPath, _logger);
        _lifecycleTracker = new OrderLifecycleTracker(journal, actualState, _logger);
    }

    public SignalAck HandleSignal(TradeSignal signal)
    {
        if (_safety.IsDisarmed)
        {
            _logger.Warn($"Signal rejected while disarmed signalId={signal.SignalId} reason={_safety.LastReason}");
            return Ack(signal, "Disarmed", "Bridge is currently disarmed.");
        }

        if (_dedupStore.IsDuplicate(signal.SignalId))
        {
            _lifecycleTracker.TrackRejected(signal, "Duplicate signalId.");
            return Ack(signal, "Rejected", "Duplicate signalId.");
        }

        if (!_validator.Validate(_config, signal, out var validationReason))
        {
            _lifecycleTracker.TrackRejected(signal, validationReason);
            return Ack(signal, "Rejected", validationReason);
        }

        var canSubmit = _config.LiveTradingEnabled
            ? _riskEngine.CanSubmit(_config, signal, out var riskReason)
            : _riskEngine.CanSubmitSimulation(_config, signal, out riskReason);

        if (!canSubmit)
        {
            _logger.Warn($"Risk rejected signalId={signal.SignalId} detail={riskReason}");
            _lifecycleTracker.TrackRejected(signal, riskReason);
            return Ack(signal, "Rejected", riskReason);
        }

        _dedupStore.MarkProcessed(signal.SignalId);

        var mode = _config.LiveTradingEnabled ? "Live" : "Simulation";
        _logger.Info($"Submitting order mode={mode} signalId={signal.SignalId} correlationId={signal.CorrelationId}");
        var submission = _orderGateway.SubmitMarketOrder(signal);

        if (!submission.Accepted)
        {
            _logger.Warn($"Order submission rejected signalId={signal.SignalId} detail={submission.Detail}");
            _lifecycleTracker.TrackRejected(signal, submission.Detail);
            return Ack(signal, "Rejected", submission.Detail);
        }

        _logger.Info(
            $"Order submission accepted orderId={submission.OrderId} signalIdTag={submission.SignalIdTag} correlationIdTag={submission.CorrelationIdTag}");

        LastOrderId = submission.OrderId;
        LastAcceptedSignal = signal;
        _lifecycleTracker.TrackAccepted(signal, submission);

        return Ack(signal, "Accepted", $"{submission.Detail} [{mode}]");
    }

    public void OnOrderPartiallyFilled(string orderId, int filledQuantity, string detail)
    {
        if (LastAcceptedSignal is null)
        {
            return;
        }

        _lifecycleTracker.TrackPartialFill(LastAcceptedSignal, orderId, filledQuantity, detail);
    }

    public void OnOrderFilled(string orderId, int filledQuantity, string detail)
    {
        if (LastAcceptedSignal is null)
        {
            return;
        }

        _lifecycleTracker.TrackFullFill(LastAcceptedSignal, orderId, filledQuantity, detail);
    }

    public void OnOrderCanceled(string orderId, int filledQuantity, string detail)
    {
        if (LastAcceptedSignal is null)
        {
            return;
        }

        _lifecycleTracker.TrackCanceled(LastAcceptedSignal, orderId, filledQuantity, detail);
    }

    public void OnExecutionAmbiguity(string orderId, string detail)
    {
        if (LastAcceptedSignal is null)
        {
            return;
        }

        _lifecycleTracker.TrackExecutionAmbiguity(LastAcceptedSignal, orderId, detail);
    }

    public void Arm()
    {
        _safety.Arm();
        _logger.Info("Bridge armed.");
    }

    public void Disarm(string reason)
    {
        _safety.Disarm(reason);
        _logger.Warn($"Bridge disarmed reason={reason}");
    }

    private static SignalAck Ack(TradeSignal signal, string status, string detail)
    {
        return new SignalAck
        {
            CorrelationId = signal.CorrelationId,
            SignalId = signal.SignalId,
            Status = status,
            Detail = detail,
            Timestamp = DateTimeOffset.UtcNow,
        };
    }
}
