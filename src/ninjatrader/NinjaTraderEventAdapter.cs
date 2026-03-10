using System;
using System.Threading;
using System.Threading.Tasks;

namespace NinjaTraderTradovateBridge;

// This adapter is the integration seam for NinjaTrader callbacks.
// Wire OnQuote/OnTrade/OnBar/OnConnection methods from NinjaScript into these methods.
public sealed class NinjaTraderEventAdapter
{
    private readonly MarketDataPublisher _publisher;

    public NinjaTraderEventAdapter(MarketDataPublisher publisher)
    {
        _publisher = publisher;
    }

    public Task OnQuoteAsync(
        DateTimeOffset timestamp,
        string instrument,
        double bid,
        double ask,
        int bidSize,
        int askSize,
        CancellationToken cancellationToken)
    {
        return _publisher.PublishQuoteAsync(
            new QuoteEvent(timestamp, instrument, bid, ask, bidSize, askSize, Guid.NewGuid().ToString("N")),
            cancellationToken);
    }

    public Task OnTradePrintAsync(
        DateTimeOffset timestamp,
        string instrument,
        double price,
        int size,
        string aggressorSide,
        CancellationToken cancellationToken)
    {
        return _publisher.PublishTradePrintAsync(
            new TradePrintEvent(timestamp, instrument, price, size, aggressorSide, Guid.NewGuid().ToString("N")),
            cancellationToken);
    }

    public Task OnBarAsync(
        DateTimeOffset timestamp,
        string instrument,
        DateTimeOffset barTime,
        string interval,
        double open,
        double high,
        double low,
        double close,
        long volume,
        CancellationToken cancellationToken)
    {
        return _publisher.PublishBarUpdateAsync(
            new BarEvent(timestamp, instrument, barTime, interval, open, high, low, close, volume, Guid.NewGuid().ToString("N")),
            cancellationToken);
    }

    public Task OnConnectionStateAsync(
        DateTimeOffset timestamp,
        string provider,
        string state,
        string? details,
        CancellationToken cancellationToken)
    {
        return _publisher.PublishConnectionStateAsync(
            new ConnectionEvent(timestamp, provider, state, details, Guid.NewGuid().ToString("N")),
            cancellationToken);
    }

    public Task OnInstrumentSessionMetadataAsync(
        DateTimeOffset timestamp,
        string instrument,
        string sessionId,
        DateTimeOffset sessionStart,
        DateTimeOffset sessionEnd,
        double tickSize,
        double pointValue,
        CancellationToken cancellationToken)
    {
        return _publisher.PublishInstrumentSessionMetadataAsync(
            new InstrumentSessionMetadataEvent(
                timestamp,
                instrument,
                sessionId,
                sessionStart,
                sessionEnd,
                tickSize,
                pointValue,
                Guid.NewGuid().ToString("N")),
            cancellationToken);
    }
}
