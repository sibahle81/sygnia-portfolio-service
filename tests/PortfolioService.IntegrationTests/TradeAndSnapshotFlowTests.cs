using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PortfolioService.Infrastructure;

namespace PortfolioService.IntegrationTests;

public sealed class TradeAndSnapshotFlowTests(PortfolioApiFactory factory) : IClassFixture<PortfolioApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public void MigrationSnapshotMatchesRuntimeModel()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        Assert.False(dbContext.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task RootRedirectsToInteractiveOpenApiDocumentation()
    {
        using var noRedirectClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        using var rootResponse = await noRedirectClient.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, rootResponse.StatusCode);
        Assert.Equal("/swagger/index.html", rootResponse.Headers.Location?.OriginalString);

        using var uiResponse = await _client.GetAsync("/swagger/index.html");
        uiResponse.EnsureSuccessStatusCode();
        var ui = await uiResponse.Content.ReadAsStringAsync();
        Assert.Contains("Sygnia Portfolio Service API", ui, StringComparison.Ordinal);

        using var documentResponse = await _client.GetAsync("/swagger/v1/swagger.json");
        documentResponse.EnsureSuccessStatusCode();
        using var document = await ReadJsonAsync(documentResponse);
        var paths = document.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/v1/trades", out _));
        Assert.True(paths.TryGetProperty(
            "/api/v1/portfolios/{accountId}/snapshots/{valuationDate}",
            out _));
    }

    [Fact]
    public async Task InitialDuplicateAndCorrectionProduceAuditedCorrectSnapshot()
    {
        var externalReference = $"FLOW-{Guid.NewGuid():N}";
        var accountId = $"ACCOUNT-{Guid.NewGuid():N}";
        var initial = CreateTrade(externalReference, accountId, 120m, 185.40m, "2025-03-01T10:15:00Z");

        using var acceptedResponse = await _client.PostAsJsonAsync("/api/v1/trades", initial);
        Assert.Equal(HttpStatusCode.Created, acceptedResponse.StatusCode);
        Assert.True(acceptedResponse.Headers.Contains("X-Correlation-ID"));
        using var acceptedJson = await ReadJsonAsync(acceptedResponse);
        Assert.Equal("accepted", acceptedJson.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(1, acceptedJson.RootElement.GetProperty("current_version").GetInt32());

        using var duplicateResponse = await _client.PostAsJsonAsync("/api/v1/trades", initial);
        Assert.Equal(HttpStatusCode.OK, duplicateResponse.StatusCode);
        using var duplicateJson = await ReadJsonAsync(duplicateResponse);
        Assert.Equal("duplicate", duplicateJson.RootElement.GetProperty("outcome").GetString());

        var correction = CreateTrade(externalReference, accountId, 100m, 184.00m, "2025-03-01T11:00:00Z");
        using var correctionResponse = await _client.PostAsJsonAsync("/api/v1/trades", correction);
        Assert.Equal(HttpStatusCode.Created, correctionResponse.StatusCode);
        using var correctionJson = await ReadJsonAsync(correctionResponse);
        Assert.Equal("corrected", correctionJson.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(2, correctionJson.RootElement.GetProperty("current_version").GetInt32());

        using var auditResponse = await _client.GetAsync($"/api/v1/trades/{externalReference}/events");
        auditResponse.EnsureSuccessStatusCode();
        using var auditJson = await ReadJsonAsync(auditResponse);
        Assert.Equal(2, auditJson.RootElement.GetArrayLength());
        Assert.Equal("initial", auditJson.RootElement[0].GetProperty("event_kind").GetString());
        Assert.Equal("correction", auditJson.RootElement[1].GetProperty("event_kind").GetString());

        using var snapshotResponse = await _client.GetAsync($"/api/v1/portfolios/{accountId}/snapshots/2025-03-01");
        snapshotResponse.EnsureSuccessStatusCode();
        using var snapshotJson = await ReadJsonAsync(snapshotResponse);
        var root = snapshotJson.RootElement;
        var position = root.GetProperty("positions")[0];
        Assert.Equal("AAPL", position.GetProperty("symbol").GetString());
        Assert.Equal(100m, position.GetProperty("quantity").GetDecimal());
        Assert.Equal(184.00m, position.GetProperty("unit_cost_usd").GetDecimal());
        Assert.Equal(186.00m, position.GetProperty("market_price_usd").GetDecimal());
        Assert.Equal(18_600.00m, position.GetProperty("market_value_usd").GetDecimal());
        Assert.Equal(200.00m, position.GetProperty("unrealized_profit_loss_usd").GetDecimal());
        Assert.Equal(18_600.00m, root.GetProperty("total_market_value_usd").GetDecimal());
        Assert.Equal(18_600.00m, root.GetProperty("overall_value_usd").GetDecimal());
    }

    [Fact]
    public async Task ConcurrentResendsCreateExactlyOneAcceptedEvent()
    {
        var externalReference = $"RACE-{Guid.NewGuid():N}";
        var accountId = $"ACCOUNT-{Guid.NewGuid():N}";
        var trade = CreateTrade(externalReference, accountId, 10m, 185.40m, "2025-03-01T12:00:00Z");

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => _client.PostAsJsonAsync("/api/v1/trades", trade)));

        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
            Assert.Equal(7, responses.Count(response => response.StatusCode == HttpStatusCode.OK));

            using var auditResponse = await _client.GetAsync($"/api/v1/trades/{externalReference}/events");
            auditResponse.EnsureSuccessStatusCode();
            using var auditJson = await ReadJsonAsync(auditResponse);
            Assert.Equal(1, auditJson.RootElement.GetArrayLength());
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task StoredProcedureAppliesAsOfCutoffAndLatestCorrection()
    {
        var externalReference = $"SQL-{Guid.NewGuid():N}";
        var accountId = $"ACCOUNT-{Guid.NewGuid():N}";
        var initial = CreateTrade(externalReference, accountId, 120m, 185.40m, "2025-03-01T10:15:00Z");
        var nextDayCorrection = CreateTrade(externalReference, accountId, 100m, 184.00m, "2025-03-02T10:00:00Z");

        using var initialResponse = await _client.PostAsJsonAsync("/api/v1/trades", initial);
        using var correctionResponse = await _client.PostAsJsonAsync("/api/v1/trades", nextDayCorrection);
        Assert.Equal(HttpStatusCode.Created, initialResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, correctionResponse.StatusCode);

        var dayOne = await ExecuteSnapshotProcedureAsync(accountId, new DateOnly(2025, 3, 1));
        Assert.Equal(120m, dayOne.Quantity);
        Assert.Equal(185.400000m, dayOne.UnitCostUsd);
        Assert.Equal(186.000000m, dayOne.MarketPriceUsd);
        Assert.Equal(22_320.000000m, dayOne.TotalMarketValueUsd);

        var dayTwo = await ExecuteSnapshotProcedureAsync(accountId, new DateOnly(2025, 3, 2));
        Assert.Equal(100m, dayTwo.Quantity);
        Assert.Equal(184.000000m, dayTwo.UnitCostUsd);
        Assert.Equal(190.000000m, dayTwo.MarketPriceUsd);
        Assert.Equal(19_000.000000m, dayTwo.TotalMarketValueUsd);
    }

    private async Task<StoredProcedureSnapshot> ExecuteSnapshotProcedureAsync(
        string accountId,
        DateOnly valuationDate)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        await dbContext.Database.OpenConnectionAsync();
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = "portfolio.GetPortfolioSnapshot";
        command.CommandType = CommandType.StoredProcedure;

        var accountParameter = command.CreateParameter();
        accountParameter.ParameterName = "@AccountId";
        accountParameter.DbType = DbType.String;
        accountParameter.Size = 64;
        accountParameter.Value = accountId;
        command.Parameters.Add(accountParameter);

        var dateParameter = command.CreateParameter();
        dateParameter.ParameterName = "@ValuationDate";
        dateParameter.DbType = DbType.Date;
        dateParameter.Value = valuationDate.ToDateTime(TimeOnly.MinValue);
        command.Parameters.Add(dateParameter);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var quantity = reader.GetDecimal(reader.GetOrdinal("Quantity"));
        var unitCost = reader.GetDecimal(reader.GetOrdinal("UnitCostUsd"));
        var marketPrice = reader.GetDecimal(reader.GetOrdinal("MarketPriceUsd"));

        Assert.True(await reader.NextResultAsync());
        Assert.True(await reader.ReadAsync());
        var totalMarketValue = reader.GetDecimal(reader.GetOrdinal("TotalMarketValueUsd"));
        return new StoredProcedureSnapshot(quantity, unitCost, marketPrice, totalMarketValue);
    }

    private static object CreateTrade(
        string externalReference,
        string accountId,
        decimal quantity,
        decimal price,
        string asOf)
    {
        return new
        {
            external_ref = externalReference,
            account_id = accountId,
            instrument = new { isin = "US0378331005" },
            side = "BUY",
            quantity,
            price,
            trade_date = "2025-03-01",
            as_of = asOf,
        };
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    private sealed record StoredProcedureSnapshot(
        decimal Quantity,
        decimal UnitCostUsd,
        decimal MarketPriceUsd,
        decimal TotalMarketValueUsd);
}
