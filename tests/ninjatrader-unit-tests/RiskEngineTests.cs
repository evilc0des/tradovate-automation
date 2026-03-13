using System;
using NinjaTraderTradovateBridge;

namespace NinjaTraderBridge.UnitTests;

public sealed class RiskEngineTests
{
    private readonly RiskEngine _engine = new();

    // ── Simulation mode ───────────────────────────────────────────────────────

    [Fact]
    public void Simulation_valid_signal_passes()
    {
        var cfg = Helpers.DefaultConfig();
        var sig = Helpers.ValidSignal();
        Assert.True(_engine.CanSubmitSimulation(cfg, sig, out var reason));
        Assert.Empty(reason);
    }

    [Fact]
    public void Simulation_quantity_above_max_is_rejected()
    {
        var cfg = Helpers.DefaultConfig(maxQty: 1);
        var sig = Helpers.ValidSignal(quantity: 2);
        Assert.False(_engine.CanSubmitSimulation(cfg, sig, out var reason));
        Assert.False(string.IsNullOrEmpty(reason));
    }

    [Fact]
    public void Simulation_quantity_at_max_is_accepted()
    {
        var cfg = Helpers.DefaultConfig(maxQty: 2);
        var sig = Helpers.ValidSignal(quantity: 2);
        Assert.True(_engine.CanSubmitSimulation(cfg, sig, out _));
    }

    // ── Session window ────────────────────────────────────────────────────────

    [Fact]
    public void Within_session_window_is_accepted()
    {
        var cfg = Helpers.DefaultConfig(sessionStart: "00:00", sessionEnd: "23:59");
        var sig = Helpers.ValidSignal();
        Assert.True(_engine.CanSubmitSimulation(cfg, sig, out _));
    }

    [Fact]
    public void Before_session_start_is_rejected()
    {
        // Window is 14:00–23:00; inject a time before the window.
        var cfg = Helpers.DefaultConfig(sessionStart: "14:00", sessionEnd: "23:00");
        var sig = Helpers.ValidSignal();
        // Pass a synthetic "now" that is before 14:00.
        var earlyMorning = new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
        Assert.False(_engine.IsWithinSessionWindow(cfg, earlyMorning, out var reason));
        Assert.False(string.IsNullOrEmpty(reason));
    }

    [Fact]
    public void After_session_end_is_rejected()
    {
        var cfg = Helpers.DefaultConfig(sessionStart: "08:00", sessionEnd: "12:00");
        var lateAfternoon = new DateTimeOffset(2026, 1, 1, 15, 0, 0, TimeSpan.Zero);
        Assert.False(_engine.IsWithinSessionWindow(cfg, lateAfternoon, out _));
    }

    [Fact]
    public void On_session_boundary_start_is_accepted()
    {
        var cfg = Helpers.DefaultConfig(sessionStart: "09:30", sessionEnd: "16:00");
        var onBoundary = new DateTimeOffset(2026, 1, 1, 9, 30, 0, TimeSpan.Zero);
        Assert.True(_engine.IsWithinSessionWindow(cfg, onBoundary, out _));
    }

    [Fact]
    public void Overnight_session_window_straddles_midnight()
    {
        // start=22:00, end=02:00 — wraps midnight
        var cfg = Helpers.DefaultConfig(sessionStart: "22:00", sessionEnd: "02:00");
        var midnight = new DateTimeOffset(2026, 1, 1, 23, 30, 0, TimeSpan.Zero);
        Assert.True(_engine.IsWithinSessionWindow(cfg, midnight, out _));

        var midday = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        Assert.False(_engine.IsWithinSessionWindow(cfg, midday, out _));
    }

    [Fact]
    public void Invalid_session_config_is_rejected()
    {
        var cfg = Helpers.DefaultConfig(sessionStart: "NOTATIME", sessionEnd: "23:59");
        Assert.False(_engine.IsWithinSessionWindow(cfg, DateTimeOffset.UtcNow, out _));
    }

    // ── Live mode ─────────────────────────────────────────────────────────────

    [Fact]
    public void Live_mode_rejected_when_disabled_in_config()
    {
        var cfg = Helpers.DefaultConfig(liveEnabled: false);
        var sig = Helpers.ValidSignal();
        Assert.False(_engine.CanSubmit(cfg, sig, out var reason));
        Assert.False(string.IsNullOrEmpty(reason));
    }

    [Fact]
    public void Live_mode_allows_valid_signal_when_enabled()
    {
        var cfg = Helpers.DefaultConfig(liveEnabled: true);
        var sig = Helpers.ValidSignal();
        Assert.True(_engine.CanSubmit(cfg, sig, out var reason));
        Assert.Empty(reason);
    }

    [Fact]
    public void Live_mode_rejects_quantity_above_max()
    {
        var cfg = Helpers.DefaultConfig(liveEnabled: true, maxQty: 1);
        var sig = Helpers.ValidSignal(quantity: 2);
        Assert.False(_engine.CanSubmit(cfg, sig, out _));
    }
}
