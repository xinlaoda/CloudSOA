#!/usr/bin/env bash
# =============================================================================
# CloudSOA 容器镜像构建脚本
# 用法: ./scripts/build-images.sh --acr cloudsoacr --tag v1.0.0
# =============================================================================
set -euo pipefail

GREEN='\033[0;32m'
RED='\033[0;31m'
NC='\033[0m'

log() { echo -e "${GREEN}[✓]${NC} $*"; }
err() { echo -e "${RED}[✗]${NC} $*"; exit 1; }

ACR_NAME=""
TAG="latest"
PUSH=true

while [[ $# -gt 0 ]]; do
    case $1 in
        --acr)     ACR_NAME="$2"; shift 2 ;;
        --tag)     TAG="$2"; shift 2 ;;
        --no-push) PUSH=false; shift ;;
        -h|--help)
            echo "用法: $0 --acr <ACR名称> --tag <标签>"
            echo "  --acr NAME     Azure Container Registry 名称"
            echo "  --tag TAG      镜像标签 (默认: latest)"
            echo "  --no-push      仅构建不推送"
            exit 0 ;;
        *) err "未知参数: $1" ;;
    esac
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

cd "${PROJECT_ROOT}"

echo "============================================"
echo "  CloudSOA 镜像构建"
echo "============================================"

# ---- 先运行测试 ----
log "运行单元测试..."
dotnet test --nologo --verbosity quiet --filter "Category!=Integration"
log "测试通过"

# ---- 确定镜像前缀 ----
if [[ -n "${ACR_NAME}" ]]; then
    ACR_SERVER="${ACR_NAME}.azurecr.io"
    
    if [[ "${PUSH}" == true ]]; then
        log "登录 ACR: ${ACR_NAME}..."
        az acr login --name "${ACR_NAME}"
    fi
else
    ACR_SERVER="cloudsoa"
    warn "未指定 ACR，仅本地构建 (使用 --acr 指定)"
fi

# ---- 构建 Broker ----
echo ""
log "构建 Broker 镜像..."
docker build \
    -t "${ACR_SERVER}/broker:${TAG}" \
    -f src/CloudSOA.Broker/Dockerfile \
    .
log "Broker 镜像构建完成: ${ACR_SERVER}/broker:${TAG}"

# ---- 构建 ServiceHost ----
echo ""
log "构建 ServiceHost 镜像..."
docker build \
    -t "${ACR_SERVER}/servicehost:${TAG}" \
    -f src/CloudSOA.ServiceHost/Dockerfile \
    .
log "ServiceHost 镜像构建完成: ${ACR_SERVER}/servicehost:${TAG}"

# ---- 打 latest 标签 ----
if [[ "${TAG}" != "latest" ]]; then
    docker tag "${ACR_SERVER}/broker:${TAG}" "${ACR_SERVER}/broker:latest"
    docker tag "${ACR_SERVER}/servicehost:${TAG}" "${ACR_SERVER}/servicehost:latest"
fi

# ---- 推送 ----
if [[ "${PUSH}" == true && -n "${ACR_NAME}" ]]; then
    echo ""
    log "推送镜像到 ACR..."
    docker push "${ACR_SERVER}/broker:${TAG}"
    docker push "${ACR_SERVER}/servicehost:${TAG}"
    
    if [[ "${TAG}" != "latest" ]]; then
        docker push "${ACR_SERVER}/broker:latest"
        docker push "${ACR_SERVER}/servicehost:latest"
    fi
    
    log "镜像推送完成"
fi

echo ""
echo "============================================"
echo "  ✅ 镜像构建完成！"
echo "============================================"
echo "  Broker:      ${ACR_SERVER}/broker:${TAG}"
echo "  ServiceHost: ${ACR_SERVER}/servicehost:${TAG}"
echo ""
