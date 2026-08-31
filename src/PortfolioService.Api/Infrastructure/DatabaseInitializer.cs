using Microsoft.EntityFrameworkCore;

namespace PortfolioService.Infrastructure;

public static partial class DatabaseInitializer
{
    public static async Task ApplyMigrationsAsync(this WebApplication app, CancellationToken cancellationToken = default)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<PortfolioDbContext>>();

        LogApplyingMigrations(logger);
        await dbContext.Database.MigrateAsync(cancellationToken);
        LogMigrationsCurrent(logger);
    }

    [LoggerMessage(EventId = 1200, Level = LogLevel.Information, Message = "Applying portfolio database migrations")]
    private static partial void LogApplyingMigrations(ILogger logger);

    [LoggerMessage(EventId = 1201, Level = LogLevel.Information, Message = "Portfolio database migrations are current")]
    private static partial void LogMigrationsCurrent(ILogger logger);
}
