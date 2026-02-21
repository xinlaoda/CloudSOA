#Requires -Version 7.0
<#
.SYNOPSIS
    CloudSOA Azure Infrastructure Deployment Script
.DESCRIPTION
    Deploy Azure resources (AKS, ACR, Redis, Service Bus, CosmosDB) using Terraform
.EXAMPLE
    .\scripts\deploy-infra.ps1 -Prefix cloudsoa -Location eastus -Environment dev
#>

[CmdletBinding()]
param(
    [string]$Prefix      = 'cloudsoa',
    [string]$Location    = 'eastus',
    [string]$Environment = 'dev',
    [int]$AksNodeCount          = 3,
    [string]$AksVmSize          = 'Standard_D4_v2',
    [string]$AksComputeVmSize   = 'Standard_D4_v2',
    [string]$AksWinComputeVmSize = 'Standard_D4_v2',
    [string]$ResourceGroupName  = '',
    [switch]$AutoApprove
)

$ErrorActionPreference = 'Stop'

function Write-Log  { param($Msg) Write-Host "  [✓] $Msg" -ForegroundColor Green }
function Write-Warn { param($Msg) Write-Host "  [!] $Msg" -ForegroundColor Yellow }
function Write-Err  { param($Msg) Write-Host "  [✗] $Msg" -ForegroundColor Red; exit 1 }

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$TfDir       = Join-Path $ProjectRoot 'infra\terraform'

Write-Host '============================================'
Write-Host '  CloudSOA Infrastructure Deployment'
Write-Host '============================================'
Write-Host "  Prefix:         $Prefix"
Write-Host "  Region:         $Location"
Write-Host "  Environment:    $Environment"
Write-Host "  AKS Nodes:      $AksNodeCount × $AksVmSize"
Write-Host "  Compute Nodes:  Linux=$AksComputeVmSize, Windows=$AksWinComputeVmSize"
Write-Host '============================================'
Write-Host ''

# ---- Check prerequisites ----
if (-not (Get-Command az -ErrorAction SilentlyContinue))        { Write-Err 'Please install Azure CLI first' }
if (-not (Get-Command terraform -ErrorAction SilentlyContinue)) { Write-Err 'Please install Terraform first' }

try { az account show 2>&1 | Out-Null } catch { Write-Err 'Please run az login first' }

$Subscription = az account show --query id -o tsv
Write-Log "Current subscription: $Subscription"

# ---- Create Terraform state backend ----
$TfRg        = "$Prefix-tfstate"
$TfSa        = "${Prefix}tfstate"
$TfContainer = 'tfstate'

Write-Host ''
Write-Log 'Creating Terraform state storage...'

az group create -n $TfRg -l $Location -o none 2>&1 | Out-Null
az storage account create -n $TfSa -g $TfRg -l $Location --sku Standard_LRS -o none 2>&1 | Out-Null
az storage container create -n $TfContainer --account-name $TfSa -o none 2>&1 | Out-Null

Write-Log "State storage ready: $TfSa/$TfContainer"

# ---- Generate Terraform variable files ----
Push-Location $TfDir

$rgNameLine = if ($ResourceGroupName) { "resource_group_name = `"$ResourceGroupName`"" } else { "# resource_group_name uses default: {prefix}-rg" }
$RgActual = if ($ResourceGroupName) { $ResourceGroupName } else { "$Prefix-rg" }

@"
prefix                  = "$Prefix"
location                = "$Location"
aks_node_count          = $AksNodeCount
aks_vm_size             = "$AksVmSize"
aks_compute_vm_size     = "$AksComputeVmSize"
aks_win_compute_vm_size = "$AksWinComputeVmSize"
$rgNameLine
tags = {
  project     = "CloudSOA"
  environment = "$Environment"
  managed_by  = "terraform"
}
"@ | Set-Content -Path 'terraform.tfvars' -Encoding utf8

Write-Log 'Generated terraform.tfvars'

@"
resource_group_name  = "$TfRg"
storage_account_name = "$TfSa"
container_name       = "$TfContainer"
key                  = "$Prefix.$Environment.tfstate"
"@ | Set-Content -Path 'backend.tfvars' -Encoding utf8

Write-Log 'Generated backend.tfvars'

# ---- Terraform Init & Apply ----
# Clean old state if exists
if (Test-Path '.terraform') { Remove-Item -Recurse -Force .terraform }
if (Test-Path '.terraform.lock.hcl') { Remove-Item -Force .terraform.lock.hcl }

Write-Host ''
Write-Log 'Terraform init...'
terraform init "-backend-config=backend.tfvars" -input=false

Write-Host ''
Write-Log 'Terraform plan...'
terraform plan -out=tfplan -input=false

Write-Host ''
Write-Host '============================================'
Write-Host '  Resources to be created:'
Write-Host "  - Resource Group: $RgActual"
Write-Host "  - AKS Cluster:    $Prefix-aks"
Write-Host "  - ACR:            ${Prefix}acr"
Write-Host "  - Redis Cache:    $Prefix-redis"
Write-Host "  - Service Bus:    $Prefix-sb"
Write-Host "  - CosmosDB:       $Prefix-cosmos"
Write-Host '============================================'

if (-not $AutoApprove) {
    $reply = Read-Host 'Confirm deployment? (y/N)'
    if ($reply -notin @('y', 'Y')) {
        Write-Warn 'Deployment cancelled'
        Pop-Location
        exit 0
    }
} else {
    Write-Log 'Auto-confirmed deployment (-AutoApprove)'
}

Write-Host ''
Write-Log 'Terraform apply...'
terraform apply tfplan

Write-Host ''
Write-Log 'Saving outputs...'
$outputPath = Join-Path $ProjectRoot 'deploy\infra-outputs.json'
terraform output -json | Set-Content -Path $outputPath -Encoding utf8

# ---- Get AKS credentials ----
Write-Host ''
Write-Log 'Getting AKS credentials...'
$rgName  = terraform output -raw resource_group_name
$aksName = terraform output -raw aks_name
az aks get-credentials --resource-group $rgName --name $aksName --overwrite-existing
kubectl get nodes

# ---- Attach ACR to AKS ----
Write-Host ''
$acrLoginServer = terraform output -raw acr_login_server
$acrAttachName = ($acrLoginServer -split '\.')[0]
Write-Log "Attaching ACR ($acrAttachName) to AKS ($aksName)..."
az aks update --resource-group $rgName --name $aksName --attach-acr $acrAttachName

Pop-Location

# ---- Output summary ----
Push-Location $TfDir
$aksName       = terraform output -raw aks_name
$acrServer     = terraform output -raw acr_login_server
$redisHostname = terraform output -raw redis_hostname
$acrShort      = ($acrServer -split '\.')[0]
Pop-Location

Write-Host ''
Write-Host '============================================'
Write-Host '  ✅ Infrastructure deployment complete!'
Write-Host '============================================'
Write-Host ''
Write-Host "  AKS Cluster:  $aksName"
Write-Host "  ACR Server:   $acrServer"
Write-Host "  Redis:     $redisHostname"
Write-Host ''
Write-Host "  Outputs saved to: deploy\infra-outputs.json"
Write-Host ''
Write-Host '  Next steps:'
Write-Host "    1. .\scripts\build-images.ps1 -AcrName $acrShort -Tag v1.0.0"
Write-Host '    2. .\scripts\deploy-k8s.ps1'
Write-Host ''
