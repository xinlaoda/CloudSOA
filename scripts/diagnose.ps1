#Requires -Version 7.0
<#
.SYNOPSIS
    CloudSOA 诊断脚本
.DESCRIPTION
    检查 K8s 集群或本地开发环境的健康状态
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
Write-Host '  CloudSOA 系统诊断'
Write-Host "  命名空间: $Namespace"
Write-Host "  时间:     $((Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'))"
Write-Host '============================================'

# ---- 检查是否可连接 K8s ----
$k8sConnected = $false
if (Get-Command kubectl -ErrorAction SilentlyContinue) {
    try {
        kubectl cluster-info 2>&1 | Out-Null
        if ($LASTEXITCODE -eq 0) { $k8sConnected = $true }
    } catch {}
}

if (-not $k8sConnected) {
    Write-Host "`n[!] 无法连接 K8s 集群，仅检查本地环境" -ForegroundColor Yellow

    Write-Section '本地服务状态'
    try {
        $resp = Invoke-WebRequest -Uri 'http://localhost:5000/healthz' -UseBasicParsing -TimeoutSec 3 -ErrorAction Stop
        if ($resp.StatusCode -eq 200) {
            Write-Host 'Broker (localhost:5000): Healthy' -ForegroundColor Green
        }
    } catch {
        Write-Host 'Broker (localhost:5000): 不可用' -ForegroundColor Red
    }

    Write-Section 'Docker 容器'
    if (Get-Command docker -ErrorAction SilentlyContinue) {
        docker ps --format 'table {{.Names}}\t{{.Status}}\t{{.Ports}}' 2>&1
    } else {
        Write-Host 'Docker 不可用'
    }

    Write-Section 'Redis 检查'
    try {
        $ping = docker exec cloudsoa-redis redis-cli ping 2>&1
        if ($ping -match 'PONG') {
            Write-Host "Redis: PONG"
            $dbsize = docker exec cloudsoa-redis redis-cli DBSIZE 2>&1
            Write-Host "Keys: $dbsize"
            Write-Host ''
            Write-Host 'Session 相关 keys:'
            docker exec cloudsoa-redis redis-cli --scan --pattern 'cloudsoa:*' 2>&1 | Select-Object -First 20
        } else {
            Write-Host 'Redis 不可用' -ForegroundColor Red
        }
    } catch {
        Write-Host 'Redis 不可用' -ForegroundColor Red
    }

    exit 0
}

# ---- K8s 诊断 ----
Write-Section '节点状态'
kubectl get nodes -o wide

Write-Section "命名空间 $Namespace 概览"
kubectl -n $Namespace get all

Write-Section 'Pod 详情'
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
        Write-Host '  最近日志:'
        kubectl -n $Namespace logs $pod --tail=5 2>&1 | ForEach-Object { "    $_" }
    }
}

Write-Section 'Service 端点'
kubectl -n $Namespace get endpoints

Write-Section 'HPA 状态'
kubectl -n $Namespace get hpa 2>&1
if ($LASTEXITCODE -ne 0) { Write-Host '无 HPA' }

Write-Section 'KEDA ScaledObjects'
kubectl -n $Namespace get scaledobject 2>&1
if ($LASTEXITCODE -ne 0) { Write-Host '无 KEDA ScaledObject' }

Write-Section '最近事件 (Warning)'
$events = kubectl -n $Namespace get events --field-selector type=Warning --sort-by='.lastTimestamp' 2>&1
if ($events) { $events | Select-Object -Last 10 } else { Write-Host '无警告事件' }

Write-Section 'Broker 日志 (最近20行)'
kubectl -n $Namespace logs -l app=broker --tail=20 2>&1
if ($LASTEXITCODE -ne 0) { Write-Host '无 Broker Pod' }

Write-Section '资源使用'
kubectl -n $Namespace top pods 2>&1
if ($LASTEXITCODE -ne 0) { Write-Host 'Metrics server 未安装' }

Write-Host ''
Write-Host '============================================'
Write-Host '  诊断完成'
Write-Host '============================================'
