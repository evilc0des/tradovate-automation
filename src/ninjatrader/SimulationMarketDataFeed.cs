using System;
using System.Threading;
using System.Threading.Tasks;

namespace NinjaTraderTradovateBridge;

public sealed class SimulationMarketDataFeed
{
    private readonly MarketDataPublisher _publisher;
    private readonly string _instrument;
    // Emit a simulated bar close every N ticks so EMA-based strategies have
    // enough bar history to form crossovers during local testing.
    private const int TicksPerBar = 10;

    public SimulationMarketDataFeed(MarketDataPublisher publisher, string instrument = "MES 06-26")
    {
        _publisher = publisher;
        _instrument = instrument;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _publisher.OnStarted();

        var price = 5000.00;
        var tickCount = 0;
        var barOpen = price;
        var barHigh = price;
        var barLow = price;
        var barStart = DateTimeOffset.UtcNow;

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

            tickCount++;
            if (price > barHigh) barHigh = price;
            if (price < barLow) barLow = price;

            if (tickCount >= TicksPerBar)
            {
                await _publisher.PublishBarUpdateAsync(
                    new BarEvent(now, _instrument, barStart, "1m", barOpen, barHigh, barLow, price, tickCount, Guid.NewGuid().ToString("N")),
                    cancellationToken).ConfigureAwait(false);

                // Reset bar accumulators.
                tickCount = 0;
                barOpen = price;
                barHigh = price;
                barLow = price;
                barStart = now;
            }

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            price += 0.25;
        }

        _publisher.OnStopped();
    }
}
