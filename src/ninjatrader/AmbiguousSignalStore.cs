using System;
using System.Collections.Generic;
using System.IO;

namespace NinjaTraderTradovateBridge;

public sealed class AmbiguousSignalStore
{
    private readonly string _storePath;
    private readonly IBridgeLogger _logger;
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);

    public AmbiguousSignalStore(string storePath, IBridgeLogger logger)
    {
        _storePath = Path.GetFullPath(storePath);
        _logger = logger;
        Load();
    }

    public bool IsBlocked(TradeSignal signal)
    {
        return _keys.Contains(BuildKey(signal.SignalId, signal.CorrelationId));
    }

    public void MarkAmbiguous(TradeSignal signal)
    {
        var key = BuildKey(signal.SignalId, signal.CorrelationId);
        if (_keys.Add(key))
        {
            Persist(key);
        }
    }

    private static string BuildKey(string signalId, string correlationId)
    {
        return $"{signalId}|{correlationId}";
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_storePath))
            {
                return;
            }

            foreach (var line in File.ReadLines(_storePath))
            {
                var value = line.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    _keys.Add(value);
                }
            }

            _logger.Info($"Loaded {_keys.Count} ambiguous signal keys from {_storePath}.");
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to load ambiguous signal store.", ex);
        }
    }

    private void Persist(string key)
    {
        try
        {
            var directory = Path.GetDirectoryName(_storePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.AppendAllText(_storePath, key + Environment.NewLine);
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to persist ambiguous signal key.", ex);
        }
    }
}
