#Requires -Version 7.0
<#
.SYNOPSIS
    CloudSOA AKS Deployment Script
.DESCRIPTION
    Deploys all CloudSOA components (Broker, Portal, ServiceManager, ServiceHost)
    to AKS with security configuration (auth, network policies, TLS).
.EXAMPLE
    .\scripts\deploy-k8s.ps1
    .\scripts\deploy-k8s.ps1 -AcrServer cloudsoacr.azurecr.io -Tag v1.8.0
    .\scripts\deploy-k8s.ps1 -AcrServer cloudsoacr.azurecr.io -Tag v1.8.0 -SkipTls
#>

[CmdletBinding()]
param(
    [string]$AcrServer           = '',
    [string]$Tag                 = 'latest',
    [string]$RedisHost           = '',
    [string]$RedisPassword       = '',
    [string]$BlobConnectionString = '',
    [string]$CosmosDbConnectionString = '',
    [string]$Namespace           = 'cloudsoa',
    [ValidateSet('none','apikey','jwt')]
    [string]$AuthMode            = 'apikey',
    [string]$InfraOutputsFile    = '',
    [switch]$InstallKeda,
    [bool]$EnableNetworkPolicies = $true,
    [switch]$SkipTls
)

$ErrorActionPreference = 'Stop'

function Write-Log  { param($Msg) Write-Host "  [✓] $Msg" -ForegroundColor Green }
function Write-Warn { param($Msg) Write-Host "  [!] $Msg" -ForegroundColor Yellow }
function Write-Err  { param($Msg) Write-Host "  [✗] $Msg" -ForegroundColor Red; exit 1 }

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$K8sDir      = Join-Path $ProjectRoot 'deploy\k8s'

# ---- Read infra outputs ----
if (-not $InfraOutputsFile) {
    $defaultOutputs = Join-Path $ProjectRoot 'deploy\infra-outputs.json'
    if (Test-Path $defaultOutputs) { $InfraOutputsFile = $defaultOutputs }
}
if ($InfraOutputsFile -and (Test-Path $InfraOutputsFile)) {
    Write-Log "Reading infra outputs from $InfraOutputsFile..."
    $infraOutputs = Get-Content $InfraOutputsFile -Raw | ConvertFrom-Json
    if (-not $AcrServer -and $infraOutputs.acr_login_server) {
        $AcrServer = $infraOutputs.acr_login_server.value
    }
    if (-not $RedisHost -and $infraOutputs.redis_hostname) {
        $RedisHost = $infraOutputs.redis_hostname.value
    }
    if (-not $RedisPassword -and $infraOutputs.redis_primary_key) {
        $RedisPassword = $infraOutputs.redis_primary_key.value
    }
    if (-not $BlobConnectionString -and $infraOutputs.blob_storage_connection_string) {
        $BlobConnectionString = $infraOutputs.blob_storage_connection_string.value
    }
    if (-not $CosmosDbConnectionString -and $infraOutputs.cosmosdb_endpoint -and $infraOutputs.cosmosdb_primary_key) {
        $CosmosDbConnectionString = "AccountEndpoint=$($infraOutputs.cosmosdb_endpoint.value);AccountKey=$($infraOutputs.cosmosdb_primary_key.value);"
    }
}

Write-Host '============================================'
Write-Host '  CloudSOA K8s Deployment'
Write-Host '============================================'
Write-Host "  ACR:        $(if ($AcrServer) { $AcrServer } else { 'not specified' })"
Write-Host "  Tag:        $Tag"
Write-Host "  Namespace:  $Namespace"
Write-Host "  Auth:       $AuthMode"
Write-Host "  TLS:        $(if ($SkipTls) { 'disabled' } else { 'direct (self-signed)' })"
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

# ---- ServiceManager Secrets (with CosmosDB) ----
$cosmosConn = if ($CosmosDbConnectionString) { $CosmosDbConnectionString } else { '' }
if ($BlobConnectionString) {
    Write-Log 'Creating ServiceManager secrets...'
    kubectl create secret generic servicemanager-secrets `
        -n $Namespace `
        --from-literal="blob-connection-string=$BlobConnectionString" `
        --from-literal="cosmosdb-connection-string=$cosmosConn" `
        --dry-run=client -o yaml | kubectl apply -f -
} else {
    Write-Warn 'No BlobConnectionString specified; ServiceManager may use dev storage'
    kubectl create secret generic servicemanager-secrets `
        -n $Namespace `
        --from-literal="blob-connection-string=UseDevelopmentStorage=true" `
        --from-literal="cosmosdb-connection-string=$cosmosConn" `
        --dry-run=client -o yaml | kubectl apply -f -
}

# ---- API Key Secret ----
$ApiKey = -join ((1..32) | ForEach-Object { '{0:x2}' -f (Get-Random -Maximum 256) })
kubectl create secret generic broker-auth `
    -n $Namespace `
    --from-literal="api-key=$ApiKey" `
    --dry-run=client -o yaml | kubectl apply -f -
Write-Log "Secrets created (API Key: $($ApiKey.Substring(0,8))...)"

# ---- TLS Certificate ----
$tlsCertPassword = 'cloudsoa-tls'
if (-not $SkipTls) {
    Write-Log 'Generating self-signed TLS certificate...'
    $pfxPath = Join-Path ([System.IO.Path]::GetTempPath()) 'broker.pfx'
    $cert = New-SelfSignedCertificate -DnsName 'cloudsoa-broker' -CertStoreLocation 'Cert:\CurrentUser\My' -NotAfter (Get-Date).AddDays(365) -KeyExportPolicy Exportable
    $pfxSecure = ConvertTo-SecureString -String $tlsCertPassword -Force -AsPlainText
    Export-PfxCertificate -Cert "Cert:\CurrentUser\My\$($cert.Thumbprint)" -FilePath $pfxPath -Password $pfxSecure | Out-Null
    Remove-Item "Cert:\CurrentUser\My\$($cert.Thumbprint)" -Force
    kubectl create secret generic broker-tls-cert `
        -n $Namespace `
        --from-file="broker.pfx=$pfxPath" `
        --dry-run=client -o yaml | kubectl apply -f -
    Remove-Item $pfxPath -Force -ErrorAction SilentlyContinue
    Write-Log 'TLS secret created'
}

# ---- ConfigMap (built from scratch via kubectl) ----
Write-Log 'Deploying ConfigMap...'
$redisConnStr = if ($RedisHost -and $RedisPassword) {
    "$RedisHost,password=$RedisPassword,ssl=True,abortConnect=False"
} else {
    "redis-service.cloudsoa.svc.cluster.local:6379"
}
$cmArgs = @(
    "ConnectionStrings__Redis=$redisConnStr",
    "ServiceManager__BaseUrl=http://servicemanager-service",
    "Authentication__Mode=$AuthMode"
)
# Only set Kestrel endpoints when TLS is NOT used (code configures them in TLS mode)
if ($SkipTls) {
    $cmArgs += "Kestrel__Endpoints__Http__Url=http://0.0.0.0:5000"
    $cmArgs += "Kestrel__Endpoints__Grpc__Url=http://0.0.0.0:5001"
    $cmArgs += "Kestrel__Endpoints__Grpc__Protocols=Http2"
}
if ($BlobConnectionString) {
    $cmArgs += "ConnectionStrings__BlobStorage=$BlobConnectionString"
}
if (-not $SkipTls) {
    $cmArgs += "Tls__Mode=direct"
    $cmArgs += "Tls__CertPath=/certs/broker.pfx"
    $cmArgs += "Tls__CertPassword=$tlsCertPassword"
}
$literalArgs = $cmArgs | ForEach-Object { "--from-literal=$_" }
kubectl create configmap broker-config -n $Namespace @literalArgs --dry-run=client -o yaml | kubectl apply -f -

# ---- Helper: patch image in YAML and apply via temp file ----
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
        $content = $content -replace '[a-z0-9]+\.azurecr\.io/', "$AcrServer/"
        $content = $content -replace "(image:\s*${AcrServer}/${ImageName}:)\S+", "`${1}${Tag}"
        $content = $content -replace "(image:\s*)cloudsoa\.azurecr\.io/${ImageName}:\S+", "`${1}${AcrServer}/${ImageName}:${Tag}"
    }
    $tmpFile = Join-Path ([System.IO.Path]::GetTempPath()) "cloudsoa-$ImageName.yaml"
    $content | Set-Content -Path $tmpFile -Encoding utf8
    try {
        kubectl apply -f $tmpFile
    } finally {
        Remove-Item $tmpFile -Force -ErrorAction SilentlyContinue
    }
}

# ---- Deploy Components ----
Deploy-Component 'broker-deployment.yaml' 'broker' 'Broker'

# Patch broker for TLS volume mount
if (-not $SkipTls) {
    Write-Log 'Patching Broker for TLS volume mount...'
    $tlsPatch = @{
        spec = @{
            template = @{
                spec = @{
                    volumes = @(
                        @{
                            name = 'tls-cert'
                            secret = @{ secretName = 'broker-tls-cert' }
                        }
                    )
                    containers = @(
                        @{
                            name = 'broker'
                            volumeMounts = @(
                                @{
                                    name = 'tls-cert'
                                    mountPath = '/certs'
                                    readOnly = $true
                                }
                            )
                            ports = @(
                                @{ containerPort = 5443; name = 'https' }
                                @{ containerPort = 5444; name = 'grpcs' }
                            )
                        }
                    )
                }
            }
        }
    } | ConvertTo-Json -Depth 10 -Compress
    kubectl -n $Namespace patch deployment broker --type strategic -p $tlsPatch
} else {
    # Non-TLS: change broker-service to expose HTTP instead of HTTPS
    Write-Log 'Patching Broker service for HTTP mode...'
    $svcPatch = @{
        spec = @{
            ports = @(
                @{ name = 'http'; port = 80; targetPort = 5000 }
                @{ name = 'grpc'; port = 5001; targetPort = 5001 }
            )
        }
    } | ConvertTo-Json -Depth 5 -Compress
    kubectl -n $Namespace patch svc broker-service --type strategic -p $svcPatch
}

Deploy-Component 'portal-deployment.yaml' 'portal' 'Portal'

# ---- ServiceManager RBAC ----
Write-Log 'Applying ServiceManager RBAC...'
kubectl apply -f (Join-Path $K8sDir 'servicemanager-rbac.yaml')

Deploy-Component 'servicemanager-deployment.yaml' 'servicemanager' 'ServiceManager'

# ---- Patch ServiceManager with ServiceHost image env vars ----
if ($AcrServer) {
    Write-Log 'Patching ServiceManager with ServiceHost images...'
    kubectl -n $Namespace set env deployment/servicemanager `
        "ServiceHost__Image=$AcrServer/servicehost:$Tag" `
        "ServiceHost__CoreWcfImage=$AcrServer/servicehost-corewcf:$Tag"
}

# ---- Network Policies ----
if ($EnableNetworkPolicies) {
    Write-Log 'Applying network policies...'
    kubectl apply -f (Join-Path $K8sDir 'network-policies.yaml')
} else {
    Write-Warn 'Network policies disabled (-EnableNetworkPolicies:$false)'
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

# ---- Wait for LoadBalancer IPs ----
Write-Host ''
Write-Log 'Waiting for LoadBalancer IPs (timeout 300s)...'
$timeout = 300
$elapsed = 0
$brokerIp = $null
$portalIp = $null
while ($elapsed -lt $timeout) {
    $brokerSvc = kubectl -n $Namespace get svc broker-service -o json 2>$null | ConvertFrom-Json
    $portalSvc = kubectl -n $Namespace get svc portal-service -o json 2>$null | ConvertFrom-Json
    $brokerIp = if ($brokerSvc.status.loadBalancer.ingress) { $brokerSvc.status.loadBalancer.ingress[0].ip } else { $null }
    $portalIp = if ($portalSvc.status.loadBalancer.ingress) { $portalSvc.status.loadBalancer.ingress[0].ip } else { $null }
    if ($brokerIp -and $portalIp) { break }
    Start-Sleep -Seconds 10
    $elapsed += 10
    Write-Host "    Waiting... ($elapsed s)"
}
if (-not $brokerIp) { Write-Warn 'Broker LoadBalancer IP not assigned within timeout' }
if (-not $portalIp) { Write-Warn 'Portal LoadBalancer IP not assigned within timeout' }

$brokerProto = if ($SkipTls) { 'http' } else { 'https' }

Write-Host ''
Write-Host '============================================'
Write-Host '  ✅ K8s Deployment Complete!'
Write-Host '============================================'
Write-Host ''
Write-Host "  Broker:         ${brokerProto}://${brokerIp}"
Write-Host "  Portal:         http://$portalIp"
Write-Host "  Auth Mode:      $AuthMode"
Write-Host "  API Key:        $ApiKey"
Write-Host "  (Header: X-Api-Key: $ApiKey)"
Write-Host ''
Write-Host '  Verify:'
if ($SkipTls) {
    Write-Host "    curl http://$brokerIp/healthz"
    Write-Host "    curl -H 'X-Api-Key: $ApiKey' http://$brokerIp/api/v1/sessions"
} else {
    Write-Host "    curl -k https://${brokerIp}/healthz"
    Write-Host "    curl -k -H 'X-Api-Key: $ApiKey' https://${brokerIp}/api/v1/sessions"
}
Write-Host ''
