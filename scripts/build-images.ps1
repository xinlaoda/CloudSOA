#Requires -Version 7.0
<#
.SYNOPSIS
    CloudSOA Container Image Build Script
.DESCRIPTION
    Builds all CloudSOA Docker images (Broker, Portal, ServiceManager,
    ServiceHost, ServiceHost-CoreWcf) and optionally pushes to ACR.
    Use -UseAcrBuild for remote builds (no local Docker required).
.EXAMPLE
    .\scripts\build-images.ps1 -AcrName cloudsoacr -Tag v1.8.0
    .\scripts\build-images.ps1 -AcrName cloudsoacr -Tag v1.8.0 -UseAcrBuild
    .\scripts\build-images.ps1 -NoPush
    .\scripts\build-images.ps1 -AcrName cloudsoacr -Tag v1.8.0 -Images broker,portal
#>

[CmdletBinding()]
param(
    [string]$AcrName    = '',
    [string]$Tag        = 'latest',
    [switch]$NoPush,
    [switch]$UseAcrBuild,
    [switch]$SkipTests,
    [string[]]$Images   = @()
)

$ErrorActionPreference = 'Stop'

function Write-Log  { param($Msg) Write-Host "  [✓] $Msg" -ForegroundColor Green }
function Write-Warn { param($Msg) Write-Host "  [!] $Msg" -ForegroundColor Yellow }
function Write-Err  { param($Msg) Write-Host "  [✗] $Msg" -ForegroundColor Red; exit 1 }

$ProjectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $ProjectRoot

try {
    # ---- Image definitions ----
    $AllImages = [ordered]@{
        'broker'              = @{ Dockerfile = 'src\CloudSOA.Broker\Dockerfile';              Platform = 'linux' }
        'portal'              = @{ Dockerfile = 'src\CloudSOA.Portal\Dockerfile';               Platform = 'linux' }
        'servicemanager'      = @{ Dockerfile = 'src\CloudSOA.ServiceManager\Dockerfile';       Platform = 'linux' }
        'servicehost'         = @{ Dockerfile = 'src\CloudSOA.ServiceHost\Dockerfile';          Platform = 'linux' }
        'servicehost-corewcf' = @{ Dockerfile = 'src\CloudSOA.ServiceHost.CoreWcf\Dockerfile';  Platform = 'linux' }
    }

    # Filter images if user specified a subset
    if ($Images.Count -gt 0) {
        $filtered = [ordered]@{}
        foreach ($img in $Images) {
            if ($AllImages.Contains($img)) { $filtered[$img] = $AllImages[$img] }
            else { Write-Warn "Unknown image '$img', skipping. Valid: $($AllImages.Keys -join ', ')" }
        }
        if ($filtered.Count -eq 0) { Write-Err "No valid images specified" }
        $AllImages = $filtered
    }

    Write-Host '============================================'
    Write-Host '  CloudSOA Image Build'
    Write-Host '============================================'
    Write-Host "  Images: $($AllImages.Keys -join ', ')"
    Write-Host "  Tag:    $Tag"
    Write-Host "  Method: $(if ($UseAcrBuild) { 'ACR Build (remote)' } else { 'Docker (local)' })"
    Write-Host '============================================'
    Write-Host ''

    # ---- Run tests ----
    if (-not $SkipTests) {
        Write-Log 'Running unit tests...'
        dotnet test --nologo --verbosity quiet --filter 'Category!=Integration'
        Write-Log 'Tests passed'
    } else {
        Write-Warn 'Skipping tests (-SkipTests)'
    }

    # ---- Determine image prefix ----
    if ($AcrName) {
        $AcrServer = "$AcrName.azurecr.io"
        if (-not $UseAcrBuild -and -not $NoPush) {
            Write-Log "Logging into ACR: $AcrName..."
            az acr login --name $AcrName
        }
    } else {
        $AcrServer = 'cloudsoa'
        Write-Warn 'No ACR specified, local build only (use -AcrName to specify)'
    }

    # ---- Build each image ----
    $builtImages = @()
    foreach ($name in $AllImages.Keys) {
        $def = $AllImages[$name]
        $fullTag = "${AcrServer}/${name}:${Tag}"
        Write-Host ''

        if ($UseAcrBuild -and $AcrName) {
            Write-Log "Building $name via ACR Build..."
            $platform = if ($def.Platform -eq 'windows') { '--platform windows' } else { '--platform linux' }
            az acr build --registry $AcrName --image "${name}:${Tag}" --file $def.Dockerfile $platform . 2>&1 |
                Select-Object -Last 5 | ForEach-Object { Write-Host "    $_" }
        } else {
            Write-Log "Building $name (docker)..."
            docker build -t $fullTag -f $def.Dockerfile .
        }
        Write-Log "$name built: $fullTag"
        $builtImages += $fullTag

        # Tag latest
        if ($Tag -ne 'latest' -and -not $UseAcrBuild) {
            docker tag $fullTag "${AcrServer}/${name}:latest"
        }
    }

    # ---- Push ----
    if ((-not $NoPush) -and $AcrName -and (-not $UseAcrBuild)) {
        Write-Host ''
        Write-Log 'Pushing images to ACR...'
        foreach ($name in $AllImages.Keys) {
            docker push "${AcrServer}/${name}:${Tag}"
            if ($Tag -ne 'latest') { docker push "${AcrServer}/${name}:latest" }
        }
        Write-Log 'Push complete'
    }

    Write-Host ''
    Write-Host '============================================'
    Write-Host '  ✅ Image Build Complete!'
    Write-Host '============================================'
    foreach ($img in $builtImages) { Write-Host "  $img" }
    Write-Host ''
} finally {
    Pop-Location
}
