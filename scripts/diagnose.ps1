#Requires -Version 7.0
<#
.SYNOPSIS
    CloudSOA Diagnostics Script
.DESCRIPTION
    Check the health status of the K8s cluster or local development environment
.EXAMPLE
    .\scripts\diagnose.ps1
    .\scripts\diagnose.ps1 -Namespace myns
#>

[CmdletBinding()]
param(
    [string]$Namespace = 'cloudsoa'
)

$ErrorActionPreference = 'Continue'

function Write-Section { param($Msg) Write-Host "`n=== $Msg ===`n" -ForegroundColor Green }

Write-Host '============================================'
Write-Host '  CloudSOA System Diagnostics'
Write-Host "  Namespace: $Namespace"
Write-Host "  Time:      $((Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'))"
Write-Host '============================================'

# ---- Check K8s connectivity ----
$k8sConnected = $false
if (Get-Command kubectl -ErrorAction SilentlyContinue) {
    try {
        kubectl cluster-info 2>&1 | Out-Null
        if ($LASTEXITCODE -eq 0) { $k8sConnected = $true }
    } catch {}
}

if (-not $k8sConnected) {
    Write-Host "`n[!] Cannot connect to K8s cluster, checking local environment only" -ForegroundColor Yellow

    Write-Section 'Local Service Status'
    try {
        $resp = Invoke-WebRequest -Uri 'http://localhost:5000/healthz' -UseBasicParsing -TimeoutSec 3 -ErrorAction Stop
        if ($resp.StatusCode -eq 200) {
            Write-Host 'Broker (localhost:5000): Healthy' -ForegroundColor Green
        }
    } catch {
        Write-Host 'Broker (localhost:5000): unavailable' -ForegroundColor Red
    }

    Write-Section 'Docker Containers'
    if (Get-Command docker -ErrorAction SilentlyContinue) {
        docker ps --format 'table {{.Names}}\t{{.Status}}\t{{.Ports}}' 2>&1
    } else {
        Write-Host 'Docker unavailable'
    }

    Write-Section 'Redis Check'
    try {
        $ping = docker exec cloudsoa-redis redis-cli ping 2>&1
        if ($ping -match 'PONG') {
            Write-Host "Redis: PONG"
            $dbsize = docker exec cloudsoa-redis redis-cli DBSIZE 2>&1
            Write-Host "Keys: $dbsize"
            Write-Host ''
            Write-Host 'Session-related keys:'
            docker exec cloudsoa-redis redis-cli --scan --pattern 'cloudsoa:*' 2>&1 | Select-Object -First 20
        } else {
            Write-Host 'Redis unavailable' -ForegroundColor Red
        }
    } catch {
        Write-Host 'Redis unavailable' -ForegroundColor Red
    }

    exit 0
}

# ---- K8s diagnostics ----
Write-Section 'Node Status'
kubectl get nodes -o wide

Write-Section "Namespace $Namespace Overview"
kubectl -n $Namespace get all

Write-Section 'Pod Details'
kubectl -n $Namespace get pods -o wide
Write-Host ''

$pods = kubectl -n $Namespace get pods -o name --no-headers 2>&1
foreach ($pod in $pods) {
    if (-not $pod) { continue }
    $status   = kubectl -n $Namespace get $pod -o jsonpath='{.status.phase}' 2>&1
    $restarts = kubectl -n $Namespace get $pod -o jsonpath='{.status.containerStatuses[0].restartCount}' 2>&1
    if (-not $restarts) { $restarts = '?' }

    if ($status -ne 'Running' -or ([int]$restarts -gt 5)) {
        Write-Host "${pod}: $status (restarts: $restarts)" -ForegroundColor Red
        Write-Host '  Recent logs:'
        kubectl -n $Namespace logs $pod --tail=5 2>&1 | ForEach-Object { "    $_" }
    }
}

Write-Section 'Service Endpoints'
kubectl -n $Namespace get endpoints

Write-Section 'HPA Status'
kubectl -n $Namespace get hpa 2>&1
if ($LASTEXITCODE -ne 0) { Write-Host 'No HPA' }

Write-Section 'KEDA ScaledObjects'
kubectl -n $Namespace get scaledobject 2>&1
if ($LASTEXITCODE -ne 0) { Write-Host 'No KEDA ScaledObject' }

Write-Section 'Recent Events (Warning)'
$events = kubectl -n $Namespace get events --field-selector type=Warning --sort-by='.lastTimestamp' 2>&1
if ($events) { $events | Select-Object -Last 10 } else { Write-Host 'No warning events' }

Write-Section 'Broker Logs (last 20 lines)'
kubectl -n $Namespace logs -l app=broker --tail=20 2>&1
if ($LASTEXITCODE -ne 0) { Write-Host 'No Broker Pod' }

Write-Section 'Resource Usage'
kubectl -n $Namespace top pods 2>&1
if ($LASTEXITCODE -ne 0) { Write-Host 'Metrics server not installed' }

Write-Host ''
Write-Host '============================================'
Write-Host '  Diagnostics complete'
Write-Host '============================================'
