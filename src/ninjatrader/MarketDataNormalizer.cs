using System;

namespace NinjaTraderTradovateBridge;

public static class MarketDataNormalizer
{
    public static QuoteUpdateMessage ToQuoteUpdateMessage(QuoteEvent quoteEvent)
    {
        return new QuoteUpdateMessage
        {
            Timestamp = quoteEvent.Timestamp,
            Instrument = quoteEvent.Instrument,
            Bid = quoteEvent.Bid,
            Ask = quoteEvent.Ask,
            BidSize = quoteEvent.BidSize,
            AskSize = quoteEvent.AskSize,
            CorrelationId = quoteEvent.CorrelationId,
        };
    }

    public static TradePrintMessage ToTradePrintMessage(TradePrintEvent tradeEvent)
    {
        return new TradePrintMessage
        {
            Timestamp = tradeEvent.Timestamp,
            Instrument = tradeEvent.Instrument,
            Price = tradeEvent.Price,
            Size = tradeEvent.Size,
            AggressorSide = tradeEvent.AggressorSide,
            CorrelationId = tradeEvent.CorrelationId,
        };
    }

    public static BarUpdateMessage ToBarUpdateMessage(BarEvent barEvent)
    {
        return new BarUpdateMessage
        {
            Timestamp = barEvent.Timestamp,
            Instrument = barEvent.Instrument,
            BarTime = barEvent.BarTime,
            Interval = barEvent.Interval,
            Open = barEvent.Open,
            High = barEvent.High,
            Low = barEvent.Low,
            Close = barEvent.Close,
            Volume = barEvent.Volume,
            CorrelationId = barEvent.CorrelationId,
        };
    }

    public static ConnectionStateMessage ToConnectionStateMessage(ConnectionEvent connectionEvent)
    {
        return new ConnectionStateMessage
        {
            Timestamp = connectionEvent.Timestamp,
            Provider = connectionEvent.Provider,
            State = connectionEvent.State,
            Details = connectionEvent.Details,
            CorrelationId = connectionEvent.CorrelationId,
        };
    }

    public static MarketDataMessage ToMarketDataMessage(QuoteEvent quoteEvent, double? lastPrice = null, long? lastSize = null)
    {
        return new MarketDataMessage
        {
            Timestamp = quoteEvent.Timestamp,
            Instrument = quoteEvent.Instrument,
            EventType = "QuoteUpdate",
            Bid = quoteEvent.Bid,
            Ask = quoteEvent.Ask,
            LastPrice = lastPrice,
            LastSize = lastSize,
            CorrelationId = quoteEvent.CorrelationId,
        };
    }

    public static InstrumentSessionMetadataMessage ToInstrumentSessionMetadataMessage(InstrumentSessionMetadataEvent metadataEvent)
    {
        return new InstrumentSessionMetadataMessage
        {
            Timestamp = metadataEvent.Timestamp,
            Instrument = metadataEvent.Instrument,
            SessionId = metadataEvent.SessionId,
            SessionStart = metadataEvent.SessionStart,
            SessionEnd = metadataEvent.SessionEnd,
            TickSize = metadataEvent.TickSize,
            PointValue = metadataEvent.PointValue,
            CorrelationId = metadataEvent.CorrelationId,
        };
    }
}
