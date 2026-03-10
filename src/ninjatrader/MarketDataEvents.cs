using System;

namespace NinjaTraderTradovateBridge;

// These event records keep Phase 3 decoupled from direct NinjaTrader type references.
// A thin adapter can map NinjaTrader callbacks into these records.
public sealed record QuoteEvent(
    DateTimeOffset Timestamp,
    string Instrument,
    double Bid,
    double Ask,
    int BidSize,
    int AskSize,
    string CorrelationId);

public sealed record TradePrintEvent(
    DateTimeOffset Timestamp,
    string Instrument,
    double Price,
    int Size,
    string AggressorSide,
    string CorrelationId);

public sealed record BarEvent(
    DateTimeOffset Timestamp,
    string Instrument,
    DateTimeOffset BarTime,
    string Interval,
    double Open,
    double High,
    double Low,
    double Close,
    long Volume,
    string CorrelationId);

public sealed record ConnectionEvent(
    DateTimeOffset Timestamp,
    string Provider,
    string State,
    string? Details,
    string CorrelationId);

public sealed record InstrumentSessionMetadataEvent(
    DateTimeOffset Timestamp,
    string Instrument,
    string SessionId,
    DateTimeOffset SessionStart,
    DateTimeOffset SessionEnd,
    double TickSize,
    double PointValue,
    string CorrelationId);
