#Requires -Version 7.0
<#
.SYNOPSIS
    CloudSOA AKS 集群部署脚本
.DESCRIPTION
    将 Broker 和 ServiceHost 部署到 AKS 集群
.EXAMPLE
    .\scripts\deploy-k8s.ps1 -AcrServer cloudsoacr.azurecr.io -Tag v1.0.0 -RedisHost "host:6380" -RedisPassword "xxx"
#>

[CmdletBinding()]
param(
    [string]$AcrServer     = '',
    [string]$Tag           = 'latest',
    [string]$RedisHost     = '',
    [string]$RedisPassword = '',
    [string]$Namespace     = 'cloudsoa',
    [switch]$InstallKeda
)

$ErrorActionPreference = 'Stop'

function Write-Log  { param($Msg) Write-Host "  [✓] $Msg" -ForegroundColor Green }
function Write-Warn { param($Msg) Write-Host "  [!] $Msg" -ForegroundColor Yellow }
function Write-Err  { param($Msg) Write-Host "  [✗] $Msg" -ForegroundColor Red; exit 1 }

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$K8sDir      = Join-Path $ProjectRoot 'deploy\k8s'

Write-Host '============================================'
Write-Host '  CloudSOA K8s 部署'
Write-Host '============================================'
Write-Host "  ACR:        $(if ($AcrServer) { $AcrServer } else { '未指定' })"
Write-Host "  镜像标签:    $Tag"
Write-Host "  命名空间:    $Namespace"
Write-Host '============================================'
Write-Host ''

# ---- 检查前置条件 ----
if (-not (Get-Command kubectl -ErrorAction SilentlyContinue)) { Write-Err '请先安装 kubectl' }
try { kubectl cluster-info 2>&1 | Out-Null } catch { Write-Err '无法连接到 K8s 集群，请检查 kubeconfig' }

Write-Log '已连接到集群'

# ---- 创建命名空间 ----
Write-Log '创建命名空间...'
kubectl apply -f (Join-Path $K8sDir 'namespace.yaml')

# ---- 创建 Secrets ----
if ($RedisHost -and $RedisPassword) {
    Write-Log '创建 Redis Secret...'
    $connStr = "$RedisHost,password=$RedisPassword,ssl=True,abortConnect=False"
    kubectl create secret generic redis-secret `
        -n $Namespace `
        --from-literal="connection-string=$connStr" `
        --dry-run=client -o yaml | kubectl apply -f -
}

# 生成 API Key
$ApiKey = -join ((1..32) | ForEach-Object { '{0:x2}' -f (Get-Random -Maximum 256) })
kubectl create secret generic broker-auth `
    -n $Namespace `
    --from-literal="api-key=$ApiKey" `
    --dry-run=client -o yaml | kubectl apply -f -
Write-Log "Secrets 已创建 (API Key: $($ApiKey.Substring(0,8))...)"

# ---- 更新镜像地址并部署 ----
Write-Log '部署 ConfigMap...'
kubectl apply -f (Join-Path $K8sDir 'broker-configmap.yaml')

# 更新 Broker 镜像并部署
Write-Log '部署 Broker...'
$brokerYaml = Join-Path $K8sDir 'broker-deployment.yaml'
if ($AcrServer) {
    (Get-Content $brokerYaml -Raw) -replace 'cloudsoa\.azurecr\.io/broker:latest', "$AcrServer/broker:$Tag" |
        kubectl apply -f -
} else {
    kubectl apply -f $brokerYaml
}

# 部署 Redis (开发环境)
if (-not $RedisHost) {
    Write-Warn '未指定外部 Redis，部署集群内 Redis (仅用于开发)...'
    kubectl apply -f (Join-Path $K8sDir 'redis.yaml')
}

# 部署 ServiceHost
Write-Log '部署 ServiceHost...'
$shYaml = Join-Path $K8sDir 'servicehost-deployment.yaml'
if ($AcrServer) {
    (Get-Content $shYaml -Raw) -replace 'cloudsoa\.azurecr\.io/servicehost:latest', "$AcrServer/servicehost:$Tag" |
        kubectl apply -f -
} else {
    kubectl apply -f $shYaml
}

# ---- 安装 KEDA ----
if ($InstallKeda) {
    Write-Log '安装 KEDA...'
    helm repo add kedacore https://kedacore.github.io/charts 2>&1 | Out-Null
    helm repo update
    helm upgrade --install keda kedacore/keda --namespace keda --create-namespace --wait
    Write-Log 'KEDA 安装完成'
}

# ---- 等待部署就绪 ----
Write-Host ''
Write-Log '等待 Broker 就绪...'
kubectl -n $Namespace rollout status deployment/broker --timeout=120s

Write-Host ''
Write-Log 'Pod 状态:'
kubectl -n $Namespace get pods -o wide

Write-Host ''
Write-Log 'Service 状态:'
kubectl -n $Namespace get svc

Write-Host ''
Write-Host '============================================'
Write-Host '  ✅ K8s 部署完成！'
Write-Host '============================================'
Write-Host ''
Write-Host '  验证命令:'
Write-Host "    kubectl -n $Namespace port-forward svc/broker-service 5000:80"
Write-Host '    curl http://localhost:5000/healthz'
Write-Host ''
Write-Host "  API Key: $ApiKey"
Write-Host "  (使用 Header: X-Api-Key: $ApiKey)"
Write-Host ''
