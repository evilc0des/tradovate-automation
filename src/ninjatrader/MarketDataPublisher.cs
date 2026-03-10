using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NinjaTraderTradovateBridge;

public sealed class MarketDataPublisher
{
    private readonly IMarketDataTransport _transport;
    private readonly IBridgeLogger _logger;
    private readonly TimeSpan _quoteCoalesceInterval;

    private readonly Dictionary<string, DateTimeOffset> _lastQuoteSentByInstrument = new(StringComparer.OrdinalIgnoreCase);

    public MarketDataPublisher(IMarketDataTransport transport, IBridgeLogger logger, TimeSpan? quoteCoalesceInterval = null)
    {
        _transport = transport;
        _logger = logger;
        _quoteCoalesceInterval = quoteCoalesceInterval ?? TimeSpan.FromMilliseconds(100);
    }

    public async Task PublishQuoteAsync(QuoteEvent quoteEvent, CancellationToken cancellationToken)
    {
        if (ShouldCoalesceQuote(quoteEvent))
        {
            return;
        }

        var quote = MarketDataNormalizer.ToQuoteUpdateMessage(quoteEvent);
        var marketData = MarketDataNormalizer.ToMarketDataMessage(quoteEvent);

        await _transport.PublishAsync(quote, cancellationToken).ConfigureAwait(false);
        await _transport.PublishAsync(marketData, cancellationToken).ConfigureAwait(false);

        _lastQuoteSentByInstrument[quoteEvent.Instrument] = quoteEvent.Timestamp;
    }

    public Task PublishTradePrintAsync(TradePrintEvent tradeEvent, CancellationToken cancellationToken)
    {
        var message = MarketDataNormalizer.ToTradePrintMessage(tradeEvent);
        return _transport.PublishAsync(message, cancellationToken);
    }

    public Task PublishBarUpdateAsync(BarEvent barEvent, CancellationToken cancellationToken)
    {
        var message = MarketDataNormalizer.ToBarUpdateMessage(barEvent);
        return _transport.PublishAsync(message, cancellationToken);
    }

    public Task PublishConnectionStateAsync(ConnectionEvent connectionEvent, CancellationToken cancellationToken)
    {
        var message = MarketDataNormalizer.ToConnectionStateMessage(connectionEvent);
        return _transport.PublishAsync(message, cancellationToken);
    }

    public Task PublishInstrumentSessionMetadataAsync(InstrumentSessionMetadataEvent metadataEvent, CancellationToken cancellationToken)
    {
        var message = MarketDataNormalizer.ToInstrumentSessionMetadataMessage(metadataEvent);
        return _transport.PublishAsync(message, cancellationToken);
    }

    public void OnStarted()
    {
        _logger.Info("Market data publisher started.");
    }

    public void OnStopped()
    {
        _logger.Info("Market data publisher stopped.");
    }

    private bool ShouldCoalesceQuote(QuoteEvent quoteEvent)
    {
        if (!_lastQuoteSentByInstrument.TryGetValue(quoteEvent.Instrument, out var lastSent))
        {
            return false;
        }

        return quoteEvent.Timestamp - lastSent < _quoteCoalesceInterval;
    }
}
