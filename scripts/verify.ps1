$ErrorActionPreference = "Stop"

function Assert-NativeSucceeded {
    param(
        [int]$ExitCode,
        [string]$Step
    )

    if ($ExitCode -ne 0) {
        throw "$Step failed with exit code $ExitCode."
    }
}

Write-Host "Starting SQL Server LocalDB..."
sqllocaldb start MSSQLLocalDB | Out-Host
Assert-NativeSucceeded -ExitCode $LASTEXITCODE -Step "LocalDB startup"

Write-Host "Restoring dependencies..."
dotnet restore .\Sygnia.PortfolioService.sln | Out-Host
Assert-NativeSucceeded -ExitCode $LASTEXITCODE -Step "Dependency restore"

Write-Host "Building Release with warnings as errors..."
dotnet build .\Sygnia.PortfolioService.sln --no-restore --configuration Release | Out-Host
Assert-NativeSucceeded -ExitCode $LASTEXITCODE -Step "Release build"

Write-Host "Running SQL Server integration tests..."
dotnet test .\tests\PortfolioService.IntegrationTests `
    --no-build `
    --no-restore `
    --configuration Release `
    --logger "console;verbosity=minimal" | Out-Host
Assert-NativeSucceeded -ExitCode $LASTEXITCODE -Step "Integration tests"

Write-Host "Verification passed." -ForegroundColor Green
