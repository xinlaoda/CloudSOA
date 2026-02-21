#!/usr/bin/env bash
# =============================================================================
# CloudSOA Azure Infrastructure Deployment Script
# Usage: ./scripts/deploy-infra.sh --prefix cloudsoa --location eastus --environment dev
# =============================================================================
set -euo pipefail

GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

log()  { echo -e "${GREEN}[✓]${NC} $*"; }
warn() { echo -e "${YELLOW}[!]${NC} $*"; }
err()  { echo -e "${RED}[✗]${NC} $*"; exit 1; }

# Default parameters
PREFIX="cloudsoa"
LOCATION="eastus"
ENVIRONMENT="dev"
AKS_NODE_COUNT=3
AKS_VM_SIZE="Standard_D4s_v3"

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --prefix)      PREFIX="$2"; shift 2 ;;
        --location)    LOCATION="$2"; shift 2 ;;
        --environment) ENVIRONMENT="$2"; shift 2 ;;
        --node-count)  AKS_NODE_COUNT="$2"; shift 2 ;;
        --vm-size)     AKS_VM_SIZE="$2"; shift 2 ;;
        -h|--help)
            echo "Usage: $0 [options]"
            echo "  --prefix NAME        Resource name prefix (default: cloudsoa)"
            echo "  --location REGION    Azure region (default: eastus)"
            echo "  --environment ENV    Environment identifier (default: dev)"
            echo "  --node-count N       AKS node count (default: 3)"
            echo "  --vm-size SIZE       AKS VM size (default: Standard_D4s_v3)"
            exit 0 ;;
        *) err "Unknown argument: $1" ;;
    esac
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
TF_DIR="${PROJECT_ROOT}/infra/terraform"

echo "============================================"
echo "  CloudSOA Infrastructure Deployment"
echo "============================================"
echo "  Prefix:     ${PREFIX}"
echo "  Region:     ${LOCATION}"
echo "  Environment: ${ENVIRONMENT}"
echo "  AKS Nodes:  ${AKS_NODE_COUNT} × ${AKS_VM_SIZE}"
echo "============================================"
echo ""

# ---- Check prerequisites ----
command -v az &>/dev/null       || err "Please install Azure CLI first"
command -v terraform &>/dev/null || err "Please install Terraform first"
az account show &>/dev/null      || err "Please run az login first"

SUBSCRIPTION=$(az account show --query id -o tsv)
log "Current subscription: ${SUBSCRIPTION}"

# ---- Create Terraform state backend ----
TF_RG="${PREFIX}-tfstate"
TF_SA="${PREFIX}tfstate"
TF_CONTAINER="tfstate"

echo ""
log "Creating Terraform state storage..."

az group create -n "${TF_RG}" -l "${LOCATION}" -o none 2>/dev/null || true
az storage account create -n "${TF_SA}" -g "${TF_RG}" -l "${LOCATION}" \
    --sku Standard_LRS -o none 2>/dev/null || true
az storage container create -n "${TF_CONTAINER}" \
    --account-name "${TF_SA}" -o none 2>/dev/null || true

log "State storage ready: ${TF_SA}/${TF_CONTAINER}"

# ---- Generate Terraform variable files ----
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

log "Generated terraform.tfvars"

cat > backend.tfvars <<EOF
resource_group_name  = "${TF_RG}"
storage_account_name = "${TF_SA}"
container_name       = "${TF_CONTAINER}"
key                  = "${PREFIX}.${ENVIRONMENT}.tfstate"
EOF

log "Generated backend.tfvars"

# ---- Terraform Init & Apply ----
echo ""
log "Terraform init..."
terraform init -backend-config=backend.tfvars -input=false

echo ""
log "Terraform plan..."
terraform plan -out=tfplan -input=false

echo ""
echo "============================================"
echo "  Resources to be created:"
echo "  - Resource Group: ${PREFIX}-rg"
echo "  - AKS Cluster:    ${PREFIX}-aks"
echo "  - ACR:            ${PREFIX}acr"
echo "  - Redis Cache:    ${PREFIX}-redis"
echo "  - Service Bus:    ${PREFIX}-sb"
echo "  - CosmosDB:       ${PREFIX}-cosmos"
echo "============================================"
read -p "Confirm deployment? (y/N) " -n 1 -r
echo ""

if [[ ! $REPLY =~ ^[Yy]$ ]]; then
    warn "Deployment cancelled"
    exit 0
fi

echo ""
log "Terraform apply..."
terraform apply tfplan

echo ""
log "Saving outputs..."
terraform output -json > "${PROJECT_ROOT}/deploy/infra-outputs.json"

# ---- Get AKS credentials ----
echo ""
log "Getting AKS credentials..."
az aks get-credentials \
    --resource-group "${PREFIX}-rg" \
    --name "${PREFIX}-aks" \
    --overwrite-existing

kubectl get nodes

# ---- Output summary ----
echo ""
echo "============================================"
echo "  ✅ Infrastructure deployment complete!"
echo "============================================"
echo ""
echo "  AKS Cluster:  $(terraform output -raw aks_name)"
echo "  ACR Server:   $(terraform output -raw acr_login_server)"
echo "  Redis:     $(terraform output -raw redis_hostname)"
echo ""
echo "  Outputs saved to: deploy/infra-outputs.json"
echo ""
echo "  Next steps:"
echo "    1. ./scripts/build-images.sh --acr $(terraform output -raw acr_login_server | cut -d. -f1) --tag v1.0.0"
echo "    2. ./scripts/deploy-k8s.sh"
echo ""
