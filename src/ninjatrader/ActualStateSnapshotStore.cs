using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace NinjaTraderTradovateBridge;

public sealed class ActualStateSnapshotStore
{
    private readonly string _snapshotPath;
    private readonly IBridgeLogger _logger;
    private readonly PersistenceHealthMonitor _health;

    private readonly Dictionary<string, OrderStateSnapshot> _orders = new(StringComparer.Ordinal);

    public ActualStateSnapshotStore(string snapshotPath, IBridgeLogger logger, PersistenceHealthMonitor health)
    {
        _snapshotPath = Path.GetFullPath(snapshotPath);
        _logger = logger;
        _health = health;
        Load();
    }

    public void UpsertOrder(OrderStateSnapshot snapshot)
    {
        _orders[snapshot.OrderId] = snapshot;
        Persist();
    }

    public ActualStateSnapshot GetSnapshot()
    {
        return new ActualStateSnapshot
        {
            UpdatedUtc = DateTimeOffset.UtcNow,
            Orders = new List<OrderStateSnapshot>(_orders.Values),
        };
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
            var state = JsonSerializer.Deserialize<ActualStateSnapshot>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
            if (state?.Orders is null)
            {
                if (!string.IsNullOrWhiteSpace(json))
                {
                    _health.ReportCritical("ActualState", _snapshotPath, "Could not deserialize snapshot");
                }
                return;
            }

            foreach (var order in state.Orders)
            {
                _orders[order.OrderId] = order;
            }

            _logger.Info($"Loaded {_orders.Count} order snapshots from {_snapshotPath}.");
        }
        catch (Exception ex)
        {
            _health.ReportCritical("ActualState", _snapshotPath, ex.Message);
            _logger.Error("Failed to load actual-state snapshot store.", ex);
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

            var state = new ActualStateSnapshot
            {
                UpdatedUtc = DateTimeOffset.UtcNow,
                Orders = new List<OrderStateSnapshot>(_orders.Values),
            };

            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });
            File.WriteAllText(_snapshotPath, json);
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to persist actual-state snapshot store.", ex);
        }
    }
}

public sealed class ActualStateSnapshot
{
    public DateTimeOffset UpdatedUtc { get; init; }
    public List<OrderStateSnapshot> Orders { get; init; } = new List<OrderStateSnapshot>();
}

public sealed class OrderStateSnapshot
{
    public string OrderId { get; init; } = string.Empty;
    public string SignalId { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
    public string Instrument { get; init; } = string.Empty;
    public string Side { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public int FilledQuantity { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public DateTimeOffset UpdatedUtc { get; init; } = DateTimeOffset.UtcNow;
}
