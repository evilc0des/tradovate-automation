using System;
using System.IO;
using System.Text.Json;

namespace NinjaTraderTradovateBridge;

public sealed class SafetyStateManager
{
    private readonly string _statePath;
    private readonly IBridgeLogger _logger;

    public bool IsDisarmed { get; private set; }
    public string? LastReason { get; private set; }

    public SafetyStateManager(string statePath, bool disarmOnStartup, IBridgeLogger logger)
    {
        _statePath = Path.GetFullPath(statePath);
        _logger = logger;
        IsDisarmed = disarmOnStartup;
        Load();
        Persist();
    }

    public void Arm()
    {
        IsDisarmed = false;
        LastReason = null;
        Persist();
    }

    public void Disarm(string reason)
    {
        IsDisarmed = true;
        LastReason = reason;
        Persist();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_statePath))
            {
                return;
            }

            var json = File.ReadAllText(_statePath);
            var snapshot = JsonSerializer.Deserialize<SafetyStateSnapshot>(json);
            if (snapshot is null)
            {
                return;
            }

            IsDisarmed = snapshot.IsDisarmed;
            LastReason = snapshot.LastReason;
            _logger.Info($"Loaded safety state: disarmed={IsDisarmed}.");
        }
        catch (Exception ex)
        {
            IsDisarmed = true;
            LastReason = "Safety state load failed";
            _logger.Error("Failed to load safety state; forcing disarmed mode.", ex);
        }
    }

    private void Persist()
    {
        try
        {
            var directory = Path.GetDirectoryName(_statePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var snapshot = new SafetyStateSnapshot
            {
                IsDisarmed = IsDisarmed,
                LastReason = LastReason,
                UpdatedUtc = DateTimeOffset.UtcNow,
            };

            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_statePath, json);
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to persist safety state.", ex);
        }
    }

    private sealed class SafetyStateSnapshot
    {
        public bool IsDisarmed { get; set; }
        public string? LastReason { get; set; }
        public DateTimeOffset UpdatedUtc { get; set; }
    }
}
