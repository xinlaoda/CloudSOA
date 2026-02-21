#!/usr/bin/env bash
# =============================================================================
# CloudSOA AKS Cluster Deployment Script
# Usage: ./scripts/deploy-k8s.sh --acr cloudsoacr.azurecr.io --tag v1.0.0 \
#         --redis-host "host:6380" --redis-password "xxx"
# =============================================================================
set -euo pipefail

GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

log()  { echo -e "${GREEN}[✓]${NC} $*"; }
warn() { echo -e "${YELLOW}[!]${NC} $*"; }
err()  { echo -e "${RED}[✗]${NC} $*"; exit 1; }

ACR_SERVER=""
TAG="latest"
REDIS_HOST=""
REDIS_PASSWORD=""
NAMESPACE="cloudsoa"
INSTALL_KEDA=false

while [[ $# -gt 0 ]]; do
    case $1 in
        --acr)            ACR_SERVER="$2"; shift 2 ;;
        --tag)            TAG="$2"; shift 2 ;;
        --redis-host)     REDIS_HOST="$2"; shift 2 ;;
        --redis-password) REDIS_PASSWORD="$2"; shift 2 ;;
        --namespace)      NAMESPACE="$2"; shift 2 ;;
        --install-keda)   INSTALL_KEDA=true; shift ;;
        -h|--help)
            echo "Usage: $0 [options]"
            echo "  --acr SERVER           ACR server (e.g. myacr.azurecr.io)"
            echo "  --tag TAG              Image tag (default: latest)"
            echo "  --redis-host HOST      Redis host (e.g. host:6380)"
            echo "  --redis-password PASS  Redis password"
            echo "  --namespace NS         K8s namespace (default: cloudsoa)"
            echo "  --install-keda         Also install KEDA"
            exit 0 ;;
        *) err "Unknown argument: $1" ;;
    esac
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
K8S_DIR="${PROJECT_ROOT}/deploy/k8s"

echo "============================================"
echo "  CloudSOA K8s Deployment"
echo "============================================"
echo "  ACR:        ${ACR_SERVER:-not specified}"
echo "  Image Tag:   ${TAG}"
echo "  Namespace:   ${NAMESPACE}"
echo "============================================"
echo ""

# ---- Check prerequisites ----
command -v kubectl &>/dev/null || err "Please install kubectl first"
kubectl cluster-info &>/dev/null || err "Cannot connect to K8s cluster, please check kubeconfig"

log "Connected to cluster"

# ---- Create namespace ----
log "Creating namespace..."
kubectl apply -f "${K8S_DIR}/namespace.yaml"

# ---- Create Secrets ----
if [[ -n "${REDIS_HOST}" && -n "${REDIS_PASSWORD}" ]]; then
    log "Creating Redis Secret..."
    kubectl create secret generic redis-secret \
        -n "${NAMESPACE}" \
        --from-literal=connection-string="${REDIS_HOST},password=${REDIS_PASSWORD},ssl=True,abortConnect=False" \
        --dry-run=client -o yaml | kubectl apply -f -
fi

# Generate API Key
API_KEY=$(openssl rand -hex 32)
kubectl create secret generic broker-auth \
    -n "${NAMESPACE}" \
    --from-literal=api-key="${API_KEY}" \
    --dry-run=client -o yaml | kubectl apply -f -
log "Secrets created (API Key: ${API_KEY:0:8}...)"

# ---- Update image references and deploy ----
log "Deploying ConfigMap..."
kubectl apply -f "${K8S_DIR}/broker-configmap.yaml"

# Update Broker image and deploy
log "Deploying Broker..."
if [[ -n "${ACR_SERVER}" ]]; then
    sed "s|cloudsoa.azurecr.io/broker:latest|${ACR_SERVER}/broker:${TAG}|g" \
        "${K8S_DIR}/broker-deployment.yaml" | kubectl apply -f -
else
    kubectl apply -f "${K8S_DIR}/broker-deployment.yaml"
fi

# Deploy Redis (dev environment)
if [[ -z "${REDIS_HOST}" ]]; then
    warn "No external Redis specified, deploying in-cluster Redis (dev only)..."
    kubectl apply -f "${K8S_DIR}/redis.yaml"
fi

# Deploy ServiceHost
log "Deploying ServiceHost..."
if [[ -n "${ACR_SERVER}" ]]; then
    sed "s|cloudsoa.azurecr.io/servicehost:latest|${ACR_SERVER}/servicehost:${TAG}|g" \
        "${K8S_DIR}/servicehost-deployment.yaml" | kubectl apply -f -
else
    kubectl apply -f "${K8S_DIR}/servicehost-deployment.yaml"
fi

# ---- Install KEDA ----
if [[ "${INSTALL_KEDA}" == true ]]; then
    log "Installing KEDA..."
    helm repo add kedacore https://kedacore.github.io/charts 2>/dev/null || true
    helm repo update
    helm upgrade --install keda kedacore/keda \
        --namespace keda \
        --create-namespace \
        --wait
    log "KEDA installation complete"
fi

# ---- Wait for deployment to be ready ----
echo ""
log "Waiting for Broker to be ready..."
kubectl -n "${NAMESPACE}" rollout status deployment/broker --timeout=120s

echo ""
log "Pod status:"
kubectl -n "${NAMESPACE}" get pods -o wide

echo ""
log "Service status:"
kubectl -n "${NAMESPACE}" get svc

echo ""
echo "============================================"
echo "  ✅ K8s deployment complete!"
echo "============================================"
echo ""
echo "  Verification commands:"
echo "    kubectl -n ${NAMESPACE} port-forward svc/broker-service 5000:80 &"
echo "    curl http://localhost:5000/healthz"
echo ""
echo "  API Key: ${API_KEY}"
echo "  (Use Header: X-Api-Key: ${API_KEY})"
echo ""
