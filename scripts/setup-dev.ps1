#Requires -Version 7.0
<#
.SYNOPSIS
    CloudSOA 开发环境一键安装脚本
.DESCRIPTION
    检查并安装开发所需工具，启动本地 Redis，编译项目并运行单元测试
.EXAMPLE
    .\scripts\setup-dev.ps1
#>

$ErrorActionPreference = 'Stop'

function Write-Log   { param($Msg) Write-Host "  [✓] $Msg" -ForegroundColor Green }
function Write-Warn  { param($Msg) Write-Host "  [!] $Msg" -ForegroundColor Yellow }
function Write-Err   { param($Msg) Write-Host "  [✗] $Msg" -ForegroundColor Red; exit 1 }

Write-Host '=========================================='
Write-Host '  CloudSOA 开发环境安装'
Write-Host '=========================================='
Write-Host ''

# ---- .NET 8 SDK ----
if ((Get-Command dotnet -ErrorAction SilentlyContinue) -and (dotnet --list-sdks | Select-String '^8\.')) {
    Write-Log ".NET 8 SDK 已安装 ($(dotnet --version))"
} else {
    Write-Warn '请手动安装 .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0'
}

# ---- Docker ----
if (Get-Command docker -ErrorAction SilentlyContinue) {
    Write-Log "Docker 已安装 ($(docker --version))"
} else {
    Write-Warn '请手动安装 Docker Desktop: https://docs.docker.com/desktop/install/windows-install/'
}

# ---- Azure CLI ----
if (Get-Command az -ErrorAction SilentlyContinue) {
    $azVer = (az version 2>&1 | ConvertFrom-Json).'azure-cli'
    Write-Log "Azure CLI 已安装 ($azVer)"
} else {
    Write-Warn '请安装 Azure CLI: winget install Microsoft.AzureCLI'
}

# ---- kubectl ----
if (Get-Command kubectl -ErrorAction SilentlyContinue) {
    $kVer = (kubectl version --client -o json 2>&1 | ConvertFrom-Json).clientVersion.gitVersion
    Write-Log "kubectl 已安装 ($kVer)"
} else {
    Write-Warn '请安装 kubectl: winget install Kubernetes.kubectl'
}

# ---- Helm ----
if (Get-Command helm -ErrorAction SilentlyContinue) {
    Write-Log "Helm 已安装 ($(helm version --short 2>&1))"
} else {
    Write-Warn '请安装 Helm: winget install Helm.Helm'
}

# ---- Terraform ----
if (Get-Command terraform -ErrorAction SilentlyContinue) {
    Write-Log "Terraform 已安装 ($(terraform --version 2>&1 | Select-Object -First 1))"
} else {
    Write-Warn 'Terraform 未安装，如需部署基础设施请安装: winget install Hashicorp.Terraform'
}

Write-Host ''
Write-Host '=========================================='
Write-Host '  启动本地 Redis'
Write-Host '=========================================='

if (Get-Command docker -ErrorAction SilentlyContinue) {
    $running = docker ps --format '{{.Names}}' 2>&1 | Select-String 'cloudsoa-redis'
    $exists  = docker ps -a --format '{{.Names}}' 2>&1 | Select-String 'cloudsoa-redis'

    if ($running) {
        Write-Log 'Redis 容器已在运行'
    } elseif ($exists) {
        docker start cloudsoa-redis | Out-Null
        Write-Log 'Redis 容器已启动'
    } else {
        docker run -d --name cloudsoa-redis `
            -p 6379:6379 `
            --restart unless-stopped `
            redis:7-alpine `
            redis-server --maxmemory 256mb --maxmemory-policy allkeys-lru | Out-Null
        Write-Log 'Redis 容器已创建并启动'
    }
} else {
    Write-Warn 'Docker 不可用，跳过 Redis 启动'
}

Write-Host ''
Write-Host '=========================================='
Write-Host '  编译项目'
Write-Host '=========================================='

$ProjectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $ProjectRoot
try {
    dotnet restore --verbosity quiet
    dotnet build --nologo --verbosity quiet
    Write-Log '项目编译成功'

    Write-Host ''
    Write-Host '=========================================='
    Write-Host '  运行单元测试'
    Write-Host '=========================================='

    dotnet test --nologo --filter 'Category!=Integration' --verbosity quiet
    Write-Log '单元测试全部通过'
} finally {
    Pop-Location
}

Write-Host ''
Write-Host '=========================================='
Write-Host '  ✅ 开发环境安装完成！'
Write-Host '=========================================='
Write-Host ''
Write-Host '  启动 Broker:'
Write-Host '    cd src\CloudSOA.Broker; dotnet run'
Write-Host ''
Write-Host '  端点:'
Write-Host '    REST:   http://localhost:5000'
Write-Host '    gRPC:   http://localhost:5001'
Write-Host '    健康:   http://localhost:5000/healthz'
Write-Host '    指标:   http://localhost:5000/metrics'
Write-Host ''
