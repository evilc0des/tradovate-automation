using System;
using System.Collections.Generic;

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

        if (IsExitInstruction(signal.Instruction))
        {
            reason = string.Empty;
            return true; // exits and flatten bypass all time-based gates
        }

        if (IsWithinEventBlackout(config, DateTimeOffset.UtcNow, out reason))
        {
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

        if (IsExitInstruction(signal.Instruction))
        {
            reason = string.Empty;
            return true; // exits and flatten bypass all time-based gates
        }

        if (IsWithinEventBlackout(config, DateTimeOffset.UtcNow, out reason))
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

    /// <summary>
    /// Returns <c>true</c> when <paramref name="nowUtc"/> falls within the
    /// <c>±EventBlackoutRadiusMinutes</c> window of any configured event time.
    /// Event times are taken from <see cref="BridgeConfig.SessionEventTimesUtc"/>
    /// and <see cref="BridgeConfig.NewsEventTimesUtc"/> (both comma-separated
    /// HH:MM UTC strings).
    /// </summary>
    public bool IsWithinEventBlackout(BridgeConfig config, DateTimeOffset nowUtc, out string reason)
    {
        var radius = TimeSpan.FromMinutes(config.EventBlackoutRadiusMinutes);
        var current = nowUtc.TimeOfDay;

        foreach (var timeStr in CollectEventTimes(config))
        {
            if (!TimeSpan.TryParse(timeStr, out var eventTime))
                continue;

            var windowStart = eventTime - radius;
            var windowEnd   = eventTime + radius;

            bool inWindow;
            if (windowStart < TimeSpan.Zero)
            {
                // Window wraps midnight at the start (e.g. event at 00:01 −3min)
                var wrappedStart = windowStart + TimeSpan.FromDays(1);
                inWindow = current >= wrappedStart || current <= windowEnd;
            }
            else if (windowEnd >= TimeSpan.FromDays(1))
            {
                // Window wraps midnight at the end (e.g. event at 23:58 +3min)
                var wrappedEnd = windowEnd - TimeSpan.FromDays(1);
                inWindow = current >= windowStart || current <= wrappedEnd;
            }
            else
            {
                inWindow = current >= windowStart && current <= windowEnd;
            }

            if (inWindow)
            {
                reason = $"Event blackout: \u00b1{config.EventBlackoutRadiusMinutes}min window around {timeStr} UTC.";
                return true;
            }
        }

        reason = string.Empty;
        return false;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IEnumerable<string> CollectEventTimes(BridgeConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.SessionEventTimesUtc))
        {
            foreach (var t in config.SessionEventTimesUtc.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                yield return t;
        }
        if (!string.IsNullOrWhiteSpace(config.NewsEventTimesUtc))
        {
            foreach (var t in config.NewsEventTimesUtc.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                yield return t;
        }
    }

    private static bool IsExitInstruction(string? instruction) =>
        string.Equals(instruction, "exit",    StringComparison.OrdinalIgnoreCase)
     || string.Equals(instruction, "flatten", StringComparison.OrdinalIgnoreCase);
}

