using System;
using System.Collections.Generic;
using System.Threading.Channels;
using System.Threading;
using System.Threading.Tasks;

namespace NinjaTraderTradovateBridge;

public sealed class MarketDataPublisher
{
    private readonly IMarketDataTransport _transport;
    private readonly BridgeConfig _config;
    private readonly IBridgeLogger _logger;
    private readonly TimeSpan _quoteCoalesceInterval;
    private readonly Channel<object> _outboundQueue;
    private readonly CancellationTokenSource _publisherCts = new();
    private readonly Task _pumpTask;
    private CancellationTokenSource? _heartbeatCts;

    private readonly Dictionary<string, DateTimeOffset> _lastQuoteSentByInstrument = new(StringComparer.OrdinalIgnoreCase);

    public MarketDataPublisher(BridgeConfig config, IMarketDataTransport transport, IBridgeLogger logger, TimeSpan? quoteCoalesceInterval = null)
    {
        _config = config;
        _transport = transport;
        _logger = logger;
        _quoteCoalesceInterval = quoteCoalesceInterval ?? TimeSpan.FromMilliseconds(100);
        _outboundQueue = Channel.CreateBounded<object>(new BoundedChannelOptions(_config.MarketDataQueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
        });
        _pumpTask = Task.Run(() => PumpAsync(_publisherCts.Token));
    }

    public async Task PublishQuoteAsync(QuoteEvent quoteEvent, CancellationToken cancellationToken)
    {
        if (ShouldCoalesceQuote(quoteEvent))
        {
            return;
        }

        var quote = MarketDataNormalizer.ToQuoteUpdateMessage(quoteEvent);
        var marketData = MarketDataNormalizer.ToMarketDataMessage(quoteEvent);

        TryEnqueue(quote, isNonCritical: true);
        TryEnqueue(marketData, isNonCritical: true);

        _lastQuoteSentByInstrument[quoteEvent.Instrument] = quoteEvent.Timestamp;
        await Task.CompletedTask;
    }

    public Task PublishTradePrintAsync(TradePrintEvent tradeEvent, CancellationToken cancellationToken)
    {
        var message = MarketDataNormalizer.ToTradePrintMessage(tradeEvent);
        TryEnqueue(message, isNonCritical: true);
        return Task.CompletedTask;
    }

    public Task PublishBarUpdateAsync(BarEvent barEvent, CancellationToken cancellationToken)
    {
        var message = MarketDataNormalizer.ToBarUpdateMessage(barEvent);
        TryEnqueue(message, isNonCritical: true);
        return Task.CompletedTask;
    }

    public Task PublishConnectionStateAsync(ConnectionEvent connectionEvent, CancellationToken cancellationToken)
    {
        var message = MarketDataNormalizer.ToConnectionStateMessage(connectionEvent);
        return EnqueueCriticalAsync(message, cancellationToken);
    }

    public Task PublishInstrumentSessionMetadataAsync(InstrumentSessionMetadataEvent metadataEvent, CancellationToken cancellationToken)
    {
        var message = MarketDataNormalizer.ToInstrumentSessionMetadataMessage(metadataEvent);
        return EnqueueCriticalAsync(message, cancellationToken);
    }

    public void OnStarted()
    {
        _logger.Info("Market data publisher started.");
        _heartbeatCts?.Cancel();
        _heartbeatCts = new CancellationTokenSource();
        _ = Task.Run(() => HeartbeatLoopAsync(_heartbeatCts.Token));
    }

    public void OnStopped()
    {
        _heartbeatCts?.Cancel();
        _logger.Info("Market data publisher stopped.");
    }

    private async Task EnqueueCriticalAsync(object message, CancellationToken cancellationToken)
    {
        await _outboundQueue.Writer.WriteAsync(message, cancellationToken).ConfigureAwait(false);
    }

    private void TryEnqueue(object message, bool isNonCritical)
    {
        if (_outboundQueue.Writer.TryWrite(message))
        {
            return;
        }

        if (isNonCritical)
        {
            _logger.Warn("Dropped non-critical market data message due to bounded queue pressure.");
            return;
        }

        _logger.Warn("Failed to enqueue critical market data message under queue pressure.");
    }

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var message = await _outboundQueue.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                await _transport.PublishAsync(message, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.Error("Market data outbound pump crashed.", ex);
        }
    }

    private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TryEnqueue(new HeartbeatMessage(), isNonCritical: true);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_config.HeartbeatIntervalSeconds), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
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
