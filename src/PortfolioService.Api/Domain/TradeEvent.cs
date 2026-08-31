namespace PortfolioService.Domain;

public sealed class TradeEvent
{
    private TradeEvent()
    {
    }

    public long Id { get; private set; }

    public string ExternalReference { get; private set; } = string.Empty;

    public string AccountId { get; private set; } = string.Empty;

    public int InstrumentId { get; private set; }

    public Instrument Instrument { get; private set; } = null!;

    public TradeSide Side { get; private set; }

    public decimal Quantity { get; private set; }

    public decimal UnitPrice { get; private set; }

    public DateOnly TradeDate { get; private set; }

    public DateTime AsOfUtc { get; private set; }

    public DateTime ReceivedAtUtc { get; private set; }

    public TradeEventKind EventKind { get; private set; }

    public int VersionNumber { get; private set; }

    public static TradeEvent Create(
        string externalReference,
        string accountId,
        int instrumentId,
        TradeSide side,
        decimal quantity,
        decimal unitPrice,
        DateOnly tradeDate,
        DateTime asOfUtc,
        DateTime receivedAtUtc,
        TradeEventKind eventKind,
        int versionNumber)
    {
        return new TradeEvent
        {
            ExternalReference = externalReference,
            AccountId = accountId,
            InstrumentId = instrumentId,
            Side = side,
            Quantity = quantity,
            UnitPrice = unitPrice,
            TradeDate = tradeDate,
            AsOfUtc = asOfUtc,
            ReceivedAtUtc = receivedAtUtc,
            EventKind = eventKind,
            VersionNumber = versionNumber,
        };
    }
}
