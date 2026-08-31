using Microsoft.EntityFrameworkCore;
using PortfolioService.Common;
using PortfolioService.Domain;
using PortfolioService.Infrastructure;

namespace PortfolioService.Features.Portfolios;

public interface IPortfolioSnapshotService
{
    Task<PortfolioSnapshotResponse> GetSnapshotAsync(
        string accountId,
        DateOnly valuationDate,
        CancellationToken cancellationToken);
}

public sealed partial class PortfolioSnapshotService(
    PortfolioDbContext dbContext,
    ILogger<PortfolioSnapshotService> logger) : IPortfolioSnapshotService
{
    public async Task<PortfolioSnapshotResponse> GetSnapshotAsync(
        string accountId,
        DateOnly valuationDate,
        CancellationToken cancellationToken)
    {
        var normalizedAccountId = accountId.Trim();
        var asOfExclusiveUtc = DateTime.SpecifyKind(
            valuationDate.AddDays(1).ToDateTime(TimeOnly.MinValue),
            DateTimeKind.Utc);

        var eligibleEvents = dbContext.TradeEvents
            .AsNoTracking()
            .Where(x => x.AccountId == normalizedAccountId
                && x.TradeDate <= valuationDate
                && x.AsOfUtc < asOfExclusiveUtc);

        var latestVersions = eligibleEvents
            .GroupBy(x => x.ExternalReference)
            .Select(group => new
            {
                ExternalReference = group.Key,
                AsOfUtc = group.Max(x => x.AsOfUtc),
            });

        var currentEvents =
            from tradeEvent in eligibleEvents
            join latest in latestVersions
                on new { tradeEvent.ExternalReference, tradeEvent.AsOfUtc }
                equals new { latest.ExternalReference, latest.AsOfUtc }
            select tradeEvent;

        var aggregates = await currentEvents
            .GroupBy(x => x.InstrumentId)
            .Select(group => new PositionAggregate(
                group.Key,
                group.Sum(x => x.Side == TradeSide.Buy ? x.Quantity : -x.Quantity),
                group.Sum(x => x.Side == TradeSide.Buy ? x.Quantity : 0m),
                group.Sum(x => x.Side == TradeSide.Buy ? x.Quantity * x.UnitPrice : 0m)))
            .ToListAsync(cancellationToken);
        aggregates = aggregates.Where(x => x.NetQuantity != 0m).ToList();

        if (aggregates.Count == 0)
        {
            LogEmptySnapshot(
                logger,
                normalizedAccountId,
                valuationDate);
            return new PortfolioSnapshotResponse(
                normalizedAccountId,
                valuationDate,
                "USD",
                [],
                0m,
                0m);
        }

        var instrumentIds = aggregates.Select(x => x.InstrumentId).ToArray();
        var instruments = await dbContext.Instruments
            .AsNoTracking()
            .Where(x => instrumentIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var eligiblePrices = dbContext.MarketPrices
            .AsNoTracking()
            .Where(x => instrumentIds.Contains(x.InstrumentId) && x.PriceDate <= valuationDate);
        var latestPriceDates = eligiblePrices
            .GroupBy(x => x.InstrumentId)
            .Select(group => new
            {
                InstrumentId = group.Key,
                PriceDate = group.Max(x => x.PriceDate),
            });
        var latestPrices = await (
                from price in eligiblePrices
                join latest in latestPriceDates
                    on new { price.InstrumentId, price.PriceDate }
                    equals new { latest.InstrumentId, latest.PriceDate }
                select price)
            .ToDictionaryAsync(x => x.InstrumentId, cancellationToken);

        var positions = new List<PositionResponse>(aggregates.Count);
        foreach (var aggregate in aggregates)
        {
            var instrument = instruments[aggregate.InstrumentId];
            if (!string.Equals(instrument.Currency, "USD", StringComparison.OrdinalIgnoreCase))
            {
                throw new ApiException(
                    StatusCodes.Status422UnprocessableEntity,
                    "unsupported_currency",
                    $"Instrument '{instrument.Symbol}' is not USD-denominated and FX conversion is not enabled.");
            }

            if (!latestPrices.TryGetValue(aggregate.InstrumentId, out var marketPrice))
            {
                throw new ApiException(
                    StatusCodes.Status422UnprocessableEntity,
                    "missing_market_price",
                    $"No market price exists for '{instrument.Symbol}' on or before {valuationDate:yyyy-MM-dd}.");
            }

            var unitCost = aggregate.BuyQuantity == 0m
                ? 0m
                : aggregate.TotalBuyCost / aggregate.BuyQuantity;
            var marketValue = aggregate.NetQuantity * marketPrice.ClosePrice;
            var unrealizedProfitLoss = aggregate.NetQuantity * (marketPrice.ClosePrice - unitCost);

            positions.Add(new PositionResponse(
                instrument.Isin,
                instrument.Symbol,
                aggregate.NetQuantity,
                RoundMoney(unitCost),
                RoundMoney(marketPrice.ClosePrice),
                RoundMoney(marketValue),
                RoundMoney(unrealizedProfitLoss)));
        }

        positions.Sort((left, right) => string.Compare(left.Symbol, right.Symbol, StringComparison.Ordinal));
        var totalMarketValue = positions.Sum(x => x.MarketValueUsd);

        LogSnapshotProduced(
            logger,
            normalizedAccountId,
            valuationDate,
            positions.Count,
            totalMarketValue);
        return new PortfolioSnapshotResponse(
            normalizedAccountId,
            valuationDate,
            "USD",
            positions,
            totalMarketValue,
            totalMarketValue);
    }

    private static decimal RoundMoney(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private sealed record PositionAggregate(
        int InstrumentId,
        decimal NetQuantity,
        decimal BuyQuantity,
        decimal TotalBuyCost);

    [LoggerMessage(
        EventId = 3000,
        Level = LogLevel.Information,
        Message = "Produced empty portfolio snapshot for account {AccountId} on {ValuationDate}")]
    private static partial void LogEmptySnapshot(
        ILogger logger,
        string accountId,
        DateOnly valuationDate);

    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Information,
        Message = "Produced portfolio snapshot for account {AccountId} on {ValuationDate} with {PositionCount} positions and total value {TotalMarketValueUsd} USD")]
    private static partial void LogSnapshotProduced(
        ILogger logger,
        string accountId,
        DateOnly valuationDate,
        int positionCount,
        decimal totalMarketValueUsd);
}
