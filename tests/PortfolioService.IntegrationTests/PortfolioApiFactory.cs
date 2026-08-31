using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PortfolioService.Infrastructure;

namespace PortfolioService.IntegrationTests;

public sealed class PortfolioApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"SygniaPortfolioTests_{Guid.NewGuid():N}";
    private readonly bool _correctionProcessingEnabled;
    private readonly string _connectionString;
    private bool _databaseDeleted;

    public PortfolioApiFactory()
        : this(correctionProcessingEnabled: true)
    {
    }

    internal PortfolioApiFactory(bool correctionProcessingEnabled)
    {
        _correctionProcessingEnabled = correctionProcessingEnabled;
        var connectionTemplate = Environment.GetEnvironmentVariable("TEST_SQLSERVER_CONNECTION")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=master;Trusted_Connection=True;TrustServerCertificate=True";
        var connectionBuilder = new SqlConnectionStringBuilder(connectionTemplate)
        {
            InitialCatalog = _databaseName,
        };
        _connectionString = connectionBuilder.ConnectionString;
    }

    public string ConnectionString => _connectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PortfolioDatabase"] = ConnectionString,
                ["DatabaseInitialization:ApplyMigrationsOnStartup"] = "false",
                ["Features:CorrectionProcessingEnabled"] = _correctionProcessingEnabled.ToString(),
            });
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        dbContext.Database.Migrate();
        return host;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_databaseDeleted)
        {
            _databaseDeleted = true;
            var options = new DbContextOptionsBuilder<PortfolioDbContext>()
                .UseSqlServer(ConnectionString)
                .Options;
            using var dbContext = new PortfolioDbContext(options);
            dbContext.Database.EnsureDeleted();
        }

        base.Dispose(disposing);
    }
}
