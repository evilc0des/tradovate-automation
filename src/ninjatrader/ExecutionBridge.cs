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
            return Ack(signal, "Rejected", "Duplicate signalId.");
        }

        if (!_validator.Validate(_config, signal, out var validationReason))
        {
            return Ack(signal, "Rejected", validationReason);
        }

        var canSubmit = _config.LiveTradingEnabled
            ? _riskEngine.CanSubmit(_config, signal, out var riskReason)
            : _riskEngine.CanSubmitSimulation(_config, signal, out riskReason);

        if (!canSubmit)
        {
            _logger.Warn($"Risk rejected signalId={signal.SignalId} detail={riskReason}");
            return Ack(signal, "Rejected", riskReason);
        }

        _dedupStore.MarkProcessed(signal.SignalId);

        var mode = _config.LiveTradingEnabled ? "Live" : "Simulation";
        _logger.Info($"Submitting order mode={mode} signalId={signal.SignalId} correlationId={signal.CorrelationId}");
        var submission = _orderGateway.SubmitMarketOrder(signal);

        if (!submission.Accepted)
        {
            _logger.Warn($"Order submission rejected signalId={signal.SignalId} detail={submission.Detail}");
            return Ack(signal, "Rejected", submission.Detail);
        }

        _logger.Info(
            $"Order submission accepted orderId={submission.OrderId} signalIdTag={submission.SignalIdTag} correlationIdTag={submission.CorrelationIdTag}");
        return Ack(signal, "Accepted", $"{submission.Detail} [{mode}]");
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
