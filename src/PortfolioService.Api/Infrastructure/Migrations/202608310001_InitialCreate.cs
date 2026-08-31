using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1861 // Migration metadata intentionally mirrors EF-generated array arguments.

namespace PortfolioService.Infrastructure.Migrations;

[DbContext(typeof(PortfolioDbContext))]
[Migration("202608310001_InitialCreate")]
public sealed class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "portfolio");

        migrationBuilder.CreateTable(
            name: "Instruments",
            schema: "portfolio",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Isin = table.Column<string>(type: "varchar(12)", unicode: false, maxLength: 12, nullable: false),
                Symbol = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                Currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_Instruments", x => x.Id));

        migrationBuilder.CreateTable(
            name: "MarketPrices",
            schema: "portfolio",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                InstrumentId = table.Column<int>(type: "int", nullable: false),
                PriceDate = table.Column<DateOnly>(type: "date", nullable: false),
                ClosePrice = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                Currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MarketPrices", x => x.Id);
                table.ForeignKey(
                    name: "FK_MarketPrices_Instruments_InstrumentId",
                    column: x => x.InstrumentId,
                    principalSchema: "portfolio",
                    principalTable: "Instruments",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "TradeEvents",
            schema: "portfolio",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ExternalReference = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                AccountId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                InstrumentId = table.Column<int>(type: "int", nullable: false),
                Side = table.Column<int>(type: "int", nullable: false),
                Quantity = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                UnitPrice = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                TradeDate = table.Column<DateOnly>(type: "date", nullable: false),
                AsOfUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                ReceivedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                EventKind = table.Column<int>(type: "int", nullable: false),
                VersionNumber = table.Column<int>(type: "int", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TradeEvents", x => x.Id);
                table.ForeignKey(
                    name: "FK_TradeEvents_Instruments_InstrumentId",
                    column: x => x.InstrumentId,
                    principalSchema: "portfolio",
                    principalTable: "Instruments",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.InsertData(
            schema: "portfolio",
            table: "Instruments",
            columns: ["Id", "Currency", "Isin", "Name", "Symbol"],
            columnTypes: ["int", "char(3)", "varchar(12)", "nvarchar(128)", "varchar(16)"],
            values: new object[] { 1, "USD", "US0378331005", "Apple Inc.", "AAPL" });

        migrationBuilder.InsertData(
            schema: "portfolio",
            table: "MarketPrices",
            columns: ["Id", "ClosePrice", "Currency", "InstrumentId", "PriceDate"],
            columnTypes: ["bigint", "decimal(19,6)", "char(3)", "int", "date"],
            values: new object[,]
            {
                { 1L, 186.000000m, "USD", 1, new DateOnly(2025, 3, 1) },
                { 2L, 190.000000m, "USD", 1, new DateOnly(2025, 3, 2) },
            });

        migrationBuilder.CreateIndex(
            name: "UX_Instruments_Isin",
            schema: "portfolio",
            table: "Instruments",
            column: "Isin",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "UX_Instruments_Symbol",
            schema: "portfolio",
            table: "Instruments",
            column: "Symbol",
            unique: true);

        migrationBuilder.CreateIndex(
                name: "UX_MarketPrices_Instrument_PriceDate",
                schema: "portfolio",
                table: "MarketPrices",
                columns: ["InstrumentId", "PriceDate"],
                unique: true,
                descending: [false, true])
            .Annotation("SqlServer:Include", new[] { "ClosePrice", "Currency" });

        migrationBuilder.CreateIndex(
            name: "IX_TradeEvents_InstrumentId",
            schema: "portfolio",
            table: "TradeEvents",
            column: "InstrumentId");

        migrationBuilder.CreateIndex(
                name: "IX_TradeEvents_Account_ExternalReference_AsOfUtc",
                schema: "portfolio",
                table: "TradeEvents",
                columns: ["AccountId", "ExternalReference", "AsOfUtc"],
                descending: [false, false, true])
            .Annotation(
                "SqlServer:Include",
                new[] { "TradeDate", "InstrumentId", "Side", "Quantity", "UnitPrice" });

        migrationBuilder.CreateIndex(
            name: "UX_TradeEvents_ExternalReference_AsOfUtc",
            schema: "portfolio",
            table: "TradeEvents",
            columns: ["ExternalReference", "AsOfUtc"],
            unique: true,
            descending: [false, true]);

        migrationBuilder.CreateIndex(
            name: "UX_TradeEvents_ExternalReference_VersionNumber",
            schema: "portfolio",
            table: "TradeEvents",
            columns: ["ExternalReference", "VersionNumber"],
            unique: true);

        migrationBuilder.Sql(PortfolioSnapshotProcedureSql);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP PROCEDURE IF EXISTS [portfolio].[GetPortfolioSnapshot];");
        migrationBuilder.DropTable(name: "MarketPrices", schema: "portfolio");
        migrationBuilder.DropTable(name: "TradeEvents", schema: "portfolio");
        migrationBuilder.DropTable(name: "Instruments", schema: "portfolio");
    }

    private const string PortfolioSnapshotProcedureSql = """
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

            DECLARE @AsOfExclusiveUtc datetime2(7) = DATEADD(day, 1, CONVERT(datetime2(7), @ValuationDate));

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
                    CASE WHEN position.BuyQuantity = 0 THEN 0
                         ELSE position.TotalBuyCost / position.BuyQuantity END
                    AS decimal(19, 6)
                ) AS UnitCostUsd,
                price.ClosePrice AS MarketPriceUsd,
                price.Currency AS PriceCurrency,
                CAST(position.Quantity * price.ClosePrice AS decimal(19, 6)) AS MarketValueUsd,
                CAST
                (
                    position.Quantity
                        * (price.ClosePrice
                            - CASE WHEN position.BuyQuantity = 0 THEN 0
                                   ELSE position.TotalBuyCost / position.BuyQuantity END)
                    AS decimal(19, 6)
                ) AS UnrealizedProfitLossUsd
            INTO #Positions
            FROM AggregatedPositions AS position
            INNER JOIN [portfolio].[Instruments] AS instrument ON instrument.Id = position.InstrumentId
            OUTER APPLY
            (
                SELECT TOP (1) marketPrice.ClosePrice, marketPrice.Currency
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
                SELECT 1 FROM #Positions
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
        """;
}

#pragma warning restore CA1861
