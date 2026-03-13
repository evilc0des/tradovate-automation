using System;
using System.Collections.Generic;
using System.IO;
using NinjaTraderTradovateBridge;

namespace NinjaTraderBridge.UnitTests;

public sealed class ReconciliationEngineTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"recon_{Guid.NewGuid():N}");
    private string TempPath(string name) => Path.Combine(_tempDir, name);

    public ReconciliationEngineTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private ReconciliationEngine Engine() =>
        new ReconciliationEngine(TempPath("recon.json"), Helpers.NullLogger);

    private static ExpectedStateSnapshot EmptyExpected() =>
        new ExpectedStateSnapshot
        {
            UpdatedUtc = DateTimeOffset.UtcNow,
            Orders = new List<ExpectedOrderSnapshot>(),
            ExpectedPositionsByInstrument = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
        };

    private static ActualStateSnapshot EmptyActual() =>
        new ActualStateSnapshot
        {
            UpdatedUtc = DateTimeOffset.UtcNow,
            Orders = new List<OrderStateSnapshot>(),
        };

    // ── Clean states ──────────────────────────────────────────────────────────

    [Fact]
    public void Both_empty_is_a_match()
    {
        var report = Engine().Reconcile(EmptyExpected(), EmptyActual(), "test");
        Assert.True(report.IsMatch);
        Assert.Empty(report.Mismatches);
    }

    [Fact]
    public void Matching_positions_is_a_match()
    {
        var expected = EmptyExpected();
        expected.ExpectedPositionsByInstrument["MES 06-26"] = 1;

        var actual = EmptyActual();
        actual.Orders.Add(new OrderStateSnapshot
        {
            OrderId = "ORD-1",
            Instrument = "MES 06-26",
            Side = "Buy",
            Quantity = 1,
            FilledQuantity = 1,
            Status = "FullFill",
        });

        var report = Engine().Reconcile(expected, actual, "test");
        Assert.True(report.IsMatch);
    }

    // ── Position mismatches ───────────────────────────────────────────────────

    [Fact]
    public void Position_mismatch_is_detected()
    {
        var expected = EmptyExpected();
        expected.ExpectedPositionsByInstrument["MES 06-26"] = 1;

        // Actual state has no fills → position 0 vs expected 1.
        var report = Engine().Reconcile(expected, EmptyActual(), "test");
        Assert.False(report.IsMatch);
        Assert.Contains(report.Mismatches, m => m.Type == "Position" && m.Key == "MES 06-26");
    }

    [Fact]
    public void Short_position_mismatch_is_detected()
    {
        var expected = EmptyExpected();
        expected.ExpectedPositionsByInstrument["MES 06-26"] = -1;

        // Actual is flat.
        var report = Engine().Reconcile(expected, EmptyActual(), "test");
        Assert.False(report.IsMatch);
    }

    [Fact]
    public void Unexpected_actual_position_is_detected()
    {
        // Expected is flat but actual has a fill.
        var actual = EmptyActual();
        actual.Orders.Add(new OrderStateSnapshot
        {
            OrderId = "ORD-X",
            Instrument = "MES 06-26",
            Side = "Buy",
            Quantity = 1,
            FilledQuantity = 1,
            Status = "FullFill",
        });

        var report = Engine().Reconcile(EmptyExpected(), actual, "test");
        Assert.False(report.IsMatch);
        Assert.Contains(report.Mismatches, m => m.Type == "Position");
    }

    [Fact]
    public void Position_calculation_is_case_insensitive_for_instrument()
    {
        var expected = EmptyExpected();
        expected.ExpectedPositionsByInstrument["mes 06-26"] = 1;

        var actual = EmptyActual();
        actual.Orders.Add(new OrderStateSnapshot
        {
            OrderId = "ORD-1",
            Instrument = "MES 06-26",
            Side = "Buy",
            Quantity = 1,
            FilledQuantity = 1,
            Status = "FullFill",
        });

        var report = Engine().Reconcile(expected, actual, "test");
        Assert.True(report.IsMatch);
    }

    [Fact]
    public void Buy_and_sell_fills_net_to_zero()
    {
        var expected = EmptyExpected();
        // Expected net position: 0 (flat)
        expected.ExpectedPositionsByInstrument["MES 06-26"] = 0;

        var actual = EmptyActual();
        actual.Orders.Add(new OrderStateSnapshot
        {
            OrderId = "ORD-1", Instrument = "MES 06-26", Side = "Buy",
            Quantity = 1, FilledQuantity = 1, Status = "FullFill",
        });
        actual.Orders.Add(new OrderStateSnapshot
        {
            OrderId = "ORD-2", Instrument = "MES 06-26", Side = "Sell",
            Quantity = 1, FilledQuantity = 1, Status = "FullFill",
        });

        var report = Engine().Reconcile(expected, actual, "test");
        Assert.True(report.IsMatch);
    }

    // ── Working order mismatches ───────────────────────────────────────────────

    [Fact]
    public void Expected_working_order_missing_from_actual_is_detected()
    {
        var expected = EmptyExpected();
        expected.Orders.Add(new ExpectedOrderSnapshot
        {
            OrderId = "ORD-EXPECTED",
            SignalId = Guid.NewGuid().ToString("N"),
            Instrument = "MES 06-26",
            Side = "Buy",
            Quantity = 1,
            Status = "Working",
        });

        var report = Engine().Reconcile(expected, EmptyActual(), "test");
        Assert.False(report.IsMatch);
        Assert.Contains(report.Mismatches, m => m.Type == "WorkingOrder" && m.Key == "ORD-EXPECTED");
    }

    [Fact]
    public void Actual_working_order_not_in_expected_is_detected()
    {
        var actual = EmptyActual();
        actual.Orders.Add(new OrderStateSnapshot
        {
            OrderId = "ORD-GHOST",
            Instrument = "MES 06-26",
            Side = "Buy",
            Quantity = 1,
            FilledQuantity = 0,
            Status = "Accepted",
        });

        var report = Engine().Reconcile(EmptyExpected(), actual, "test");
        Assert.False(report.IsMatch);
        Assert.Contains(report.Mismatches, m => m.Type == "WorkingOrder" && m.Key == "ORD-GHOST");
    }

    [Fact]
    public void Matching_working_order_is_not_a_mismatch()
    {
        var orderId = "ORD-MATCH";

        var expected = EmptyExpected();
        expected.Orders.Add(new ExpectedOrderSnapshot
        {
            OrderId = orderId,
            SignalId = Guid.NewGuid().ToString("N"),
            Instrument = "MES 06-26",
            Side = "Buy",
            Quantity = 1,
            Status = "Working",
        });

        var actual = EmptyActual();
        actual.Orders.Add(new OrderStateSnapshot
        {
            OrderId = orderId, Instrument = "MES 06-26", Side = "Buy",
            Quantity = 1, FilledQuantity = 0, Status = "Accepted",
        });

        var report = Engine().Reconcile(expected, actual, "test");
        // Position: expected 0 (order not yet filled), actual 0 → match
        // Working order: both sides reference same orderId → match
        Assert.True(report.IsMatch);
    }

    // ── Trigger field ─────────────────────────────────────────────────────────

    [Fact]
    public void Report_captures_trigger_string()
    {
        var report = Engine().Reconcile(EmptyExpected(), EmptyActual(), "startup");
        Assert.Equal("startup", report.Trigger);
    }
}
