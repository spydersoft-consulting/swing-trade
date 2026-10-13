<#
.SYNOPSIS
  Read-only check of real market data against a RUNNING Local API (starts nothing).
.DESCRIPTION
  Mints an admin token from the mock IdP, POSTs /api/v1/refresh, polls the run, then prints one
  row per watchlist ticker (as-of, last close, MA20, MA50, trend, signal, bars, flags).
  Keys must already be configured (docs/M3_MANUAL_CHECK.md). Exit code 1 if the run failed or any ticker is flagged.
#>
param(
  [string]$Api = 'http://localhost:8180',
  [string]$Idp = 'http://localhost:8210',
  [int]$TimeoutSec = 300,
  [int]$StaleDays = 4,
  [switch]$NoRefresh
)
$ErrorActionPreference = 'Stop'
$tok = (Invoke-RestMethod -Method Post "$Idp/connect/token" -Body @{
  grant_type='password'; client_id='swingtrade-api-test'; client_secret='swingtrade-api-test-secret'
  username='admin'; password='Test123!'; scope='swingtrade' }).access_token
$h = @{ Authorization = "Bearer $tok" }

$run = $null
if (-not $NoRefresh) {
  try { $run = Invoke-RestMethod -Method Post "$Api/api/v1/refresh" -Headers $h }
  catch {
    if ($_.Exception.Response.StatusCode.value__ -eq 409) { Write-Warning 'A run is already active; waiting for it.'
      $run = (Invoke-RestMethod "$Api/api/v1/refresh/runs?page=1&pageSize=1" -Headers $h).items[0] }
    else { throw }
  }
  $deadline = (Get-Date).AddSeconds($TimeoutSec)
  while ($run.status -eq 'Running') {
    if ((Get-Date) -gt $deadline) { throw "Run $($run.id) still Running after $TimeoutSec s" }
    Start-Sleep -Seconds 3
    $run = Invoke-RestMethod "$Api/api/v1/refresh/runs/$($run.id)" -Headers $h
  }
} else {
  $run = (Invoke-RestMethod "$Api/api/v1/refresh/runs?page=1&pageSize=1" -Headers $h).items[0]
}

Write-Host ''
Write-Host ("Run {0}: status={1} provider={2} ok={3} failed={4}" -f $run.id,$run.status,$run.provider,$run.tickersOk,$run.tickersFailed)
if ($run.message) { Write-Host "Message (failed tickers / fallbacks): $($run.message)" }

$rows = Invoke-RestMethod "$Api/api/v1/watchlist" -Headers $h
$today = (Get-Date).Date
$out = foreach ($r in $rows) {
  $bars = (Invoke-RestMethod "$Api/api/v1/instruments/$($r.ticker)/bars?days=120" -Headers $h).bars
  $n = @($bars).Count
  $flags = @()
  if ($n -lt 50) { $flags += "BARS<50 ($n)" }
  if ($null -eq $r.asOfDate) { $flags += 'NO DATA' }
  elseif (([datetime]$r.asOfDate) -lt $today.AddDays(-$StaleDays)) { $flags += "OLD ASOF" }
  if ($r.stale) { $flags += 'STALE' }
  if ($null -eq $r.ma50) { $flags += 'NO MA50' }
  [pscustomobject]@{ Ticker=$r.ticker; AsOf=$r.asOfDate; Last=$r.last; MA20=$r.ma20; MA50=$r.ma50
    Trend=$r.trend; Signal=$r.signal; Bars=$n; Flags=($flags -join '; ') }
}
$out | Sort-Object Ticker | Format-Table -AutoSize
$flagged = @($out | Where-Object Flags)
Write-Host ("{0} tickers listed (expected 12), {1} flagged." -f @($out).Count, $flagged.Count)
Write-Host 'Bars is capped at 120 by the API; compare Last/MA20/MA50 with a broker chart (plain closes) per docs/M3_MANUAL_CHECK.md.'
if ($run.status -ne 'Succeeded' -or $flagged.Count -gt 0 -or @($out).Count -ne 12) { exit 1 }
