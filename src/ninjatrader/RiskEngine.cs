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

        reason = string.Empty;
        return true;
    }
}
