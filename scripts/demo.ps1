param(
    [string]$BaseUrl = "http://localhost:5180"
)

$ErrorActionPreference = "Stop"
$runId = Get-Date -Format "yyyyMMddHHmmssfff"
$externalReference = "DEMO-$runId"
$accountId = "DEMO-ACCOUNT-$runId"

function Invoke-TradeSubmission {
    param([hashtable]$Payload)

    $json = $Payload | ConvertTo-Json -Depth 5
    return Invoke-RestMethod `
        -Method Post `
        -Uri "$BaseUrl/api/v1/trades" `
        -ContentType "application/json" `
        -Body $json
}

function Assert-Equal {
    param(
        $Expected,
        $Actual,
        [string]$Message
    )

    if ($Expected -ne $Actual) {
        throw "$Message Expected '$Expected', received '$Actual'."
    }
}

$initial = @{
    external_ref = $externalReference
    account_id = $accountId
    instrument = @{ isin = "US0378331005" }
    side = "BUY"
    quantity = 120
    price = 185.40
    trade_date = "2025-03-01"
    as_of = "2025-03-01T10:15:00Z"
}

Write-Host "1/5 Submitting initial event..."
$accepted = Invoke-TradeSubmission -Payload $initial
Assert-Equal "accepted" $accepted.outcome "Initial submission failed."
$accepted | ConvertTo-Json -Depth 5

Write-Host "2/5 Resending the exact event..."
$duplicate = Invoke-TradeSubmission -Payload $initial
Assert-Equal "duplicate" $duplicate.outcome "Duplicate was not ignored."
$duplicate | ConvertTo-Json -Depth 5

$correction = @{
    external_ref = $externalReference
    account_id = $accountId
    instrument = @{ isin = "US0378331005" }
    side = "BUY"
    quantity = 100
    price = 184.00
    trade_date = "2025-03-01"
    as_of = "2025-03-01T11:00:00Z"
}

Write-Host "3/5 Submitting later correction..."
$corrected = Invoke-TradeSubmission -Payload $correction
Assert-Equal "corrected" $corrected.outcome "Correction was not accepted."
Assert-Equal 2 $corrected.current_version "Correction did not create version 2."
$corrected | ConvertTo-Json -Depth 5

Write-Host "4/5 Retrieving immutable audit history..."
$events = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/v1/trades/$externalReference/events"
Assert-Equal 2 $events.Count "Audit history should contain two accepted versions."
$events | ConvertTo-Json -Depth 5

Write-Host "5/5 Retrieving corrected snapshot..."
$snapshot = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/v1/portfolios/$accountId/snapshots/2025-03-01"
Assert-Equal 100 $snapshot.positions[0].quantity "Corrected quantity is wrong."
Assert-Equal 18600 $snapshot.total_market_value_usd "Snapshot total is wrong."
Assert-Equal 200 $snapshot.positions[0].unrealized_profit_loss_usd "Unrealized P/L is wrong."
$snapshot | ConvertTo-Json -Depth 5

Write-Host "Demo passed for account $accountId." -ForegroundColor Green
