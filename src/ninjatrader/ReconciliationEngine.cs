using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace NinjaTraderTradovateBridge;

public sealed class ReconciliationEngine
{
    private readonly string _reportPath;
    private readonly IBridgeLogger _logger;

    public ReconciliationEngine(string reportPath, IBridgeLogger logger)
    {
        _reportPath = Path.GetFullPath(reportPath);
        _logger = logger;
    }

    public ReconciliationReport Reconcile(ExpectedStateSnapshot expected, ActualStateSnapshot actual, string trigger)
    {
        var mismatches = new List<ReconciliationMismatch>();

        CompareWorkingOrders(expected, actual, mismatches);
        ComparePositions(expected, actual, mismatches);

        var report = new ReconciliationReport
        {
            Timestamp = DateTimeOffset.UtcNow,
            Trigger = trigger,
            IsMatch = mismatches.Count == 0,
            Mismatches = mismatches,
        };

        Persist(report);
        _logger.Info($"Reconciliation trigger={trigger} match={report.IsMatch} mismatches={mismatches.Count}");
        return report;
    }

    private static void CompareWorkingOrders(ExpectedStateSnapshot expected, ActualStateSnapshot actual, List<ReconciliationMismatch> mismatches)
    {
        var expectedWorking = expected.Orders
            .Where(o => string.Equals(o.Status, "Working", StringComparison.OrdinalIgnoreCase))
            .Select(o => o.OrderId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);

        var actualWorking = actual.Orders
            .Where(o => string.Equals(o.Status, "Accepted", StringComparison.OrdinalIgnoreCase)
                || string.Equals(o.Status, "PartialFill", StringComparison.OrdinalIgnoreCase))
            .Select(o => o.OrderId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var expectedId in expectedWorking)
        {
            if (!actualWorking.Contains(expectedId))
            {
                mismatches.Add(new ReconciliationMismatch
                {
                    Type = "WorkingOrder",
                    Key = expectedId,
                    Expected = "Working",
                    Actual = "Missing",
                    Detail = "Expected working order not present in actual state.",
                });
            }
        }

        foreach (var actualId in actualWorking)
        {
            if (!expectedWorking.Contains(actualId))
            {
                mismatches.Add(new ReconciliationMismatch
                {
                    Type = "WorkingOrder",
                    Key = actualId,
                    Expected = "Missing",
                    Actual = "Working",
                    Detail = "Actual working order not present in expected state.",
                });
            }
        }
    }

    private static void ComparePositions(ExpectedStateSnapshot expected, ActualStateSnapshot actual, List<ReconciliationMismatch> mismatches)
    {
        var actualPositions = CalculateActualPositions(actual);

        var instruments = new HashSet<string>(expected.ExpectedPositionsByInstrument.Keys, StringComparer.OrdinalIgnoreCase);
        instruments.UnionWith(actualPositions.Keys);

        foreach (var instrument in instruments)
        {
            var expectedPos = expected.ExpectedPositionsByInstrument.TryGetValue(instrument, out var e) ? e : 0;
            var actualPos = actualPositions.TryGetValue(instrument, out var a) ? a : 0;
            if (expectedPos != actualPos)
            {
                mismatches.Add(new ReconciliationMismatch
                {
                    Type = "Position",
                    Key = instrument,
                    Expected = expectedPos.ToString(),
                    Actual = actualPos.ToString(),
                    Detail = "Expected vs actual position mismatch.",
                });
            }
        }
    }

    private static Dictionary<string, int> CalculateActualPositions(ActualStateSnapshot actual)
    {
        var positions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var order in actual.Orders)
        {
            if (order.FilledQuantity <= 0)
            {
                continue;
            }

            var sign = string.Equals(order.Side, "Buy", StringComparison.OrdinalIgnoreCase) ? 1 : -1;
            var existing = positions.TryGetValue(order.Instrument, out var value) ? value : 0;
            positions[order.Instrument] = existing + (order.FilledQuantity * sign);
        }

        return positions;
    }

    private void Persist(ReconciliationReport report)
    {
        try
        {
            var directory = Path.GetDirectoryName(_reportPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });
            File.WriteAllText(_reportPath, json);
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to persist reconciliation report.", ex);
        }
    }
}

public sealed class ReconciliationReport
{
    public DateTimeOffset Timestamp { get; init; }
    public string Trigger { get; init; } = string.Empty;
    public bool IsMatch { get; init; }
    public List<ReconciliationMismatch> Mismatches { get; init; } = new List<ReconciliationMismatch>();
}

public sealed class ReconciliationMismatch
{
    public string Type { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
    public string Expected { get; init; } = string.Empty;
    public string Actual { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}
