using System;
using System.Collections.Generic;

namespace NinjaTraderTradovateBridge;

public sealed class DedupStore
{
    private readonly HashSet<string> _processedSignalIds = new(StringComparer.Ordinal);

    public bool IsDuplicate(string signalId)
    {
        return _processedSignalIds.Contains(signalId);
    }

    public void MarkProcessed(string signalId)
    {
        _processedSignalIds.Add(signalId);
    }
}
