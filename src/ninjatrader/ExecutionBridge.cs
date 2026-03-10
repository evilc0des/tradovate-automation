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
    private readonly ExpectedStateSnapshotStore _expectedState;
    private readonly ActualStateSnapshotStore _actualState;
    private readonly ReconciliationEngine _reconciliation;
    private readonly RuntimeMarkersStore _runtimeMarkers;

    public string? LastOrderId { get; private set; }
    public TradeSignal? LastAcceptedSignal { get; private set; }

    public bool IsDisarmed => _safety.IsDisarmed;

    public ExecutionBridge(BridgeConfig config, IBridgeLogger? logger = null, IOrderSubmissionGateway? orderGateway = null)
    {
        _config = config;
        _logger = logger ?? new ConsoleBridgeLogger();
        var health = new PersistenceHealthMonitor();
        _validator = new SignalValidator();
        _dedupStore = new DedupStore(config.ProcessedSignalStorePath, _logger, health);
        _riskEngine = new RiskEngine();
        _safety = new SafetyStateManager(config.SafetyStatePath, config.DisarmOnStartup, _logger, health);
        _orderGateway = orderGateway ?? new SimulatedOrderSubmissionGateway();
        var journal = new ExecutionJournal(config.ExecutionJournalPath, _logger);
        _actualState = new ActualStateSnapshotStore(config.ActualStateSnapshotPath, _logger, health);
        _expectedState = new ExpectedStateSnapshotStore(config.ExpectedStateSnapshotPath, _logger, health);
        _reconciliation = new ReconciliationEngine(config.ReconciliationReportPath, _logger);
        _lifecycleTracker = new OrderLifecycleTracker(journal, _actualState, _logger);
        _runtimeMarkers = new RuntimeMarkersStore(config.RuntimeMarkersPath, _logger);

        _runtimeMarkers.MarkStartup();
        if (health.HasCriticalIssues)
        {
            Disarm($"Critical persistence corruption detected: {health.Summarize()}");
        }
    }

    public SignalAck HandleSignal(TradeSignal signal)
    {
        if (_safety.IsDisarmed)
        {
            _logger.Warn($"Signal rejected while disarmed signalId={signal.SignalId} reason={_safety.LastReason}");
            _expectedState.TrackRejected(signal, "Bridge disarmed");
            return Ack(signal, "Disarmed", "Bridge is currently disarmed.");
        }

        if (_dedupStore.IsDuplicate(signal.SignalId))
        {
            _lifecycleTracker.TrackRejected(signal, "Duplicate signalId.");
            _expectedState.TrackRejected(signal, "Duplicate signalId.");
            return Ack(signal, "Rejected", "Duplicate signalId.");
        }

        if (!_validator.Validate(_config, signal, out var validationReason))
        {
            _lifecycleTracker.TrackRejected(signal, validationReason);
            _expectedState.TrackRejected(signal, validationReason);
            return Ack(signal, "Rejected", validationReason);
        }

        var canSubmit = _config.LiveTradingEnabled
            ? _riskEngine.CanSubmit(_config, signal, out var riskReason)
            : _riskEngine.CanSubmitSimulation(_config, signal, out riskReason);

        if (!canSubmit)
        {
            _logger.Warn($"Risk rejected signalId={signal.SignalId} detail={riskReason}");
            _lifecycleTracker.TrackRejected(signal, riskReason);
            _expectedState.TrackRejected(signal, riskReason);
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
            _expectedState.TrackRejected(signal, submission.Detail);
            return Ack(signal, "Rejected", submission.Detail);
        }

        _logger.Info(
            $"Order submission accepted orderId={submission.OrderId} signalIdTag={submission.SignalIdTag} correlationIdTag={submission.CorrelationIdTag}");

        LastOrderId = submission.OrderId;
        LastAcceptedSignal = signal;
        _lifecycleTracker.TrackAccepted(signal, submission);
        _expectedState.TrackAccepted(signal, submission.OrderId, submission.Detail);

        return Ack(signal, "Accepted", $"{submission.Detail} [{mode}]");
    }

    public void OnOrderPartiallyFilled(string orderId, int filledQuantity, string detail)
    {
        if (LastAcceptedSignal is null)
        {
            return;
        }

        _lifecycleTracker.TrackPartialFill(LastAcceptedSignal, orderId, filledQuantity, detail);
        _expectedState.TrackPartialFill(LastAcceptedSignal, orderId, filledQuantity, detail);
    }

    public void OnOrderFilled(string orderId, int filledQuantity, string detail)
    {
        if (LastAcceptedSignal is null)
        {
            return;
        }

        _lifecycleTracker.TrackFullFill(LastAcceptedSignal, orderId, filledQuantity, detail);
        _expectedState.TrackFullFill(LastAcceptedSignal, orderId, filledQuantity, detail);
    }

    public void OnOrderCanceled(string orderId, int filledQuantity, string detail)
    {
        if (LastAcceptedSignal is null)
        {
            return;
        }

        _lifecycleTracker.TrackCanceled(LastAcceptedSignal, orderId, filledQuantity, detail);
        _expectedState.TrackCanceled(LastAcceptedSignal, orderId, filledQuantity, detail);
    }

    public void OnExecutionAmbiguity(string orderId, string detail)
    {
        if (LastAcceptedSignal is null)
        {
            return;
        }

        _lifecycleTracker.TrackExecutionAmbiguity(LastAcceptedSignal, orderId, detail);
        _expectedState.TrackAmbiguous(LastAcceptedSignal, orderId, detail);
        Disarm($"Execution ambiguity detected for orderId={orderId}");
    }

    public ReconciliationReport RunStartupRecoveryCheck()
    {
        return RunRecoveryCheck("Startup");
    }

    public ReconciliationReport RunReconnectRecoveryCheck()
    {
        return RunRecoveryCheck("Reconnect");
    }

    private ReconciliationReport RunRecoveryCheck(string trigger)
    {
        var report = _reconciliation.Reconcile(_expectedState.GetSnapshot(), _actualState.GetSnapshot(), trigger);
        if (!report.IsMatch)
        {
            Disarm($"{trigger} reconciliation mismatch ({report.Mismatches.Count} issue(s))");
        }

        return report;
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

    public void Shutdown()
    {
        _runtimeMarkers.MarkShutdown();
        _logger.Info("Bridge shutdown marker persisted.");
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
