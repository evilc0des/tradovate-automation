using System;
using System.IO;
using NinjaTraderTradovateBridge;

namespace NinjaTraderBridge.UnitTests;

/// <summary>
/// Tests for the ExecutionBridge overall guard flow:
/// arm/disarm state, dedup, validation, risk, and order submission.
/// Acts as a config validation test too (ensures safety defaults are respected).
/// </summary>
public sealed class ExecutionBridgeTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"bridge_{Guid.NewGuid():N}");

    public ExecutionBridgeTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private BridgeConfig MakeConfig(bool disarmOnStartup = false) =>
        new BridgeConfig
        {
            LiveTradingEnabled = false,
            DisarmOnStartup = disarmOnStartup,
            AllowedAccount = "SIM101",
            AllowedInstruments = new[] { "MES 06-26" },
            AllowedSignalSources = new[] { "rust.strategy" },
            MaxOrderQuantity = 1,
            MaxSignalAgeMs = 5000,
            SessionStartUtc = "00:00",
            SessionEndUtc = "23:59",
            ProcessedSignalStorePath = Path.Combine(_tempDir, "ids.txt"),
            SafetyStatePath = Path.Combine(_tempDir, "safety.json"),
            ExecutionJournalPath = Path.Combine(_tempDir, "journal.ndjson"),
            ActualStateSnapshotPath = Path.Combine(_tempDir, "actual.json"),
            ExpectedStateSnapshotPath = Path.Combine(_tempDir, "expected.json"),
            ReconciliationReportPath = Path.Combine(_tempDir, "recon.json"),
            RuntimeMarkersPath = Path.Combine(_tempDir, "markers.ndjson"),
            AmbiguousSignalStorePath = Path.Combine(_tempDir, "ambi.txt"),
        };

    private ExecutionBridge AcquireBridge(bool disarmOnStartup = false, IOrderSubmissionGateway? gateway = null)
    {
        var cfg = MakeConfig(disarmOnStartup);
        var bridge = new ExecutionBridge(cfg, Helpers.NullLogger, gateway ?? new AlwaysAcceptGateway());
        return bridge;
    }

    // ── Arm / Disarm ──────────────────────────────────────────────────────────

    [Fact]
    public void Bridge_starts_disarmed_when_DisarmOnStartup_is_true()
    {
        var bridge = AcquireBridge(disarmOnStartup: true);
        Assert.True(bridge.IsDisarmed);
    }

    [Fact]
    public void Bridge_starts_armed_when_DisarmOnStartup_is_false()
    {
        var bridge = AcquireBridge(disarmOnStartup: false);
        Assert.False(bridge.IsDisarmed);
    }

    [Fact]
    public void Arm_then_Disarm_toggles_state()
    {
        var bridge = AcquireBridge(disarmOnStartup: true);
        bridge.Arm();
        Assert.False(bridge.IsDisarmed);
        bridge.Disarm("test reason");
        Assert.True(bridge.IsDisarmed);
    }

    // ── Signal rejected while disarmed ────────────────────────────────────────

    [Fact]
    public void Signal_is_rejected_while_disarmed()
    {
        var bridge = AcquireBridge(disarmOnStartup: true);
        var ack = bridge.HandleSignal(Helpers.ValidSignal());
        Assert.Equal("Disarmed", ack.Status);
    }

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public void Valid_signal_is_accepted_when_armed()
    {
        var bridge = AcquireBridge(disarmOnStartup: false);
        var sig = Helpers.ValidSignal();
        var ack = bridge.HandleSignal(sig);
        Assert.Equal("Accepted", ack.Status);
        Assert.Equal(sig.SignalId, ack.SignalId);
        Assert.Equal(sig.CorrelationId, ack.CorrelationId);
    }

    [Fact]
    public void Accepted_signal_left_ordered_id_on_bridge()
    {
        var gateway = new AlwaysAcceptGateway();
        var bridge = AcquireBridge(disarmOnStartup: false, gateway);
        bridge.HandleSignal(Helpers.ValidSignal());
        Assert.NotNull(bridge.LastOrderId);
        Assert.StartsWith("TST-", bridge.LastOrderId);
    }

    // ── Deduplication ─────────────────────────────────────────────────────────

    [Fact]
    public void Duplicate_signal_is_rejected()
    {
        var bridge = AcquireBridge(disarmOnStartup: false);
        var sig = Helpers.ValidSignal(signalId: "FIXED-ID-001");
        bridge.HandleSignal(sig);  // first — accepted
        var ack2 = bridge.HandleSignal(sig);  // duplicate
        Assert.Equal("Rejected", ack2.Status);
        Assert.Contains("Duplicate", ack2.Detail, StringComparison.OrdinalIgnoreCase);
    }

    // ── Validation failures ───────────────────────────────────────────────────

    [Fact]
    public void Wrong_account_is_rejected()
    {
        var bridge = AcquireBridge(disarmOnStartup: false);
        var sig = Helpers.ValidSignal(account: "WRONG-ACCOUNT");
        var ack = bridge.HandleSignal(sig);
        Assert.Equal("Rejected", ack.Status);
    }

    [Fact]
    public void Wrong_instrument_is_rejected()
    {
        var bridge = AcquireBridge(disarmOnStartup: false);
        var sig = Helpers.ValidSignal(instrument: "NQ 06-26");
        var ack = bridge.HandleSignal(sig);
        Assert.Equal("Rejected", ack.Status);
    }

    [Fact]
    public void Stale_signal_is_rejected()
    {
        var bridge = AcquireBridge(disarmOnStartup: false);
        var sig = Helpers.ValidSignal(timestamp: DateTimeOffset.UtcNow.AddSeconds(-10));
        var ack = bridge.HandleSignal(sig);
        Assert.Equal("Rejected", ack.Status);
    }

    // ── Gateway rejection ─────────────────────────────────────────────────────

    [Fact]
    public void Gateway_rejection_returns_rejected_ack()
    {
        var bridge = AcquireBridge(disarmOnStartup: false, gateway: new AlwaysRejectGateway());
        var ack = bridge.HandleSignal(Helpers.ValidSignal());
        Assert.Equal("Rejected", ack.Status);
    }
}
