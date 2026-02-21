#!/usr/bin/env bash
# =============================================================================
# CloudSOA Dev Environment Setup Script
# Usage: ./scripts/setup-dev.sh
# =============================================================================
set -euo pipefail

GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

log()  { echo -e "${GREEN}[✓]${NC} $*"; }
warn() { echo -e "${YELLOW}[!]${NC} $*"; }
err()  { echo -e "${RED}[✗]${NC} $*"; exit 1; }

echo "============================================"
echo "  CloudSOA Dev Environment Setup"
echo "============================================"
echo ""

# ---- .NET 8 SDK ----
if command -v dotnet &>/dev/null && dotnet --list-sdks | grep -q "^8\."; then
    log ".NET 8 SDK installed ($(dotnet --version))"
else
    warn "Installing .NET 8 SDK..."
    wget -q https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb -O /tmp/packages-microsoft-prod.deb
    sudo dpkg -i /tmp/packages-microsoft-prod.deb
    sudo apt-get update -qq
    sudo apt-get install -y -qq dotnet-sdk-8.0
    rm -f /tmp/packages-microsoft-prod.deb
    log ".NET 8 SDK installed ($(dotnet --version))"
fi

# ---- Docker ----
if command -v docker &>/dev/null; then
    log "Docker installed ($(docker --version 2>/dev/null | head -1))"
else
    warn "Please install Docker manually: https://docs.docker.com/engine/install/"
fi

# ---- Azure CLI ----
if command -v az &>/dev/null; then
    log "Azure CLI installed ($(az version --output tsv 2>/dev/null | head -1 | cut -f1))"
else
    warn "Installing Azure CLI..."
    curl -sL https://aka.ms/InstallAzureCLIDeb | sudo bash
    log "Azure CLI installed"
fi

# ---- kubectl ----
if command -v kubectl &>/dev/null; then
    log "kubectl installed ($(kubectl version --client -o json 2>/dev/null | grep gitVersion | head -1 | tr -d ' ",' | cut -d: -f2))"
else
    warn "Installing kubectl..."
    KUBE_VERSION=$(curl -sL https://dl.k8s.io/release/stable.txt)
    curl -sLO "https://dl.k8s.io/release/${KUBE_VERSION}/bin/linux/amd64/kubectl"
    sudo install -o root -g root -m 0755 kubectl /usr/local/bin/kubectl
    rm -f kubectl
    log "kubectl installed (${KUBE_VERSION})"
fi

# ---- Helm ----
if command -v helm &>/dev/null; then
    log "Helm installed ($(helm version --short 2>/dev/null))"
else
    warn "Installing Helm..."
    curl -sL https://raw.githubusercontent.com/helm/helm/main/scripts/get-helm-3 | bash
    log "Helm installed"
fi

# ---- Terraform ----
if command -v terraform &>/dev/null; then
    log "Terraform installed ($(terraform --version | head -1))"
else
    warn "Terraform not installed, please install manually if needed for infrastructure deployment"
    warn "  https://developer.hashicorp.com/terraform/install"
fi

# ---- protoc ----
if command -v protoc &>/dev/null; then
    log "protoc installed ($(protoc --version))"
else
    warn "Installing protoc..."
    PROTOC_VERSION=28.3
    curl -sLO "https://github.com/protocolbuffers/protobuf/releases/download/v${PROTOC_VERSION}/protoc-${PROTOC_VERSION}-linux-x86_64.zip"
    sudo unzip -qo "protoc-${PROTOC_VERSION}-linux-x86_64.zip" -d /usr/local
    rm -f "protoc-${PROTOC_VERSION}-linux-x86_64.zip"
    log "protoc installed (${PROTOC_VERSION})"
fi

echo ""
echo "============================================"
echo "  Start local Redis"
echo "============================================"

if docker ps --format '{{.Names}}' 2>/dev/null | grep -q cloudsoa-redis; then
    log "Redis container already running"
elif docker ps -a --format '{{.Names}}' 2>/dev/null | grep -q cloudsoa-redis; then
    docker start cloudsoa-redis
    log "Redis container started"
else
    docker run -d --name cloudsoa-redis \
        -p 6379:6379 \
        --restart unless-stopped \
        redis:7-alpine \
        redis-server --maxmemory 256mb --maxmemory-policy allkeys-lru
    log "Redis container created and started"
fi

echo ""
echo "============================================"
echo "  Build project"
echo "============================================"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

cd "${PROJECT_ROOT}"
dotnet restore --verbosity quiet
dotnet build --nologo --verbosity quiet
log "Build succeeded"

echo ""
echo "============================================"
echo "  Run unit tests"
echo "============================================"

dotnet test --nologo --filter "Category!=Integration" --verbosity quiet
log "All unit tests passed"

echo ""
echo "============================================"
echo "  ✅ Dev environment setup complete!"
echo "============================================"
echo ""
echo "  Start Broker:"
echo "    cd src/CloudSOA.Broker && dotnet run"
echo ""
echo "  Endpoints:"
echo "    REST:   http://localhost:5000"
echo "    gRPC:   http://localhost:5001"
echo "    Health: http://localhost:5000/healthz"
echo "    Metrics: http://localhost:5000/metrics"
echo ""
