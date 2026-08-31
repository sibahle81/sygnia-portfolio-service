using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PortfolioService.Common;
using PortfolioService.Configuration;
using PortfolioService.Features.Portfolios;
using PortfolioService.Features.Trades;
using PortfolioService.Infrastructure;

var migrateOnly = args.Any(value => string.Equals(value, "--migrate-only", StringComparison.OrdinalIgnoreCase));
var hostArguments = args.Where(value => !string.Equals(value, "--migrate-only", StringComparison.OrdinalIgnoreCase)).ToArray();
var builder = WebApplication.CreateBuilder(hostArguments);

var connectionString = builder.Configuration.GetConnectionString("PortfolioDatabase")
    ?? throw new InvalidOperationException("Connection string 'PortfolioDatabase' is required.");

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions.TryAdd("trace_id", context.HttpContext.TraceIdentifier);
    };
});
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
});
builder.Services.AddDbContext<PortfolioDbContext>(options =>
    options.UseSqlServer(connectionString, sql =>
        sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null)));
builder.Services.AddOptions<TradeProcessingOptions>()
    .Bind(builder.Configuration.GetSection(TradeProcessingOptions.SectionName));
builder.Services.AddOptions<DatabaseInitializationOptions>()
    .Bind(builder.Configuration.GetSection(DatabaseInitializationOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ITradeIngestionService, TradeIngestionService>();
builder.Services.AddScoped<IPortfolioSnapshotService, PortfolioSnapshotService>();

var app = builder.Build();

if (migrateOnly)
{
    await app.ApplyMigrationsAsync();
    return;
}

if (app.Services.GetRequiredService<IOptions<DatabaseInitializationOptions>>().Value.ApplyMigrationsOnStartup)
{
    await app.ApplyMigrationsAsync();
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.MapControllers();
app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", async (PortfolioDbContext dbContext, CancellationToken cancellationToken) =>
{
    var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
    return canConnect
        ? Results.Ok(new { status = "ready" })
        : Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Database unavailable",
            detail: "The service cannot connect to the portfolio database.");
});

app.Run();

public partial class Program;
