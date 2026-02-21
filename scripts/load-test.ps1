#Requires -Version 7.0
<#
.SYNOPSIS
    CloudSOA Simple Load Test Script
.DESCRIPTION
    Send concurrent requests to the Broker and measure throughput
.EXAMPLE
    .\scripts\load-test.ps1
    .\scripts\load-test.ps1 -BrokerUrl http://myhost:5000 -TotalRequests 500 -Concurrency 10
#>

[CmdletBinding()]
param(
    [string]$BrokerUrl     = 'http://localhost:5000',
    [int]$TotalRequests    = 100,
    [int]$Concurrency      = 5
)

$ErrorActionPreference = 'Stop'

$BatchSize = [Math]::Floor($TotalRequests / $Concurrency)
$Payload   = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('load-test-payload-data-1234567890'))

Write-Host '============================================'
Write-Host '  CloudSOA Load Test'
Write-Host "  Broker:   $BrokerUrl"
Write-Host "  Total Requests: $TotalRequests"
Write-Host "  Concurrency:    $Concurrency"
Write-Host "  Batch Size:     $BatchSize"
Write-Host '============================================'
Write-Host ''

# 1. Create Session
Write-Host '[1] Creating Session...'
$session = Invoke-RestMethod -Uri "$BrokerUrl/api/v1/sessions" -Method Post `
    -ContentType 'application/json' `
    -Body '{"serviceName":"LoadTestService","minimumUnits":1,"maximumUnits":50}'
$SessionId = $session.sessionId
Write-Host "    SessionId: $SessionId"

# 2. Build request payload
$requests = @()
for ($i = 1; $i -le $BatchSize; $i++) {
    $requests += @{ action = 'Echo'; payload = $Payload; userData = "$i" }
}
$batchBody = @{ requests = $requests } | ConvertTo-Json -Depth 3 -Compress

# 3. Send concurrently
Write-Host "[2] Sending $TotalRequests requests ($Concurrency concurrent)..."
$sw = [System.Diagnostics.Stopwatch]::StartNew()

$jobs = @()
for ($c = 0; $c -lt $Concurrency; $c++) {
    $jobs += Start-ThreadJob -ScriptBlock {
        param($url, $sid, $body)
        Invoke-RestMethod -Uri "$url/api/v1/sessions/$sid/requests" `
            -Method Post -ContentType 'application/json' -Body $body | Out-Null
    } -ArgumentList $BrokerUrl, $SessionId, $batchBody
}

$jobs | Wait-Job | Out-Null
$jobs | Remove-Job

$sendMs = $sw.ElapsedMilliseconds
Write-Host "    Send time: ${sendMs}ms"

# Flush
Invoke-RestMethod -Uri "$BrokerUrl/api/v1/sessions/$SessionId/requests/flush" -Method Post | Out-Null

# 4. Wait and fetch responses
Write-Host '[3] Waiting and fetching responses...'
$received  = 0
$timeout   = 60
$waitStart = [DateTime]::UtcNow

while ($received -lt $TotalRequests) {
    $elapsed = ([DateTime]::UtcNow - $waitStart).TotalSeconds
    if ($elapsed -gt $timeout) {
        Write-Host "    ⚠ Timeout (${timeout}s), received $received/$TotalRequests" -ForegroundColor Yellow
        break
    }

    try {
        $resp = Invoke-RestMethod -Uri "$BrokerUrl/api/v1/sessions/$SessionId/responses?maxCount=500"
        $batchCount = [int]$resp.count
        $received += $batchCount

        if ($batchCount -eq 0) { Start-Sleep -Milliseconds 500 }
    } catch {
        Start-Sleep -Milliseconds 500
    }
}

$sw.Stop()
$totalMs = $sw.ElapsedMilliseconds

# 5. Close Session
Invoke-RestMethod -Uri "$BrokerUrl/api/v1/sessions/$SessionId" -Method Delete -ErrorAction SilentlyContinue | Out-Null

# 6. Output results
Write-Host ''
Write-Host '============================================'
Write-Host '  Load Test Results'
Write-Host '============================================'
Write-Host "  Total Requests:      $TotalRequests"
Write-Host "  Responses received:  $received"
Write-Host "  Total time:          ${totalMs}ms"

if ($totalMs -gt 0) {
    $tps = [Math]::Floor($received * 1000 / $totalMs)
    Write-Host "  Throughput:          $tps req/s"
}

if ($received -lt $TotalRequests) {
    $loss = $TotalRequests - $received
    $pct  = [Math]::Floor($loss * 100 / $TotalRequests)
    Write-Host "  Lost:                $loss (${pct}%)"
}

Write-Host '============================================'
