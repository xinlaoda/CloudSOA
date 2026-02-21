#Requires -Version 7.0
<#
.SYNOPSIS
    CloudSOA Azure 基础设施部署脚本
.DESCRIPTION
    使用 Terraform 部署 AKS、ACR、Redis、Service Bus、CosmosDB 等 Azure 资源
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
    [string]$ResourceGroupName  = ''
)

$ErrorActionPreference = 'Stop'

function Write-Log  { param($Msg) Write-Host "  [✓] $Msg" -ForegroundColor Green }
function Write-Warn { param($Msg) Write-Host "  [!] $Msg" -ForegroundColor Yellow }
function Write-Err  { param($Msg) Write-Host "  [✗] $Msg" -ForegroundColor Red; exit 1 }

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$TfDir       = Join-Path $ProjectRoot 'infra\terraform'

Write-Host '============================================'
Write-Host '  CloudSOA 基础设施部署'
Write-Host '============================================'
Write-Host "  前缀:     $Prefix"
Write-Host "  区域:     $Location"
Write-Host "  环境:     $Environment"
Write-Host "  AKS节点:  $AksNodeCount × $AksVmSize"
Write-Host "  计算节点:  Linux=$AksComputeVmSize, Windows=$AksWinComputeVmSize"
Write-Host '============================================'
Write-Host ''

# ---- 检查前置条件 ----
if (-not (Get-Command az -ErrorAction SilentlyContinue))        { Write-Err '请先安装 Azure CLI' }
if (-not (Get-Command terraform -ErrorAction SilentlyContinue)) { Write-Err '请先安装 Terraform' }

try { az account show 2>&1 | Out-Null } catch { Write-Err '请先运行 az login' }

$Subscription = az account show --query id -o tsv
Write-Log "当前订阅: $Subscription"

# ---- 创建 Terraform State 后端 ----
$TfRg        = "$Prefix-tfstate"
$TfSa        = "${Prefix}tfstate"
$TfContainer = 'tfstate'

Write-Host ''
Write-Log '创建 Terraform State 存储...'

az group create -n $TfRg -l $Location -o none 2>&1 | Out-Null
az storage account create -n $TfSa -g $TfRg -l $Location --sku Standard_LRS -o none 2>&1 | Out-Null
az storage container create -n $TfContainer --account-name $TfSa -o none 2>&1 | Out-Null

Write-Log "State 存储就绪: $TfSa/$TfContainer"

# ---- 生成 Terraform 变量文件 ----
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

Write-Log '已生成 terraform.tfvars'

@"
resource_group_name  = "$TfRg"
storage_account_name = "$TfSa"
container_name       = "$TfContainer"
key                  = "$Prefix.$Environment.tfstate"
"@ | Set-Content -Path 'backend.tfvars' -Encoding utf8

Write-Log '已生成 backend.tfvars'

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
Write-Host '  即将创建以下资源:'
Write-Host "  - Resource Group: $RgActual"
Write-Host "  - AKS Cluster:    $Prefix-aks"
Write-Host "  - ACR:            ${Prefix}acr"
Write-Host "  - Redis Cache:    $Prefix-redis"
Write-Host "  - Service Bus:    $Prefix-sb"
Write-Host "  - CosmosDB:       $Prefix-cosmos"
Write-Host '============================================'

$reply = Read-Host '确认部署? (y/N)'
if ($reply -notin @('y', 'Y')) {
    Write-Warn '已取消部署'
    Pop-Location
    exit 0
}

Write-Host ''
Write-Log 'Terraform apply...'
terraform apply tfplan

Write-Host ''
Write-Log '保存输出...'
$outputPath = Join-Path $ProjectRoot 'deploy\infra-outputs.json'
terraform output -json | Set-Content -Path $outputPath -Encoding utf8

# ---- 获取 AKS 凭证 ----
Write-Host ''
Write-Log '获取 AKS 凭证...'
$rgName  = terraform output -raw resource_group_name
$aksName = terraform output -raw aks_name
az aks get-credentials --resource-group $rgName --name $aksName --overwrite-existing
kubectl get nodes

Pop-Location

# ---- 输出摘要 ----
Push-Location $TfDir
$aksName       = terraform output -raw aks_name
$acrServer     = terraform output -raw acr_login_server
$redisHostname = terraform output -raw redis_hostname
$acrShort      = ($acrServer -split '\.')[0]
Pop-Location

Write-Host ''
Write-Host '============================================'
Write-Host '  ✅ 基础设施部署完成！'
Write-Host '============================================'
Write-Host ''
Write-Host "  AKS 集群:  $aksName"
Write-Host "  ACR 地址:  $acrServer"
Write-Host "  Redis:     $redisHostname"
Write-Host ''
Write-Host "  输出已保存到: deploy\infra-outputs.json"
Write-Host ''
Write-Host '  下一步:'
Write-Host "    1. .\scripts\build-images.ps1 -AcrName $acrShort -Tag v1.0.0"
Write-Host '    2. .\scripts\deploy-k8s.ps1'
Write-Host ''
