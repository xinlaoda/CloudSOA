#Requires -Version 7.0
<#
.SYNOPSIS
    CloudSOA 容器镜像构建脚本
.DESCRIPTION
    构建 Broker 和 ServiceHost Docker 镜像并推送到 ACR
.EXAMPLE
    .\scripts\build-images.ps1 -AcrName cloudsoacr -Tag v1.0.0
    .\scripts\build-images.ps1 -NoPush
#>

[CmdletBinding()]
param(
    [string]$AcrName = '',
    [string]$Tag     = 'latest',
    [switch]$NoPush
)

$ErrorActionPreference = 'Stop'

function Write-Log  { param($Msg) Write-Host "  [✓] $Msg" -ForegroundColor Green }
function Write-Warn { param($Msg) Write-Host "  [!] $Msg" -ForegroundColor Yellow }
function Write-Err  { param($Msg) Write-Host "  [✗] $Msg" -ForegroundColor Red; exit 1 }

$ProjectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $ProjectRoot

try {
    Write-Host '============================================'
    Write-Host '  CloudSOA 镜像构建'
    Write-Host '============================================'

    # ---- 先运行测试 ----
    Write-Log '运行单元测试...'
    dotnet test --nologo --verbosity quiet --filter 'Category!=Integration'
    Write-Log '测试通过'

    # ---- 确定镜像前缀 ----
    if ($AcrName) {
        $AcrServer = "$AcrName.azurecr.io"
        if (-not $NoPush) {
            Write-Log "登录 ACR: $AcrName..."
            az acr login --name $AcrName
        }
    } else {
        $AcrServer = 'cloudsoa'
        Write-Warn '未指定 ACR，仅本地构建 (使用 -AcrName 指定)'
    }

    # ---- 构建 Broker ----
    Write-Host ''
    Write-Log '构建 Broker 镜像...'
    docker build -t "${AcrServer}/broker:${Tag}" -f src\CloudSOA.Broker\Dockerfile .
    Write-Log "Broker 镜像构建完成: ${AcrServer}/broker:${Tag}"

    # ---- 构建 ServiceHost ----
    Write-Host ''
    Write-Log '构建 ServiceHost 镜像...'
    docker build -t "${AcrServer}/servicehost:${Tag}" -f src\CloudSOA.ServiceHost\Dockerfile .
    Write-Log "ServiceHost 镜像构建完成: ${AcrServer}/servicehost:${Tag}"

    # ---- 打 latest 标签 ----
    if ($Tag -ne 'latest') {
        docker tag "${AcrServer}/broker:${Tag}" "${AcrServer}/broker:latest"
        docker tag "${AcrServer}/servicehost:${Tag}" "${AcrServer}/servicehost:latest"
    }

    # ---- 推送 ----
    if ((-not $NoPush) -and $AcrName) {
        Write-Host ''
        Write-Log '推送镜像到 ACR...'
        docker push "${AcrServer}/broker:${Tag}"
        docker push "${AcrServer}/servicehost:${Tag}"

        if ($Tag -ne 'latest') {
            docker push "${AcrServer}/broker:latest"
            docker push "${AcrServer}/servicehost:latest"
        }

        Write-Log '镜像推送完成'
    }

    Write-Host ''
    Write-Host '============================================'
    Write-Host '  ✅ 镜像构建完成！'
    Write-Host '============================================'
    Write-Host "  Broker:      ${AcrServer}/broker:${Tag}"
    Write-Host "  ServiceHost: ${AcrServer}/servicehost:${Tag}"
    Write-Host ''
} finally {
    Pop-Location
}
