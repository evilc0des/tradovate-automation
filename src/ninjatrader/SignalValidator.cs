using System;
using System.Linq;

namespace NinjaTraderTradovateBridge;

public sealed class SignalValidator
{
    public bool Validate(BridgeConfig config, TradeSignal signal, out string reason)
    {
        if (signal.MessageType != "TradeSignal" || signal.Version != "v1")
        {
            reason = "Unsupported envelope.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(signal.SignalId))
        {
            reason = "Missing signalId.";
            return false;
        }

        if (!string.Equals(signal.OrderType, "Market", StringComparison.OrdinalIgnoreCase))
        {
            reason = "Unsupported order type for v1.";
            return false;
        }

        if (signal.Quantity < 1 || signal.Quantity > config.MaxOrderQuantity)
        {
            reason = "Quantity violates configured limits.";
            return false;
        }

        if (!string.Equals(signal.Account, config.AllowedAccount, StringComparison.Ordinal))
        {
            reason = "Signal account is not allowed.";
            return false;
        }

        var allowedInstrument = config.AllowedInstruments.Any(i =>
            string.Equals(i, signal.Instrument, StringComparison.OrdinalIgnoreCase));
        if (!allowedInstrument)
        {
            reason = "Signal instrument is not allowed.";
            return false;
        }

        var ageMs = (DateTimeOffset.UtcNow - signal.Timestamp).TotalMilliseconds;
        if (ageMs > config.MaxSignalAgeMs)
        {
            reason = "Signal exceeded max age.";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
