using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace NinjaTraderTradovateBridge;

public sealed class ExpectedStateSnapshotStore
{
    private readonly string _snapshotPath;
    private readonly IBridgeLogger _logger;
    private readonly PersistenceHealthMonitor _health;

    private readonly Dictionary<string, ExpectedOrderSnapshot> _orders = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _expectedPositionsByInstrument = new(StringComparer.OrdinalIgnoreCase);

    public ExpectedStateSnapshotStore(string snapshotPath, IBridgeLogger logger, PersistenceHealthMonitor health)
    {
        _snapshotPath = Path.GetFullPath(snapshotPath);
        _logger = logger;
        _health = health;
        Load();
    }

    public void TrackAccepted(TradeSignal signal, string orderId, string detail)
    {
        var current = GetOrCreate(signal, orderId);
        current.Status = "Working";
        current.Detail = detail;
        Persist();
    }

    public void TrackRejected(TradeSignal signal, string detail)
    {
        var current = GetOrCreate(signal, string.Empty);
        current.Status = "Rejected";
        current.Detail = detail;
        Persist();
    }

    public void TrackPartialFill(TradeSignal signal, string orderId, int filledQuantity, string detail)
    {
        UpdateFill(signal, orderId, filledQuantity, "Working", detail);
    }

    public void TrackFullFill(TradeSignal signal, string orderId, int filledQuantity, string detail)
    {
        UpdateFill(signal, orderId, filledQuantity, "Filled", detail);
    }

    public void TrackCanceled(TradeSignal signal, string orderId, int filledQuantity, string detail)
    {
        UpdateFill(signal, orderId, filledQuantity, "Canceled", detail);
    }

    public void TrackAmbiguous(TradeSignal signal, string orderId, string detail)
    {
        var current = GetOrCreate(signal, orderId);
        current.Status = "Ambiguous";
        current.Detail = detail;
        current.UpdatedUtc = DateTimeOffset.UtcNow;
        Persist();
    }

    public ExpectedStateSnapshot GetSnapshot()
    {
        return new ExpectedStateSnapshot
        {
            UpdatedUtc = DateTimeOffset.UtcNow,
            Orders = [.. _orders.Values],
            ExpectedPositionsByInstrument = new Dictionary<string, int>(_expectedPositionsByInstrument, StringComparer.OrdinalIgnoreCase),
        };
    }

    private void UpdateFill(TradeSignal signal, string orderId, int filledQuantity, string status, string detail)
    {
        var current = GetOrCreate(signal, orderId);
        var delta = filledQuantity - current.FilledQuantity;
        current.FilledQuantity = filledQuantity;
        current.Status = status;
        current.Detail = detail;
        current.UpdatedUtc = DateTimeOffset.UtcNow;

        if (delta != 0)
        {
            var sign = string.Equals(signal.Side, "Buy", StringComparison.OrdinalIgnoreCase) ? 1 : -1;
            var existing = _expectedPositionsByInstrument.TryGetValue(signal.Instrument, out var position) ? position : 0;
            _expectedPositionsByInstrument[signal.Instrument] = existing + (delta * sign);
        }

        Persist();
    }

    private ExpectedOrderSnapshot GetOrCreate(TradeSignal signal, string orderId)
    {
        var key = string.IsNullOrWhiteSpace(orderId) ? $"sig:{signal.SignalId}" : orderId;
        if (!_orders.TryGetValue(key, out var current))
        {
            current = new ExpectedOrderSnapshot
            {
                OrderId = orderId,
                SignalId = signal.SignalId,
                CorrelationId = signal.CorrelationId,
                Instrument = signal.Instrument,
                Side = signal.Side,
                Quantity = signal.Quantity,
                FilledQuantity = 0,
                Status = "Pending",
                Detail = string.Empty,
                UpdatedUtc = DateTimeOffset.UtcNow,
            };
            _orders[key] = current;
        }

        return current;
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_snapshotPath))
            {
                return;
            }

            var json = File.ReadAllText(_snapshotPath);
            var snapshot = JsonSerializer.Deserialize<ExpectedStateSnapshot>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
            if (snapshot is null)
            {
                if (!string.IsNullOrWhiteSpace(json))
                {
                    _health.ReportCritical("ExpectedState", _snapshotPath, "Could not deserialize snapshot");
                }
                return;
            }

            foreach (var order in snapshot.Orders)
            {
                var key = string.IsNullOrWhiteSpace(order.OrderId) ? $"sig:{order.SignalId}" : order.OrderId;
                _orders[key] = order;
            }

            foreach (var pair in snapshot.ExpectedPositionsByInstrument)
            {
                _expectedPositionsByInstrument[pair.Key] = pair.Value;
            }

            _logger.Info($"Loaded expected state from {_snapshotPath} (orders={_orders.Count}, positions={_expectedPositionsByInstrument.Count}).");
        }
        catch (Exception ex)
        {
            _health.ReportCritical("ExpectedState", _snapshotPath, ex.Message);
            _logger.Error("Failed to load expected-state snapshot store.", ex);
        }
    }

    private void Persist()
    {
        try
        {
            var directory = Path.GetDirectoryName(_snapshotPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var snapshot = GetSnapshot();
            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });
            File.WriteAllText(_snapshotPath, json);
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to persist expected-state snapshot store.", ex);
        }
    }
}

public sealed class ExpectedStateSnapshot
{
    public DateTimeOffset UpdatedUtc { get; init; }
    public List<ExpectedOrderSnapshot> Orders { get; init; } = [];
    public Dictionary<string, int> ExpectedPositionsByInstrument { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ExpectedOrderSnapshot
{
    public string OrderId { get; set; } = string.Empty;
    public string SignalId { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string Instrument { get; set; } = string.Empty;
    public string Side { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int FilledQuantity { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public DateTimeOffset UpdatedUtc { get; set; }
}
