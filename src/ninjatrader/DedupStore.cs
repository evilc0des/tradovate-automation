using System;
using System.Collections.Generic;
using System.IO;

namespace NinjaTraderTradovateBridge;

public sealed class DedupStore
{
    private readonly HashSet<string> _processedSignalIds = new(StringComparer.Ordinal);
    private readonly string _storePath;
    private readonly IBridgeLogger _logger;

    public DedupStore(string storePath, IBridgeLogger logger)
    {
        _storePath = Path.GetFullPath(storePath);
        _logger = logger;
        Load();
    }

    public bool IsDuplicate(string signalId)
    {
        return _processedSignalIds.Contains(signalId);
    }

    public void MarkProcessed(string signalId)
    {
        if (_processedSignalIds.Add(signalId))
        {
            Persist(signalId);
        }
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
                    _processedSignalIds.Add(value);
                }
            }

            _logger.Info($"Loaded {_processedSignalIds.Count} processed signal IDs from {_storePath}.");
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to load processed signal ID store.", ex);
        }
    }

    private void Persist(string signalId)
    {
        try
        {
            var directory = Path.GetDirectoryName(_storePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.AppendAllText(_storePath, signalId + Environment.NewLine);
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to persist processed signal ID.", ex);
        }
    }
}
