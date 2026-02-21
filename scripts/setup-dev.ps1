#Requires -Version 7.0
<#
.SYNOPSIS
    CloudSOA Dev Environment Setup Script
.DESCRIPTION
    Check and install development tools, start local Redis, build project and run unit tests
.EXAMPLE
    .\scripts\setup-dev.ps1
#>

$ErrorActionPreference = 'Stop'

function Write-Log   { param($Msg) Write-Host "  [✓] $Msg" -ForegroundColor Green }
function Write-Warn  { param($Msg) Write-Host "  [!] $Msg" -ForegroundColor Yellow }
function Write-Err   { param($Msg) Write-Host "  [✗] $Msg" -ForegroundColor Red; exit 1 }

Write-Host '=========================================='
Write-Host '  CloudSOA Dev Environment Setup'
Write-Host '=========================================='
Write-Host ''

# ---- .NET 8 SDK ----
if ((Get-Command dotnet -ErrorAction SilentlyContinue) -and (dotnet --list-sdks | Select-String '^8\.')) {
    Write-Log ".NET 8 SDK installed ($(dotnet --version))"
} else {
    Write-Warn 'Please install .NET 8 SDK manually: https://dotnet.microsoft.com/download/dotnet/8.0'
}

# ---- Docker ----
if (Get-Command docker -ErrorAction SilentlyContinue) {
    Write-Log "Docker installed ($(docker --version))"
} else {
    Write-Warn 'Please install Docker Desktop manually: https://docs.docker.com/desktop/install/windows-install/'
}

# ---- Azure CLI ----
if (Get-Command az -ErrorAction SilentlyContinue) {
    $azVer = (az version 2>&1 | ConvertFrom-Json).'azure-cli'
    Write-Log "Azure CLI installed ($azVer)"
} else {
    Write-Warn 'Please install Azure CLI: winget install Microsoft.AzureCLI'
}

# ---- kubectl ----
if (Get-Command kubectl -ErrorAction SilentlyContinue) {
    $kVer = (kubectl version --client -o json 2>&1 | ConvertFrom-Json).clientVersion.gitVersion
    Write-Log "kubectl installed ($kVer)"
} else {
    Write-Warn 'Please install kubectl: winget install Kubernetes.kubectl'
}

# ---- Helm ----
if (Get-Command helm -ErrorAction SilentlyContinue) {
    Write-Log "Helm installed ($(helm version --short 2>&1))"
} else {
    Write-Warn 'Please install Helm: winget install Helm.Helm'
}

# ---- Terraform ----
if (Get-Command terraform -ErrorAction SilentlyContinue) {
    Write-Log "Terraform installed ($(terraform --version 2>&1 | Select-Object -First 1))"
} else {
    Write-Warn 'Terraform not installed, please install if needed for infrastructure deployment: winget install Hashicorp.Terraform'
}

Write-Host ''
Write-Host '=========================================='
Write-Host '  Start local Redis'
Write-Host '=========================================='

if (Get-Command docker -ErrorAction SilentlyContinue) {
    $running = docker ps --format '{{.Names}}' 2>&1 | Select-String 'cloudsoa-redis'
    $exists  = docker ps -a --format '{{.Names}}' 2>&1 | Select-String 'cloudsoa-redis'

    if ($running) {
        Write-Log 'Redis container already running'
    } elseif ($exists) {
        docker start cloudsoa-redis | Out-Null
        Write-Log 'Redis container started'
    } else {
        docker run -d --name cloudsoa-redis `
            -p 6379:6379 `
            --restart unless-stopped `
            redis:7-alpine `
            redis-server --maxmemory 256mb --maxmemory-policy allkeys-lru | Out-Null
        Write-Log 'Redis container created and started'
    }
} else {
    Write-Warn 'Docker not available, skipping Redis startup'
}

Write-Host ''
Write-Host '=========================================='
Write-Host '  Build project'
Write-Host '=========================================='

$ProjectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $ProjectRoot
try {
    dotnet restore --verbosity quiet
    dotnet build --nologo --verbosity quiet
    Write-Log 'Build succeeded'

    Write-Host ''
    Write-Host '=========================================='
    Write-Host '  Run unit tests'
    Write-Host '=========================================='

    dotnet test --nologo --filter 'Category!=Integration' --verbosity quiet
    Write-Log 'All unit tests passed'
} finally {
    Pop-Location
}

Write-Host ''
Write-Host '=========================================='
Write-Host '  ✅ Dev environment setup complete!'
Write-Host '=========================================='
Write-Host ''
Write-Host '  Start Broker:'
Write-Host '    cd src\CloudSOA.Broker; dotnet run'
Write-Host ''
Write-Host '  Endpoints:'
Write-Host '    REST:   http://localhost:5000'
Write-Host '    gRPC:   http://localhost:5001'
Write-Host '    Health: http://localhost:5000/healthz'
Write-Host '    Metrics: http://localhost:5000/metrics'
Write-Host ''
