using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PortfolioService.Infrastructure;

public sealed class PortfolioDbContextFactory : IDesignTimeDbContextFactory<PortfolioDbContext>
{
    private const string DefaultConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=SygniaPortfolio;Trusted_Connection=True;TrustServerCertificate=True";

    public PortfolioDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__PortfolioDatabase")
            ?? DefaultConnectionString;
        var options = new DbContextOptionsBuilder<PortfolioDbContext>()
            .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
            .Options;

        return new PortfolioDbContext(options);
    }
}
