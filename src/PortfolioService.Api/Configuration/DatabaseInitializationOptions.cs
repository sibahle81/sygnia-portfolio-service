namespace PortfolioService.Configuration;

public sealed class DatabaseInitializationOptions
{
    public const string SectionName = "DatabaseInitialization";

    public bool ApplyMigrationsOnStartup { get; init; }
}
