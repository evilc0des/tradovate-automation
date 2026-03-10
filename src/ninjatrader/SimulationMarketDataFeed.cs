using System;
using System.Threading;
using System.Threading.Tasks;

namespace NinjaTraderTradovateBridge;

public sealed class SimulationMarketDataFeed
{
    private readonly MarketDataPublisher _publisher;
    private readonly string _instrument;

    public SimulationMarketDataFeed(MarketDataPublisher publisher, string instrument = "MES 06-26")
    {
        _publisher = publisher;
        _instrument = instrument;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _publisher.OnStarted();

        var price = 5000.00;
        while (!cancellationToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            var bid = price - 0.25;
            var ask = price + 0.25;

            await _publisher.PublishQuoteAsync(
                new QuoteEvent(now, _instrument, bid, ask, 10, 10, Guid.NewGuid().ToString("N")),
                cancellationToken).ConfigureAwait(false);

            await _publisher.PublishTradePrintAsync(
                new TradePrintEvent(now, _instrument, price, 1, "Buy", Guid.NewGuid().ToString("N")),
                cancellationToken).ConfigureAwait(false);

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            price += 0.25;
        }

        _publisher.OnStopped();
    }
}
