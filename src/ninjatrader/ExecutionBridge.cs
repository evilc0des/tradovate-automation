using System;

namespace NinjaTraderTradovateBridge;

public sealed class ExecutionBridge
{
    private readonly BridgeConfig _config;
    private readonly SignalValidator _validator;
    private readonly DedupStore _dedupStore;
    private readonly RiskEngine _riskEngine;

    public bool IsDisarmed { get; private set; }

    public ExecutionBridge(BridgeConfig config)
    {
        _config = config;
        _validator = new SignalValidator();
        _dedupStore = new DedupStore();
        _riskEngine = new RiskEngine();
        IsDisarmed = config.DisarmOnStartup;
    }

    public SignalAck HandleSignal(TradeSignal signal)
    {
        if (IsDisarmed)
        {
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
            return Ack(signal, "Rejected", riskReason);
        }

        _dedupStore.MarkProcessed(signal.SignalId);

        // Hook: route to NinjaTrader order submission pipeline here.
        var mode = _config.LiveTradingEnabled ? "Live" : "Simulation";
        return Ack(signal, "Accepted", $"Validated for {mode} submission.");
    }

    public void Arm()
    {
        IsDisarmed = false;
    }

    public void Disarm(string reason)
    {
        IsDisarmed = true;
        _ = reason;
        // Hook: persist disarm reason and emit state-change event.
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
