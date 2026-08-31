namespace PortfolioService.Features.Portfolios;

public sealed record PortfolioSnapshotResponse(
    string AccountId,
    DateOnly ValuationDate,
    string Currency,
    IReadOnlyList<PositionResponse> Positions,
    decimal TotalMarketValueUsd,
    decimal OverallValueUsd);

public sealed record PositionResponse(
    string Isin,
    string Symbol,
    decimal Quantity,
    decimal UnitCostUsd,
    decimal MarketPriceUsd,
    decimal MarketValueUsd,
    decimal UnrealizedProfitLossUsd);
