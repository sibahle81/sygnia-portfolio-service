using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace PortfolioService.IntegrationTests;

public sealed class FeatureFlagTests
{
    [Fact]
    public async Task DisabledCorrectionProcessingRejectsCorrectionButKeepsInitialTrade()
    {
        using var disabledFactory = new PortfolioApiFactory(correctionProcessingEnabled: false);
        using var client = disabledFactory.CreateClient();
        var externalReference = $"FLAG-{Guid.NewGuid():N}";
        var accountId = $"ACCOUNT-{Guid.NewGuid():N}";
        var initial = CreateTrade(externalReference, accountId, 120m, 185.40m, "2025-03-01T10:15:00Z");
        var correction = CreateTrade(externalReference, accountId, 100m, 184.00m, "2025-03-01T11:00:00Z");

        using var initialResponse = await client.PostAsJsonAsync("/api/v1/trades", initial);
        Assert.Equal(HttpStatusCode.Created, initialResponse.StatusCode);

        using var correctionResponse = await client.PostAsJsonAsync("/api/v1/trades", correction);
        Assert.Equal(HttpStatusCode.Conflict, correctionResponse.StatusCode);
        await using var problemStream = await correctionResponse.Content.ReadAsStreamAsync();
        using var problem = await JsonDocument.ParseAsync(problemStream);
        Assert.Equal(
            "correction_processing_disabled",
            problem.RootElement.GetProperty("code").GetString());

        using var auditResponse = await client.GetAsync($"/api/v1/trades/{externalReference}/events");
        auditResponse.EnsureSuccessStatusCode();
        await using var auditStream = await auditResponse.Content.ReadAsStreamAsync();
        using var audit = await JsonDocument.ParseAsync(auditStream);
        Assert.Equal(1, audit.RootElement.GetArrayLength());
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
}
