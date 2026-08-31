# Trade Ingestion and Portfolio Snapshot Service

A .NET 8 Web API that ingests immutable trade-event versions, ignores resends, applies later corrections, retains an audit trail, and produces date-aware USD portfolio snapshots. Persistence and integration tests use SQL Server through Entity Framework Core.

## Quick start on Windows

Prerequisites:

- .NET 8 SDK or newer
- SQL Server LocalDB (installed with Visual Studio or SQL Server Express)

From the repository root:

```powershell
sqllocaldb start MSSQLLocalDB
dotnet restore
dotnet run --project .\src\PortfolioService.Api -- --migrate-only
dotnet run --project .\src\PortfolioService.Api
```

The API listens on `http://localhost:5180`. In another terminal, run the complete assessment scenario:

```powershell
.\scripts\demo.ps1
```

The demo uses a unique account and verifies:

1. An initial trade is accepted.
2. An exact resend is ignored.
3. A later correction is accepted as version 2.
4. Both accepted versions remain in the audit trail.
5. The snapshot contains 100 AAPL at USD 186, for a total value of USD 18,600 and unrealized P/L of USD 200.

## Verify the submission

The one-command verification builds with warnings as errors and runs five integration tests against temporary SQL Server databases:

```powershell
.\scripts\verify.ps1
```

Or run the commands separately:

```powershell
dotnet build .\Sygnia.PortfolioService.sln --configuration Release
dotnet test .\tests\PortfolioService.IntegrationTests --configuration Release
```

The tests cover migration/model consistency, the full API flow, eight concurrent resends, SQL procedure as-of behavior, and correction disablement. Test databases are deleted after the run.

For a non-LocalDB SQL Server, give the test login database create/drop permission and set a connection-string template. The fixture replaces its database name with a unique value:

```powershell
$env:TEST_SQLSERVER_CONNECTION = "Server=localhost,1433;Database=master;User Id=sa;Password=<password>;TrustServerCertificate=True"
dotnet test .\tests\PortfolioService.IntegrationTests
```

## API

| Method | Route | Purpose |
| --- | --- | --- |
| `POST` | `/api/v1/trades` | Accept an initial event or later correction; safely ignore a resend |
| `GET` | `/api/v1/trades/{external_ref}/events` | Retrieve the immutable accepted-event audit trail |
| `GET` | `/api/v1/portfolios/{account_id}/snapshots/{yyyy-MM-dd}` | Get positions, valuation, and account totals in USD |
| `GET` | `/health/live` | Process liveness |
| `GET` | `/health/ready` | SQL Server connectivity readiness |

Requests and responses use `snake_case`. A sample request is available in [requests/portfolio-service.http](requests/portfolio-service.http).

The API returns RFC 7807 problem details for validation, reference-data, conflict, and unexpected errors. Every response includes `X-Correlation-ID`; callers may supply a valid value in the request header.

## Database initialization and seed data

`--migrate-only` applies the EF Core migration without starting the HTTP listener. The migration creates:

- the `portfolio` schema;
- immutable `TradeEvents`, `Instruments`, and `MarketPrices` tables;
- unique and covering indexes for ingestion and snapshot access paths;
- the `portfolio.GetPortfolioSnapshot` stored procedure;
- AAPL (`US0378331005`) and USD closing prices of 186.00 on 2025-03-01 and 190.00 on 2025-03-02.

The standalone Task 2 artifact is [database/portfolio_snapshot.sql](database/portfolio_snapshot.sql). Execute it after the schema exists if the stored procedure or its supporting indexes need to be redeployed independently:

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -d SygniaPortfolio -E -i .\database\portfolio_snapshot.sql
sqlcmd -S "(localdb)\MSSQLLocalDB" -d SygniaPortfolio -E -Q "EXEC portfolio.GetPortfolioSnapshot @AccountId=N'ACC-001', @ValuationDate='2025-03-01'"
```

## Configuration

Override the application connection string with standard .NET configuration:

```powershell
$env:ConnectionStrings__PortfolioDatabase = "Server=localhost,1433;Database=SygniaPortfolio;User Id=sa;Password=<password>;TrustServerCertificate=True"
```

Corrections can be disabled while initial ingestion and duplicate protection remain safe:

```powershell
$env:Features__CorrectionProcessingEnabled = "false"
```

With corrections disabled, a later changed version returns `409` with code `correction_processing_disabled`; exact duplicates remain idempotent. Environment-variable changes require a process restart. Configuration providers that support reload are observed through `IOptionsMonitor`.

## Repository guide

- `src/PortfolioService.Api/Domain` - entities and domain enums
- `src/PortfolioService.Api/Features` - trade ingestion and portfolio snapshot slices
- `src/PortfolioService.Api/Infrastructure` - EF Core context, migration, and database initialization
- `src/PortfolioService.Api/Common` - correlation and centralized problem handling
- `database` - the single SQL deep-dive artifact
- `tests/PortfolioService.IntegrationTests` - real SQL Server end-to-end tests
- `scripts` - repeatable demo and verification commands
- [SOLUTION.md](SOLUTION.md) - architecture, assumptions, and trade-offs
- [AI_USAGE.md](AI_USAGE.md) - AI prompt and validation disclosure
