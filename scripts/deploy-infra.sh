#!/usr/bin/env bash
# =============================================================================
# CloudSOA Azure 基础设施部署脚本
# 用法: ./scripts/deploy-infra.sh --prefix cloudsoa --location eastus --environment dev
# =============================================================================
set -euo pipefail

GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

log()  { echo -e "${GREEN}[✓]${NC} $*"; }
warn() { echo -e "${YELLOW}[!]${NC} $*"; }
err()  { echo -e "${RED}[✗]${NC} $*"; exit 1; }

# 默认参数
PREFIX="cloudsoa"
LOCATION="eastus"
ENVIRONMENT="dev"
AKS_NODE_COUNT=3
AKS_VM_SIZE="Standard_D4s_v3"

# 解析参数
while [[ $# -gt 0 ]]; do
    case $1 in
        --prefix)      PREFIX="$2"; shift 2 ;;
        --location)    LOCATION="$2"; shift 2 ;;
        --environment) ENVIRONMENT="$2"; shift 2 ;;
        --node-count)  AKS_NODE_COUNT="$2"; shift 2 ;;
        --vm-size)     AKS_VM_SIZE="$2"; shift 2 ;;
        -h|--help)
            echo "用法: $0 [选项]"
            echo "  --prefix NAME        资源名称前缀 (默认: cloudsoa)"
            echo "  --location REGION    Azure 区域 (默认: eastus)"
            echo "  --environment ENV    环境标识 (默认: dev)"
            echo "  --node-count N       AKS 节点数 (默认: 3)"
            echo "  --vm-size SIZE       AKS VM 规格 (默认: Standard_D4s_v3)"
            exit 0 ;;
        *) err "未知参数: $1" ;;
    esac
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
TF_DIR="${PROJECT_ROOT}/infra/terraform"

echo "============================================"
echo "  CloudSOA 基础设施部署"
echo "============================================"
echo "  前缀:     ${PREFIX}"
echo "  区域:     ${LOCATION}"
echo "  环境:     ${ENVIRONMENT}"
echo "  AKS节点:  ${AKS_NODE_COUNT} × ${AKS_VM_SIZE}"
echo "============================================"
echo ""

# ---- 检查前置条件 ----
command -v az &>/dev/null       || err "请先安装 Azure CLI"
command -v terraform &>/dev/null || err "请先安装 Terraform"
az account show &>/dev/null      || err "请先运行 az login"

SUBSCRIPTION=$(az account show --query id -o tsv)
log "当前订阅: ${SUBSCRIPTION}"

# ---- 创建 Terraform State 后端 ----
TF_RG="${PREFIX}-tfstate"
TF_SA="${PREFIX}tfstate"
TF_CONTAINER="tfstate"

echo ""
log "创建 Terraform State 存储..."

az group create -n "${TF_RG}" -l "${LOCATION}" -o none 2>/dev/null || true
az storage account create -n "${TF_SA}" -g "${TF_RG}" -l "${LOCATION}" \
    --sku Standard_LRS -o none 2>/dev/null || true
az storage container create -n "${TF_CONTAINER}" \
    --account-name "${TF_SA}" -o none 2>/dev/null || true

log "State 存储就绪: ${TF_SA}/${TF_CONTAINER}"

# ---- 生成 Terraform 变量文件 ----
cd "${TF_DIR}"

cat > terraform.tfvars <<EOF
prefix         = "${PREFIX}"
location       = "${LOCATION}"
aks_node_count = ${AKS_NODE_COUNT}
aks_vm_size    = "${AKS_VM_SIZE}"
tags = {
  project     = "CloudSOA"
  environment = "${ENVIRONMENT}"
  managed_by  = "terraform"
}
EOF

log "已生成 terraform.tfvars"

cat > backend.tfvars <<EOF
resource_group_name  = "${TF_RG}"
storage_account_name = "${TF_SA}"
container_name       = "${TF_CONTAINER}"
key                  = "${PREFIX}.${ENVIRONMENT}.tfstate"
EOF

log "已生成 backend.tfvars"

# ---- Terraform Init & Apply ----
echo ""
log "Terraform init..."
terraform init -backend-config=backend.tfvars -input=false

echo ""
log "Terraform plan..."
terraform plan -out=tfplan -input=false

echo ""
echo "============================================"
echo "  即将创建以下资源:"
echo "  - Resource Group: ${PREFIX}-rg"
echo "  - AKS Cluster:    ${PREFIX}-aks"
echo "  - ACR:            ${PREFIX}acr"
echo "  - Redis Cache:    ${PREFIX}-redis"
echo "  - Service Bus:    ${PREFIX}-sb"
echo "  - CosmosDB:       ${PREFIX}-cosmos"
echo "============================================"
read -p "确认部署? (y/N) " -n 1 -r
echo ""

if [[ ! $REPLY =~ ^[Yy]$ ]]; then
    warn "已取消部署"
    exit 0
fi

echo ""
log "Terraform apply..."
terraform apply tfplan

echo ""
log "保存输出..."
terraform output -json > "${PROJECT_ROOT}/deploy/infra-outputs.json"

# ---- 获取 AKS 凭证 ----
echo ""
log "获取 AKS 凭证..."
az aks get-credentials \
    --resource-group "${PREFIX}-rg" \
    --name "${PREFIX}-aks" \
    --overwrite-existing

kubectl get nodes

# ---- 输出摘要 ----
echo ""
echo "============================================"
echo "  ✅ 基础设施部署完成！"
echo "============================================"
echo ""
echo "  AKS 集群:  $(terraform output -raw aks_name)"
echo "  ACR 地址:  $(terraform output -raw acr_login_server)"
echo "  Redis:     $(terraform output -raw redis_hostname)"
echo ""
echo "  输出已保存到: deploy/infra-outputs.json"
echo ""
echo "  下一步:"
echo "    1. ./scripts/build-images.sh --acr $(terraform output -raw acr_login_server | cut -d. -f1) --tag v1.0.0"
echo "    2. ./scripts/deploy-k8s.sh"
echo ""
