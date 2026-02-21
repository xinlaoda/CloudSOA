#Requires -Version 7.0
<#
.SYNOPSIS
    CloudSOA Smoke Test Script
.DESCRIPTION
    Run 8 smoke tests against the Broker service to verify basic functionality
.EXAMPLE
    .\scripts\smoke-test.ps1
    .\scripts\smoke-test.ps1 -BrokerUrl http://myhost:5000
#>

[CmdletBinding()]
param(
    [string]$BrokerUrl = 'http://localhost:5000'
)

$ErrorActionPreference = 'Continue'

$Passed = 0
$Failed = 0

function Test-Pass { param($Msg) $script:Passed++; Write-Host "  ✓ $Msg" -ForegroundColor Green }
function Test-Fail { param($Msg) $script:Failed++; Write-Host "  ✗ $Msg" -ForegroundColor Red }

Write-Host '============================================'
Write-Host '  CloudSOA Smoke Test'
Write-Host "  Broker: $BrokerUrl"
Write-Host '============================================'
Write-Host ''

# ---- Test 1: Health Check ----
Write-Host '[1/8] Health Check'
try {
    $resp = Invoke-WebRequest -Uri "$BrokerUrl/healthz" -UseBasicParsing -ErrorAction Stop
    if ($resp.StatusCode -eq 200 -and $resp.Content.Trim() -eq 'Healthy') {
        Test-Pass 'GET /healthz → 200 Healthy'
    } else {
        Test-Fail "GET /healthz → $($resp.StatusCode) $($resp.Content)"
    }
} catch {
    Test-Fail "GET /healthz → $($_.Exception.Message)"
}

# ---- Test 2: Metrics ----
Write-Host '[2/8] Metrics Endpoint'
try {
    $resp = Invoke-WebRequest -Uri "$BrokerUrl/metrics" -UseBasicParsing -ErrorAction Stop
    if ($resp.StatusCode -eq 200) { Test-Pass 'GET /metrics → 200' }
    else { Test-Fail "GET /metrics → $($resp.StatusCode)" }
} catch {
    Test-Fail "GET /metrics → $($_.Exception.Message)"
}

# ---- Test 3: Create Session ----
Write-Host '[3/8] Create Session'
$SessionId = ''
try {
    $body = '{"serviceName":"SmokeTestService","minimumUnits":1,"maximumUnits":5}'
    $resp = Invoke-RestMethod -Uri "$BrokerUrl/api/v1/sessions" -Method Post `
        -ContentType 'application/json' -Body $body -ErrorAction Stop -StatusCodeVariable 'sc'
    # Invoke-RestMethod with -StatusCodeVariable requires PS 7+
    $SessionId = $resp.sessionId
    if ($SessionId) {
        Test-Pass "POST /sessions → 201, sessionId=$($SessionId.Substring(0, [Math]::Min(12, $SessionId.Length)))..."
    } else {
        Test-Fail 'POST /sessions → could not parse sessionId'
    }
} catch {
    Test-Fail "POST /sessions → $($_.Exception.Message)"
}

if (-not $SessionId) {
    Write-Host ''
    Write-Host '  Cannot continue testing (Session creation failed)'
    Write-Host ''
    Write-Host "Results: $Passed passed, $Failed failed"
    exit 1
}

# ---- Test 4: Get Session ----
Write-Host '[4/8] Get Session'
try {
    $resp = Invoke-WebRequest -Uri "$BrokerUrl/api/v1/sessions/$SessionId" -UseBasicParsing -ErrorAction Stop
    if ($resp.StatusCode -eq 200) { Test-Pass 'GET /sessions/{id} → 200' }
    else { Test-Fail "GET /sessions/{id} → $($resp.StatusCode)" }
} catch {
    Test-Fail "GET /sessions/{id} → $($_.Exception.Message)"
}

# ---- Test 5: Send Requests ----
Write-Host '[5/8] Send Requests (3 items)'
try {
    $payload = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('smoke-test-data'))
    $body = @{
        requests = @(
            @{ action = 'Echo'; payload = $payload; userData = '1' }
            @{ action = 'Echo'; payload = $payload; userData = '2' }
            @{ action = 'Echo'; payload = $payload; userData = '3' }
        )
    } | ConvertTo-Json -Depth 3

    $resp = Invoke-WebRequest -Uri "$BrokerUrl/api/v1/sessions/$SessionId/requests" `
        -Method Post -ContentType 'application/json' -Body $body -UseBasicParsing -ErrorAction Stop
    if ($resp.StatusCode -eq 202) { Test-Pass 'POST /requests → 202 (3 enqueued)' }
    else { Test-Fail "POST /requests → $($resp.StatusCode)" }
} catch {
    Test-Fail "POST /requests → $($_.Exception.Message)"
}

# ---- Test 6: Wait and get responses ----
Write-Host '[6/8] Get Responses (wait 3s)'
Start-Sleep -Seconds 3
try {
    $resp = Invoke-RestMethod -Uri "$BrokerUrl/api/v1/sessions/$SessionId/responses?maxCount=10" -ErrorAction Stop
    if ($resp.count -ge 3) {
        Test-Pass "GET /responses → 200, count=$($resp.count)"
    } else {
        Test-Fail "GET /responses → 200, but count=$($resp.count) (expected>=3)"
    }
} catch {
    Test-Fail "GET /responses → $($_.Exception.Message)"
}

# ---- Test 7: Close Session ----
Write-Host '[7/8] Close Session'
try {
    $resp = Invoke-WebRequest -Uri "$BrokerUrl/api/v1/sessions/$SessionId" `
        -Method Delete -UseBasicParsing -ErrorAction Stop
    if ($resp.StatusCode -eq 204) { Test-Pass 'DELETE /sessions/{id} → 204' }
    else { Test-Fail "DELETE /sessions/{id} → $($resp.StatusCode)" }
} catch {
    # 204 may throw in some PS versions since no content
    if ($_.Exception.Response.StatusCode.value__ -eq 204) {
        Test-Pass 'DELETE /sessions/{id} → 204'
    } else {
        Test-Fail "DELETE /sessions/{id} → $($_.Exception.Message)"
    }
}

# ---- Test 8: 404 test ----
Write-Host '[8/8] Query non-existent Session'
try {
    Invoke-WebRequest -Uri "$BrokerUrl/api/v1/sessions/nonexistent-session-id" -UseBasicParsing -ErrorAction Stop
    Test-Fail 'GET /sessions/invalid → did not return 404'
} catch {
    if ($_.Exception.Response.StatusCode.value__ -eq 404) {
        Test-Pass 'GET /sessions/invalid → 404'
    } else {
        Test-Fail "GET /sessions/invalid → $($_.Exception.Message)"
    }
}

# ---- Results ----
Write-Host ''
Write-Host '============================================'
if ($Failed -eq 0) {
    Write-Host "  ✅ All passed: $Passed/$Passed" -ForegroundColor Green
} else {
    Write-Host "  ❌ Failed: $Failed, Passed: $Passed" -ForegroundColor Red
}
Write-Host '============================================'

exit $Failed
