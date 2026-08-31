namespace PortfolioService.Domain;

public sealed class MarketPrice
{
    private MarketPrice()
    {
    }

    public long Id { get; private set; }

    public int InstrumentId { get; private set; }

    public Instrument Instrument { get; private set; } = null!;

    public DateOnly PriceDate { get; private set; }

    public decimal ClosePrice { get; private set; }

    public string Currency { get; private set; } = "USD";
}
