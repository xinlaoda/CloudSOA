#!/usr/bin/env bash
# =============================================================================
# CloudSOA Container Image Build Script
# Usage: ./scripts/build-images.sh --acr cloudsoacr --tag v1.0.0
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
            echo "Usage: $0 --acr <ACR name> --tag <tag>"
            echo "  --acr NAME     Azure Container Registry name"
            echo "  --tag TAG      Image tag (default: latest)"
            echo "  --no-push      Build only, do not push"
            exit 0 ;;
        *) err "Unknown argument: $1" ;;
    esac
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

cd "${PROJECT_ROOT}"

echo "============================================"
echo "  CloudSOA Image Build"
echo "============================================"

# ---- Run tests first ----
log "Running unit tests..."
dotnet test --nologo --verbosity quiet --filter "Category!=Integration"
log "Tests passed"

# ---- Determine image prefix ----
if [[ -n "${ACR_NAME}" ]]; then
    ACR_SERVER="${ACR_NAME}.azurecr.io"
    
    if [[ "${PUSH}" == true ]]; then
        log "Logging in to ACR: ${ACR_NAME}..."
        az acr login --name "${ACR_NAME}"
    fi
else
    ACR_SERVER="cloudsoa"
    warn "ACR not specified, building locally only (use --acr to specify)"
fi

# ---- Build Broker ----
echo ""
log "Building Broker image..."
docker build \
    -t "${ACR_SERVER}/broker:${TAG}" \
    -f src/CloudSOA.Broker/Dockerfile \
    .
log "Broker image build complete: ${ACR_SERVER}/broker:${TAG}"

# ---- Build ServiceHost ----
echo ""
log "Building ServiceHost image..."
docker build \
    -t "${ACR_SERVER}/servicehost:${TAG}" \
    -f src/CloudSOA.ServiceHost/Dockerfile \
    .
log "ServiceHost image build complete: ${ACR_SERVER}/servicehost:${TAG}"

# ---- Tag latest ----
if [[ "${TAG}" != "latest" ]]; then
    docker tag "${ACR_SERVER}/broker:${TAG}" "${ACR_SERVER}/broker:latest"
    docker tag "${ACR_SERVER}/servicehost:${TAG}" "${ACR_SERVER}/servicehost:latest"
fi

# ---- Push ----
if [[ "${PUSH}" == true && -n "${ACR_NAME}" ]]; then
    echo ""
    log "Pushing images to ACR..."
    docker push "${ACR_SERVER}/broker:${TAG}"
    docker push "${ACR_SERVER}/servicehost:${TAG}"
    
    if [[ "${TAG}" != "latest" ]]; then
        docker push "${ACR_SERVER}/broker:latest"
        docker push "${ACR_SERVER}/servicehost:latest"
    fi
    
    log "Image push complete"
fi

echo ""
echo "============================================"
echo "  ✅ Image build complete!"
echo "============================================"
echo "  Broker:      ${ACR_SERVER}/broker:${TAG}"
echo "  ServiceHost: ${ACR_SERVER}/servicehost:${TAG}"
echo ""
