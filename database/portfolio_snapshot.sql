/*
Task 2 artifact: portfolio.GetPortfolioSnapshot

Assumptions:
- Accepted trade events are immutable versions. The latest AsOfUtc version known by
  the end of @ValuationDate is effective for each ExternalReference.
- All seeded instruments and prices are USD. FX and cash are intentionally excluded.
- Unit cost is weighted average acquisition cost from BUY events. SELL events reduce
  quantity but do not change that average. Short-position cost basis is not modeled.
- Monetary values are retained at decimal(19,6) here and rounded by API responses.
*/

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'UX_TradeEvents_ExternalReference_AsOfUtc'
      AND object_id = OBJECT_ID(N'[portfolio].[TradeEvents]')
)
BEGIN
    CREATE UNIQUE INDEX [UX_TradeEvents_ExternalReference_AsOfUtc]
        ON [portfolio].[TradeEvents] ([ExternalReference], [AsOfUtc] DESC);
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_TradeEvents_Account_ExternalReference_AsOfUtc'
      AND object_id = OBJECT_ID(N'[portfolio].[TradeEvents]')
)
BEGIN
    CREATE INDEX [IX_TradeEvents_Account_ExternalReference_AsOfUtc]
        ON [portfolio].[TradeEvents] ([AccountId], [ExternalReference], [AsOfUtc] DESC)
        INCLUDE ([TradeDate], [InstrumentId], [Side], [Quantity], [UnitPrice]);
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'UX_MarketPrices_Instrument_PriceDate'
      AND object_id = OBJECT_ID(N'[portfolio].[MarketPrices]')
)
BEGIN
    CREATE UNIQUE INDEX [UX_MarketPrices_Instrument_PriceDate]
        ON [portfolio].[MarketPrices] ([InstrumentId], [PriceDate] DESC)
        INCLUDE ([ClosePrice], [Currency]);
END;
GO

CREATE OR ALTER PROCEDURE [portfolio].[GetPortfolioSnapshot]
    @AccountId nvarchar(64),
    @ValuationDate date
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NULLIF(LTRIM(RTRIM(@AccountId)), N'') IS NULL
        THROW 51000, 'AccountId is required.', 1;

    IF @ValuationDate IS NULL
        THROW 51001, 'ValuationDate is required.', 1;

    DECLARE @AsOfExclusiveUtc datetime2(7) =
        DATEADD(day, 1, CONVERT(datetime2(7), @ValuationDate));

    ;WITH RankedEvents AS
    (
        SELECT
            tradeEvent.Id,
            tradeEvent.ExternalReference,
            tradeEvent.InstrumentId,
            tradeEvent.Side,
            tradeEvent.Quantity,
            tradeEvent.UnitPrice,
            ROW_NUMBER() OVER
            (
                PARTITION BY tradeEvent.ExternalReference
                ORDER BY tradeEvent.AsOfUtc DESC, tradeEvent.Id DESC
            ) AS VersionRank
        FROM [portfolio].[TradeEvents] AS tradeEvent
        WHERE tradeEvent.AccountId = @AccountId
          AND tradeEvent.TradeDate <= @ValuationDate
          AND tradeEvent.AsOfUtc < @AsOfExclusiveUtc
    ),
    CurrentEvents AS
    (
        SELECT InstrumentId, Side, Quantity, UnitPrice
        FROM RankedEvents
        WHERE VersionRank = 1
    ),
    AggregatedPositions AS
    (
        SELECT
            InstrumentId,
            SUM(CASE WHEN Side = 1 THEN Quantity ELSE -Quantity END) AS Quantity,
            SUM(CASE WHEN Side = 1 THEN Quantity ELSE 0 END) AS BuyQuantity,
            SUM(CASE WHEN Side = 1 THEN Quantity * UnitPrice ELSE 0 END) AS TotalBuyCost
        FROM CurrentEvents
        GROUP BY InstrumentId
    )
    SELECT
        instrument.Isin,
        instrument.Symbol,
        instrument.Currency AS InstrumentCurrency,
        CAST(position.Quantity AS decimal(19, 6)) AS Quantity,
        CAST
        (
            CASE
                WHEN position.BuyQuantity = 0 THEN 0
                ELSE position.TotalBuyCost / position.BuyQuantity
            END
            AS decimal(19, 6)
        ) AS UnitCostUsd,
        price.ClosePrice AS MarketPriceUsd,
        price.Currency AS PriceCurrency,
        CAST(position.Quantity * price.ClosePrice AS decimal(19, 6)) AS MarketValueUsd,
        CAST
        (
            position.Quantity
                * (price.ClosePrice
                    - CASE
                        WHEN position.BuyQuantity = 0 THEN 0
                        ELSE position.TotalBuyCost / position.BuyQuantity
                      END)
            AS decimal(19, 6)
        ) AS UnrealizedProfitLossUsd
    INTO #Positions
    FROM AggregatedPositions AS position
    INNER JOIN [portfolio].[Instruments] AS instrument
        ON instrument.Id = position.InstrumentId
    OUTER APPLY
    (
        SELECT TOP (1)
            marketPrice.ClosePrice,
            marketPrice.Currency
        FROM [portfolio].[MarketPrices] AS marketPrice
        WHERE marketPrice.InstrumentId = position.InstrumentId
          AND marketPrice.PriceDate <= @ValuationDate
        ORDER BY marketPrice.PriceDate DESC
    ) AS price
    WHERE position.Quantity <> 0;

    IF EXISTS (SELECT 1 FROM #Positions WHERE MarketPriceUsd IS NULL)
        THROW 51002, 'A market price is missing on or before the valuation date.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM #Positions
        WHERE InstrumentCurrency <> 'USD' OR PriceCurrency <> 'USD'
    )
        THROW 51003, 'Only USD instruments and prices are supported.', 1;

    SELECT
        Isin,
        Symbol,
        Quantity,
        UnitCostUsd,
        MarketPriceUsd,
        MarketValueUsd,
        UnrealizedProfitLossUsd
    FROM #Positions
    ORDER BY Symbol;

    SELECT
        @AccountId AS AccountId,
        @ValuationDate AS ValuationDate,
        CAST(COALESCE(SUM(MarketValueUsd), 0) AS decimal(19, 6)) AS TotalMarketValueUsd,
        CAST(COALESCE(SUM(MarketValueUsd), 0) AS decimal(19, 6)) AS OverallValueUsd
    FROM #Positions;
END;
GO
