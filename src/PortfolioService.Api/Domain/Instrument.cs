namespace PortfolioService.Domain;

public sealed class Instrument
{
    private Instrument()
    {
    }

    public int Id { get; private set; }

    public string Isin { get; private set; } = string.Empty;

    public string Symbol { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string Currency { get; private set; } = "USD";
}
