using System;
using NinjaTraderTradovateBridge;

namespace NinjaTraderBridge.UnitTests;

public sealed class SignalValidatorTests
{
    private readonly SignalValidator _validator = new();

    // ── Envelope ──────────────────────────────────────────────────────────────

    [Fact]
    public void Valid_signal_passes()
    {
        var cfg = Helpers.DefaultConfig();
        var sig = Helpers.ValidSignal();
        Assert.True(_validator.Validate(cfg, sig, out var reason));
        Assert.Empty(reason);
    }

    [Theory]
    [InlineData("", "v1")]
    [InlineData("TradeSignal", "")]
    [InlineData("WRONG", "v1")]
    [InlineData("TradeSignal", "v2")]
    public void Unsupported_envelope_is_rejected(string msgType, string version)
    {
        var cfg = Helpers.DefaultConfig();
        var sig = Helpers.ValidSignal();
        sig.MessageType = msgType;
        sig.Version = version;
        Assert.False(_validator.Validate(cfg, sig, out _));
    }

    // ── Required fields ───────────────────────────────────────────────────────

    [Fact]
    public void Missing_signalId_is_rejected()
    {
        var cfg = Helpers.DefaultConfig();
        var sig = Helpers.ValidSignal();
        sig.SignalId = string.Empty;
        Assert.False(_validator.Validate(cfg, sig, out _));
    }

    [Fact]
    public void Missing_correlationId_is_rejected()
    {
        var cfg = Helpers.DefaultConfig();
        var sig = Helpers.ValidSignal();
        sig.CorrelationId = string.Empty;
        Assert.False(_validator.Validate(cfg, sig, out _));
    }

    [Fact]
    public void Missing_sourceId_is_rejected()
    {
        var cfg = Helpers.DefaultConfig();
        var sig = Helpers.ValidSignal();
        sig.SourceId = string.Empty;
        Assert.False(_validator.Validate(cfg, sig, out _));
    }

    [Fact]
    public void Missing_strategyId_is_rejected()
    {
        var cfg = Helpers.DefaultConfig();
        var sig = Helpers.ValidSignal();
        sig.StrategyId = string.Empty;
        Assert.False(_validator.Validate(cfg, sig, out _));
    }

    // ── Allowlists ────────────────────────────────────────────────────────────

    [Fact]
    public void Disallowed_source_is_rejected()
    {
        var cfg = Helpers.DefaultConfig(sources: new[] { "rust.strategy" });
        var sig = Helpers.ValidSignal(source: "evil.source");
        Assert.False(_validator.Validate(cfg, sig, out _));
    }

    [Fact]
    public void Source_check_is_case_insensitive()
    {
        var cfg = Helpers.DefaultConfig(sources: new[] { "rust.strategy" });
        var sig = Helpers.ValidSignal(source: "RUST.STRATEGY");
        Assert.True(_validator.Validate(cfg, sig, out _));
    }

    [Fact]
    public void Disallowed_account_is_rejected()
    {
        var cfg = Helpers.DefaultConfig(account: "SIM101");
        var sig = Helpers.ValidSignal(account: "LIVE999");
        Assert.False(_validator.Validate(cfg, sig, out _));
    }

    [Fact]
    public void Disallowed_instrument_is_rejected()
    {
        var cfg = Helpers.DefaultConfig(instruments: new[] { "MES 06-26" });
        var sig = Helpers.ValidSignal(instrument: "NQ 06-26");
        Assert.False(_validator.Validate(cfg, sig, out _));
    }

    [Fact]
    public void Instrument_check_is_case_insensitive()
    {
        var cfg = Helpers.DefaultConfig(instruments: new[] { "MES 06-26" });
        var sig = Helpers.ValidSignal(instrument: "mes 06-26");
        Assert.True(_validator.Validate(cfg, sig, out _));
    }

    // ── Side ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Buy")]
    [InlineData("Sell")]
    [InlineData("BUY")]
    [InlineData("SELL")]
    public void Valid_sides_are_accepted(string side)
    {
        var cfg = Helpers.DefaultConfig();
        var sig = Helpers.ValidSignal(side: side);
        Assert.True(_validator.Validate(cfg, sig, out _));
    }

    [Theory]
    [InlineData("Long")]
    [InlineData("Short")]
    [InlineData("")]
    [InlineData("HOLD")]
    public void Invalid_sides_are_rejected(string side)
    {
        var cfg = Helpers.DefaultConfig();
        var sig = Helpers.ValidSignal(side: side);
        Assert.False(_validator.Validate(cfg, sig, out _));
    }

    // ── Order type ────────────────────────────────────────────────────────────

    [Fact]
    public void Non_market_order_type_is_rejected()
    {
        var cfg = Helpers.DefaultConfig();
        var sig = Helpers.ValidSignal();
        sig.OrderType = "Limit";
        Assert.False(_validator.Validate(cfg, sig, out _));
    }

    // ── Quantity ──────────────────────────────────────────────────────────────

    [Fact]
    public void Quantity_zero_is_rejected()
    {
        var cfg = Helpers.DefaultConfig(maxQty: 2);
        var sig = Helpers.ValidSignal(quantity: 0);
        Assert.False(_validator.Validate(cfg, sig, out _));
    }

    [Fact]
    public void Quantity_exceeding_max_is_rejected()
    {
        var cfg = Helpers.DefaultConfig(maxQty: 1);
        var sig = Helpers.ValidSignal(quantity: 2);
        Assert.False(_validator.Validate(cfg, sig, out _));
    }

    [Fact]
    public void Quantity_at_max_is_accepted()
    {
        var cfg = Helpers.DefaultConfig(maxQty: 2);
        var sig = Helpers.ValidSignal(quantity: 2);
        Assert.True(_validator.Validate(cfg, sig, out _));
    }

    // ── Staleness ─────────────────────────────────────────────────────────────

    [Fact]
    public void Fresh_signal_is_accepted()
    {
        var cfg = Helpers.DefaultConfig(maxAgeMs: 3000);
        var sig = Helpers.ValidSignal(timestamp: DateTimeOffset.UtcNow.AddMilliseconds(-100));
        Assert.True(_validator.Validate(cfg, sig, out _));
    }

    [Fact]
    public void Stale_signal_is_rejected()
    {
        var cfg = Helpers.DefaultConfig(maxAgeMs: 1000);
        var sig = Helpers.ValidSignal(timestamp: DateTimeOffset.UtcNow.AddMilliseconds(-2000));
        Assert.False(_validator.Validate(cfg, sig, out _));
    }

    [Fact]
    public void Signal_exactly_at_max_age_is_rejected()
    {
        var cfg = Helpers.DefaultConfig(maxAgeMs: 1000);
        // UtcNow - 1001 ms is clearly over the limit
        var sig = Helpers.ValidSignal(timestamp: DateTimeOffset.UtcNow.AddMilliseconds(-1001));
        Assert.False(_validator.Validate(cfg, sig, out _));
    }
}
