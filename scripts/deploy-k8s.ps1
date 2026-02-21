#Requires -Version 7.0
<#
.SYNOPSIS
    CloudSOA AKS Deployment Script
.DESCRIPTION
    Deploys all CloudSOA components (Broker, Portal, ServiceManager, ServiceHost)
    to AKS with security configuration (auth, network policies).
.EXAMPLE
    .\scripts\deploy-k8s.ps1 -AcrServer cloudsoacr.azurecr.io -Tag v1.8.0 -RedisHost "host:6380" -RedisPassword "xxx"
    .\scripts\deploy-k8s.ps1 -AcrServer cloudsoacr.azurecr.io -Tag v1.8.0 -RedisHost "host:6380" -RedisPassword "xxx" -BlobConnectionString "..." -AuthMode apikey
#>

[CmdletBinding()]
param(
    [string]$AcrServer           = '',
    [string]$Tag                 = 'latest',
    [string]$RedisHost           = '',
    [string]$RedisPassword       = '',
    [string]$BlobConnectionString = '',
    [string]$Namespace           = 'cloudsoa',
    [ValidateSet('none','apikey','jwt')]
    [string]$AuthMode            = 'apikey',
    [switch]$InstallKeda,
    [switch]$EnableNetworkPolicies
)

$ErrorActionPreference = 'Stop'

function Write-Log  { param($Msg) Write-Host "  [✓] $Msg" -ForegroundColor Green }
function Write-Warn { param($Msg) Write-Host "  [!] $Msg" -ForegroundColor Yellow }
function Write-Err  { param($Msg) Write-Host "  [✗] $Msg" -ForegroundColor Red; exit 1 }

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$K8sDir      = Join-Path $ProjectRoot 'deploy\k8s'

Write-Host '============================================'
Write-Host '  CloudSOA K8s Deployment'
Write-Host '============================================'
Write-Host "  ACR:        $(if ($AcrServer) { $AcrServer } else { 'not specified' })"
Write-Host "  Tag:        $Tag"
Write-Host "  Namespace:  $Namespace"
Write-Host "  Auth:       $AuthMode"
Write-Host '============================================'
Write-Host ''

# ---- Prerequisites ----
if (-not (Get-Command kubectl -ErrorAction SilentlyContinue)) { Write-Err 'kubectl not found' }
try { kubectl cluster-info 2>&1 | Out-Null } catch { Write-Err 'Cannot connect to K8s cluster' }

Write-Log 'Connected to cluster'

# ---- Namespace ----
Write-Log 'Creating namespace...'
kubectl create namespace $Namespace --dry-run=client -o yaml | kubectl apply -f -

# ---- ACR Pull Secret (for portal/servicemanager that use imagePullSecrets) ----
if ($AcrServer) {
    $acrName = ($AcrServer -split '\.')[0]
    $acrCreds = az acr credential show --name $acrName 2>$null | ConvertFrom-Json
    if ($acrCreds) {
        Write-Log 'Creating ACR pull secret...'
        kubectl create secret docker-registry acr-secret `
            -n $Namespace `
            --docker-server=$AcrServer `
            --docker-username=$($acrCreds.username) `
            --docker-password=$($acrCreds.passwords[0].value) `
            --dry-run=client -o yaml | kubectl apply -f -
    }
}

# ---- Redis Secret ----
if ($RedisHost -and $RedisPassword) {
    Write-Log 'Creating Redis secret...'
    $connStr = "$RedisHost,password=$RedisPassword,ssl=True,abortConnect=False"
    kubectl create secret generic redis-secret `
        -n $Namespace `
        --from-literal="connection-string=$connStr" `
        --dry-run=client -o yaml | kubectl apply -f -
} else {
    Write-Warn 'No external Redis specified, deploying in-cluster Redis (dev only)...'
    kubectl apply -f (Join-Path $K8sDir 'redis.yaml')
}

# ---- ServiceManager Secrets ----
if ($BlobConnectionString) {
    Write-Log 'Creating ServiceManager secrets...'
    kubectl create secret generic servicemanager-secrets `
        -n $Namespace `
        --from-literal="blob-connection-string=$BlobConnectionString" `
        --from-literal="cosmosdb-connection-string=" `
        --dry-run=client -o yaml | kubectl apply -f -
} else {
    Write-Warn 'No BlobConnectionString specified; ServiceManager may use dev storage'
    kubectl create secret generic servicemanager-secrets `
        -n $Namespace `
        --from-literal="blob-connection-string=UseDevelopmentStorage=true" `
        --from-literal="cosmosdb-connection-string=" `
        --dry-run=client -o yaml | kubectl apply -f -
}

# ---- API Key Secret ----
$ApiKey = -join ((1..32) | ForEach-Object { '{0:x2}' -f (Get-Random -Maximum 256) })
kubectl create secret generic broker-auth `
    -n $Namespace `
    --from-literal="api-key=$ApiKey" `
    --dry-run=client -o yaml | kubectl apply -f -
Write-Log "Secrets created (API Key: $($ApiKey.Substring(0,8))...)"

# ---- ConfigMap (update Redis connection & auth mode) ----
Write-Log 'Deploying ConfigMap...'
$configYaml = Get-Content (Join-Path $K8sDir 'broker-configmap.yaml') -Raw
if ($RedisHost -and $RedisPassword) {
    $redisConn = "$RedisHost,password=$RedisPassword,ssl=True,abortConnect=False"
    $configYaml = $configYaml -replace 'ConnectionStrings__Redis:.*', "ConnectionStrings__Redis: `"$redisConn`""
}
if ($BlobConnectionString) {
    $configYaml = $configYaml -replace 'ConnectionStrings__BlobStorage:.*', "ConnectionStrings__BlobStorage: `"$BlobConnectionString`""
}
# Set auth mode
if ($configYaml -match 'Authentication__Mode') {
    $configYaml = $configYaml -replace 'Authentication__Mode:.*', "Authentication__Mode: `"$AuthMode`""
} else {
    # Add auth mode under data section (after last data entry)
    $configYaml = $configYaml.TrimEnd() + "`n  Authentication__Mode: `"$AuthMode`"`n"
}
$configYaml | kubectl apply -f -

# ---- Helper: patch image in YAML and apply ----
function Deploy-Component {
    param([string]$YamlFile, [string]$ImageName, [string]$DisplayName)
    $yamlPath = Join-Path $K8sDir $YamlFile
    if (-not (Test-Path $yamlPath)) {
        Write-Warn "$DisplayName YAML not found: $YamlFile"
        return
    }
    Write-Log "Deploying $DisplayName..."
    $content = Get-Content $yamlPath -Raw
    if ($AcrServer) {
        # Replace any ACR image reference with the correct one
        $content = $content -replace '[a-z0-9]+\.azurecr\.io/' , "$AcrServer/"
        $content = $content -replace "(image:\s*${AcrServer}/${ImageName}:)\S+", "`${1}${Tag}"
        # Also handle bare image names like "cloudsoa.azurecr.io/broker:latest"
        $content = $content -replace "(image:\s*)cloudsoa\.azurecr\.io/${ImageName}:\S+", "`${1}${AcrServer}/${ImageName}:${Tag}"
    }
    $content | kubectl apply -f -
}

# ---- Deploy Components ----
Deploy-Component 'broker-deployment.yaml'         'broker'         'Broker'
Deploy-Component 'portal-deployment.yaml'          'portal'         'Portal'
Deploy-Component 'servicemanager-deployment.yaml'  'servicemanager' 'ServiceManager'

# ---- Network Policies ----
if ($EnableNetworkPolicies) {
    Write-Log 'Applying network policies...'
    kubectl apply -f (Join-Path $K8sDir 'network-policies.yaml')
} else {
    Write-Warn 'Network policies not enabled (use -EnableNetworkPolicies)'
}

# ---- Install KEDA ----
if ($InstallKeda) {
    Write-Log 'Installing KEDA...'
    helm repo add kedacore https://kedacore.github.io/charts 2>&1 | Out-Null
    helm repo update 2>&1 | Out-Null
    helm upgrade --install keda kedacore/keda --namespace keda --create-namespace --wait
    Write-Log 'KEDA installed'
}

# ---- Wait for rollout ----
Write-Host ''
Write-Log 'Waiting for Broker...'
kubectl -n $Namespace rollout status deployment/broker --timeout=180s

Write-Log 'Waiting for Portal...'
kubectl -n $Namespace rollout status deployment/portal --timeout=180s

Write-Log 'Waiting for ServiceManager...'
kubectl -n $Namespace rollout status deployment/servicemanager --timeout=180s

Write-Host ''
Write-Log 'Pod status:'
kubectl -n $Namespace get pods -o wide

Write-Host ''
Write-Log 'Service status:'
kubectl -n $Namespace get svc

# ---- Get external IPs ----
Write-Host ''
$brokerSvc = kubectl -n $Namespace get svc broker-service -o json 2>$null | ConvertFrom-Json
$portalSvc = kubectl -n $Namespace get svc portal-service -o json 2>$null | ConvertFrom-Json
$brokerIp = $brokerSvc.status.loadBalancer.ingress[0].ip
$portalIp = $portalSvc.status.loadBalancer.ingress[0].ip

Write-Host ''
Write-Host '============================================'
Write-Host '  ✅ K8s Deployment Complete!'
Write-Host '============================================'
Write-Host ''
Write-Host "  Broker:         http://$brokerIp"
Write-Host "  Portal:         http://$portalIp"
Write-Host "  Auth Mode:      $AuthMode"
Write-Host "  API Key:        $ApiKey"
Write-Host "  (Header: X-Api-Key: $ApiKey)"
Write-Host ''
Write-Host '  Verify:'
Write-Host "    curl http://$brokerIp/healthz"
Write-Host "    curl -H 'X-Api-Key: $ApiKey' http://$brokerIp/api/v1/sessions"
Write-Host ''
