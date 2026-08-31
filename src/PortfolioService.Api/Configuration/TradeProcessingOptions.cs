namespace PortfolioService.Configuration;

public sealed class TradeProcessingOptions
{
    public const string SectionName = "Features";

    public bool CorrectionProcessingEnabled { get; init; } = true;
}
