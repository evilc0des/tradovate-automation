using System;

namespace NinjaTraderTradovateBridge;

public sealed class RiskEngine
{
    public bool CanSubmit(BridgeConfig config, TradeSignal signal, out string reason)
    {
        if (!config.LiveTradingEnabled)
        {
            reason = "Live trading disabled; simulation-only mode active.";
            return false;
        }

        if (signal.Quantity > config.MaxOrderQuantity)
        {
            reason = "Order quantity exceeds configured max.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public bool CanSubmitSimulation(BridgeConfig config, TradeSignal signal, out string reason)
    {
        if (signal.Quantity > config.MaxOrderQuantity)
        {
            reason = "Order quantity exceeds configured max.";
            return false;
        }

        if (!IsWithinSessionWindow(config, DateTimeOffset.UtcNow, out reason))
        {
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public bool IsWithinSessionWindow(BridgeConfig config, DateTimeOffset nowUtc, out string reason)
    {
        if (!TimeSpan.TryParse(config.SessionStartUtc, out var start) ||
            !TimeSpan.TryParse(config.SessionEndUtc, out var end))
        {
            reason = "Invalid session window configuration.";
            return false;
        }

        var current = nowUtc.TimeOfDay;
        var inWindow = start <= end
            ? current >= start && current <= end
            : current >= start || current <= end;

        if (!inWindow)
        {
            reason = "Outside configured session window.";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
