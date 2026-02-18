#!/usr/bin/env bash
# =============================================================================
# CloudSOA AKS 集群部署脚本
# 用法: ./scripts/deploy-k8s.sh --acr cloudsoacr.azurecr.io --tag v1.0.0 \
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
            echo "用法: $0 [选项]"
            echo "  --acr SERVER           ACR 地址 (如 myacr.azurecr.io)"
            echo "  --tag TAG              镜像标签 (默认: latest)"
            echo "  --redis-host HOST      Redis 地址 (如 host:6380)"
            echo "  --redis-password PASS  Redis 密码"
            echo "  --namespace NS         K8s 命名空间 (默认: cloudsoa)"
            echo "  --install-keda         同时安装 KEDA"
            exit 0 ;;
        *) err "未知参数: $1" ;;
    esac
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
K8S_DIR="${PROJECT_ROOT}/deploy/k8s"

echo "============================================"
echo "  CloudSOA K8s 部署"
echo "============================================"
echo "  ACR:        ${ACR_SERVER:-未指定}"
echo "  镜像标签:    ${TAG}"
echo "  命名空间:    ${NAMESPACE}"
echo "============================================"
echo ""

# ---- 检查前置条件 ----
command -v kubectl &>/dev/null || err "请先安装 kubectl"
kubectl cluster-info &>/dev/null || err "无法连接到 K8s 集群，请检查 kubeconfig"

log "已连接到集群"

# ---- 创建命名空间 ----
log "创建命名空间..."
kubectl apply -f "${K8S_DIR}/namespace.yaml"

# ---- 创建 Secrets ----
if [[ -n "${REDIS_HOST}" && -n "${REDIS_PASSWORD}" ]]; then
    log "创建 Redis Secret..."
    kubectl create secret generic redis-secret \
        -n "${NAMESPACE}" \
        --from-literal=connection-string="${REDIS_HOST},password=${REDIS_PASSWORD},ssl=True,abortConnect=False" \
        --dry-run=client -o yaml | kubectl apply -f -
fi

# 生成 API Key
API_KEY=$(openssl rand -hex 32)
kubectl create secret generic broker-auth \
    -n "${NAMESPACE}" \
    --from-literal=api-key="${API_KEY}" \
    --dry-run=client -o yaml | kubectl apply -f -
log "Secrets 已创建 (API Key: ${API_KEY:0:8}...)"

# ---- 更新镜像地址并部署 ----
log "部署 ConfigMap..."
kubectl apply -f "${K8S_DIR}/broker-configmap.yaml"

# 更新 Broker 镜像并部署
log "部署 Broker..."
if [[ -n "${ACR_SERVER}" ]]; then
    sed "s|cloudsoa.azurecr.io/broker:latest|${ACR_SERVER}/broker:${TAG}|g" \
        "${K8S_DIR}/broker-deployment.yaml" | kubectl apply -f -
else
    kubectl apply -f "${K8S_DIR}/broker-deployment.yaml"
fi

# 部署 Redis (开发环境)
if [[ -z "${REDIS_HOST}" ]]; then
    warn "未指定外部 Redis，部署集群内 Redis (仅用于开发)..."
    kubectl apply -f "${K8S_DIR}/redis.yaml"
fi

# 部署 ServiceHost
log "部署 ServiceHost..."
if [[ -n "${ACR_SERVER}" ]]; then
    sed "s|cloudsoa.azurecr.io/servicehost:latest|${ACR_SERVER}/servicehost:${TAG}|g" \
        "${K8S_DIR}/servicehost-deployment.yaml" | kubectl apply -f -
else
    kubectl apply -f "${K8S_DIR}/servicehost-deployment.yaml"
fi

# ---- 安装 KEDA ----
if [[ "${INSTALL_KEDA}" == true ]]; then
    log "安装 KEDA..."
    helm repo add kedacore https://kedacore.github.io/charts 2>/dev/null || true
    helm repo update
    helm upgrade --install keda kedacore/keda \
        --namespace keda \
        --create-namespace \
        --wait
    log "KEDA 安装完成"
fi

# ---- 等待部署就绪 ----
echo ""
log "等待 Broker 就绪..."
kubectl -n "${NAMESPACE}" rollout status deployment/broker --timeout=120s

echo ""
log "Pod 状态:"
kubectl -n "${NAMESPACE}" get pods -o wide

echo ""
log "Service 状态:"
kubectl -n "${NAMESPACE}" get svc

echo ""
echo "============================================"
echo "  ✅ K8s 部署完成！"
echo "============================================"
echo ""
echo "  验证命令:"
echo "    kubectl -n ${NAMESPACE} port-forward svc/broker-service 5000:80 &"
echo "    curl http://localhost:5000/healthz"
echo ""
echo "  API Key: ${API_KEY}"
echo "  (使用 Header: X-Api-Key: ${API_KEY})"
echo ""
