using System;
using System.Collections.Generic;

namespace NinjaTraderTradovateBridge;

public sealed class PersistenceHealthMonitor
{
    private readonly List<string> _criticalIssues = [];

    public bool HasCriticalIssues => _criticalIssues.Count > 0;

    public IReadOnlyList<string> CriticalIssues => _criticalIssues;

    public void ReportCritical(string subsystem, string path, string reason)
    {
        _criticalIssues.Add($"{subsystem}::{path}::{reason}");
    }

    public string Summarize()
    {
        return HasCriticalIssues
            ? string.Join(" | ", _criticalIssues)
            : "ok";
    }
}
