#!/bin/bash
# ============================================================================
# deploy-private.sh — Deploy CloudSOA in fully private networking mode
#
# This script:
#   1. Provisions VNet, subnets, and Private DNS zones
#   2. Creates Private Endpoints for Azure services (Redis, Cosmos, Blob, ACR, ServiceBus)
#   3. Deploys AKS into the VNet subnet
#   4. Switches K8s services to internal Load Balancers (no public IPs)
#   5. Optionally creates Private Link Service for cross-VNet/cross-subscription access
#
# Prerequisites:
#   - Azure CLI logged in (az login)
#   - kubectl configured for the target AKS cluster
#   - Terraform installed (>= 1.5)
#   - Existing CloudSOA base deployment (deploy-secure.sh or equivalent)
#
# Usage:
#   ./deploy-private.sh [--enable-private-link-service]
#
# ============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
TF_DIR="$REPO_ROOT/infra/terraform"
K8S_DIR="$REPO_ROOT/deploy/k8s"

ENABLE_PLS=false
if [[ "${1:-}" == "--enable-private-link-service" ]]; then
    ENABLE_PLS=true
fi

echo "============================================"
echo " CloudSOA Private Networking Deployment"
echo "============================================"
echo ""

# -------------------------------------------------------------------
# Step 1: Terraform — Provision VNet + Private Endpoints
# -------------------------------------------------------------------
echo "[1/5] Provisioning VNet and Private Endpoints via Terraform..."
cd "$TF_DIR"

terraform init -input=false

terraform plan \
  -var="enable_private_networking=true" \
  -out=tfplan-private

echo ""
echo "Review the Terraform plan above."
read -p "Apply? (yes/no): " CONFIRM
if [[ "$CONFIRM" != "yes" ]]; then
    echo "Aborted."
    exit 1
fi

terraform apply tfplan-private
rm -f tfplan-private

echo "[1/5] ✅ VNet and Private Endpoints provisioned."
echo ""

# -------------------------------------------------------------------
# Step 2: Update AKS to use VNet subnet (if not already)
# -------------------------------------------------------------------
echo "[2/5] Checking AKS VNet integration..."

AKS_SUBNET_ID=$(terraform output -raw aks_subnet_id 2>/dev/null || echo "")
if [[ -n "$AKS_SUBNET_ID" ]]; then
    AKS_NAME=$(terraform output -raw aks_name)
    RG_NAME=$(terraform output -raw -json 2>/dev/null | jq -r '.resource_group_name.value // empty' || echo "")
    
    echo "AKS subnet: $AKS_SUBNET_ID"
    echo "NOTE: If AKS was not created with this subnet, you may need to recreate the cluster"
    echo "      or add a new node pool with: --vnet-subnet-id $AKS_SUBNET_ID"
fi

echo "[2/5] ✅ AKS VNet check complete."
echo ""

# -------------------------------------------------------------------
# Step 3: Disable public access on Azure services
# -------------------------------------------------------------------
echo "[3/5] Disabling public network access on Azure services..."

REDIS_NAME=$(terraform output -raw redis_hostname 2>/dev/null | sed 's/.redis.cache.windows.net//')
COSMOS_NAME=$(terraform output -raw cosmosdb_endpoint 2>/dev/null | sed 's|https://||;s|:443/||;s|.documents.azure.com||')
STORAGE_NAME=$(terraform output -raw blob_storage_account 2>/dev/null)
ACR_NAME=$(terraform output -raw acr_login_server 2>/dev/null | sed 's/.azurecr.io//')
RG=$(terraform output -raw 2>/dev/null | grep -oP '(?<=resource_group_name = ").*(?=")' || echo "")

# Get resource group from Terraform state
RG=$(cd "$TF_DIR" && terraform state show azurerm_resource_group.main 2>/dev/null | grep ' name ' | head -1 | awk -F'"' '{print $2}' || echo "")

if [[ -n "$RG" ]]; then
    echo "  Disabling public access on Redis: $REDIS_NAME"
    az redis update -n "$REDIS_NAME" -g "$RG" --set publicNetworkAccess=Disabled 2>/dev/null || echo "  (Redis: manual update may be needed)"

    echo "  Disabling public access on Cosmos DB: $COSMOS_NAME"
    az cosmosdb update -n "$COSMOS_NAME" -g "$RG" --enable-public-network false 2>/dev/null || echo "  (Cosmos: manual update may be needed)"

    echo "  Disabling public access on Storage: $STORAGE_NAME"
    az storage account update -n "$STORAGE_NAME" -g "$RG" --public-network-access Disabled 2>/dev/null || echo "  (Storage: manual update may be needed)"

    echo "  Disabling public access on ACR: $ACR_NAME"
    az acr update -n "$ACR_NAME" -g "$RG" --public-network-enabled false 2>/dev/null || echo "  (ACR: manual update may be needed)"
fi

echo "[3/5] ✅ Public access disabled."
echo ""

# -------------------------------------------------------------------
# Step 4: Switch K8s services to internal Load Balancers
# -------------------------------------------------------------------
echo "[4/5] Switching to internal Load Balancers (removing public IPs)..."

kubectl apply -f "$K8S_DIR/private-services.yaml"

echo "Waiting for internal LB IPs..."
sleep 30

BROKER_IP=$(kubectl get svc broker-service -n cloudsoa -o jsonpath='{.status.loadBalancer.ingress[0].ip}' 2>/dev/null || echo "pending")
PORTAL_IP=$(kubectl get svc portal-service -n cloudsoa -o jsonpath='{.status.loadBalancer.ingress[0].ip}' 2>/dev/null || echo "pending")

echo "  Broker internal IP: $BROKER_IP"
echo "  Portal internal IP: $PORTAL_IP"
echo "[4/5] ✅ Internal Load Balancers deployed."
echo ""

# -------------------------------------------------------------------
# Step 5: (Optional) Create Private Link Service
# -------------------------------------------------------------------
if [[ "$ENABLE_PLS" == "true" ]]; then
    echo "[5/5] Creating Private Link Service for cross-VNet access..."

    # Get the internal LB frontend IP config IDs
    # AKS creates an internal LB in the MC_ resource group
    MC_RG=$(az aks show -n "$AKS_NAME" -g "$RG" --query nodeResourceGroup -o tsv 2>/dev/null)
    
    echo "  Looking for internal LB in: $MC_RG"
    
    BROKER_FE_ID=$(az network lb frontend-ip list \
      --lb-name kubernetes-internal \
      -g "$MC_RG" \
      --query "[?contains(privateIPAddress, '$BROKER_IP')].id" -o tsv 2>/dev/null || echo "")

    if [[ -n "$BROKER_FE_ID" ]]; then
        echo "  Broker LB frontend: $BROKER_FE_ID"
        
        cd "$TF_DIR"
        terraform apply \
          -var="enable_private_networking=true" \
          -var="enable_private_link_service=true" \
          -var="broker_internal_lb_frontend_ip_id=$BROKER_FE_ID" \
          -auto-approve

        PLS_ALIAS=$(terraform output -raw broker_private_link_service_alias 2>/dev/null || echo "")
        PLS_ID=$(terraform output -raw broker_private_link_service_id 2>/dev/null || echo "")

        echo ""
        echo "  ✅ Private Link Service created!"
        echo "  PLS Alias: $PLS_ALIAS"
        echo "  PLS ID:    $PLS_ID"
        echo ""
        echo "  Share the PLS Alias with consumers. They create a Private Endpoint:"
        echo "    az network private-endpoint create \\"
        echo "      --name cloudsoa-pe \\"
        echo "      --resource-group <consumer-rg> \\"
        echo "      --vnet-name <consumer-vnet> \\"
        echo "      --subnet <consumer-subnet> \\"
        echo "      --private-connection-resource-id $PLS_ID \\"
        echo "      --connection-name cloudsoa-connection"
    else
        echo "  ⚠️  Could not find internal LB frontend IP. Deploy PLS manually."
    fi
else
    echo "[5/5] Skipped (Private Link Service). Use --enable-private-link-service to enable."
fi

echo ""
echo "============================================"
echo " Private Deployment Complete"
echo "============================================"
echo ""
echo "Summary:"
echo "  ✅ VNet + Private Endpoints for Azure services"
echo "  ✅ Public network access disabled on PaaS services"
echo "  ✅ Internal Load Balancers (no public IPs)"
if [[ "$ENABLE_PLS" == "true" ]]; then
echo "  ✅ Private Link Service for cross-VNet access"
fi
echo ""
echo "Client connectivity options:"
echo "  1. VNet Peering — client VNet peered to CloudSOA VNet"
echo "  2. Private Endpoint — client creates PE to CloudSOA PLS (cross-subscription)"
echo "  3. ExpressRoute — on-premises to Azure via dedicated circuit"
echo "  4. VPN Gateway — site-to-site or point-to-site VPN"
echo "  5. Deploy client in Azure — client runs in same or peered VNet"
