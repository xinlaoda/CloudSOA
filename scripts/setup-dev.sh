#!/usr/bin/env bash
# =============================================================================
# CloudSOA 开发环境一键安装脚本
# 用法: ./scripts/setup-dev.sh
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
echo "  CloudSOA 开发环境安装"
echo "============================================"
echo ""

# ---- .NET 8 SDK ----
if command -v dotnet &>/dev/null && dotnet --list-sdks | grep -q "^8\."; then
    log ".NET 8 SDK 已安装 ($(dotnet --version))"
else
    warn "安装 .NET 8 SDK..."
    wget -q https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb -O /tmp/packages-microsoft-prod.deb
    sudo dpkg -i /tmp/packages-microsoft-prod.deb
    sudo apt-get update -qq
    sudo apt-get install -y -qq dotnet-sdk-8.0
    rm -f /tmp/packages-microsoft-prod.deb
    log ".NET 8 SDK 安装完成 ($(dotnet --version))"
fi

# ---- Docker ----
if command -v docker &>/dev/null; then
    log "Docker 已安装 ($(docker --version 2>/dev/null | head -1))"
else
    warn "请手动安装 Docker: https://docs.docker.com/engine/install/"
fi

# ---- Azure CLI ----
if command -v az &>/dev/null; then
    log "Azure CLI 已安装 ($(az version --output tsv 2>/dev/null | head -1 | cut -f1))"
else
    warn "安装 Azure CLI..."
    curl -sL https://aka.ms/InstallAzureCLIDeb | sudo bash
    log "Azure CLI 安装完成"
fi

# ---- kubectl ----
if command -v kubectl &>/dev/null; then
    log "kubectl 已安装 ($(kubectl version --client -o json 2>/dev/null | grep gitVersion | head -1 | tr -d ' ",' | cut -d: -f2))"
else
    warn "安装 kubectl..."
    KUBE_VERSION=$(curl -sL https://dl.k8s.io/release/stable.txt)
    curl -sLO "https://dl.k8s.io/release/${KUBE_VERSION}/bin/linux/amd64/kubectl"
    sudo install -o root -g root -m 0755 kubectl /usr/local/bin/kubectl
    rm -f kubectl
    log "kubectl 安装完成 (${KUBE_VERSION})"
fi

# ---- Helm ----
if command -v helm &>/dev/null; then
    log "Helm 已安装 ($(helm version --short 2>/dev/null))"
else
    warn "安装 Helm..."
    curl -sL https://raw.githubusercontent.com/helm/helm/main/scripts/get-helm-3 | bash
    log "Helm 安装完成"
fi

# ---- Terraform ----
if command -v terraform &>/dev/null; then
    log "Terraform 已安装 ($(terraform --version | head -1))"
else
    warn "Terraform 未安装，如需部署基础设施请手动安装"
    warn "  https://developer.hashicorp.com/terraform/install"
fi

# ---- protoc ----
if command -v protoc &>/dev/null; then
    log "protoc 已安装 ($(protoc --version))"
else
    warn "安装 protoc..."
    PROTOC_VERSION=28.3
    curl -sLO "https://github.com/protocolbuffers/protobuf/releases/download/v${PROTOC_VERSION}/protoc-${PROTOC_VERSION}-linux-x86_64.zip"
    sudo unzip -qo "protoc-${PROTOC_VERSION}-linux-x86_64.zip" -d /usr/local
    rm -f "protoc-${PROTOC_VERSION}-linux-x86_64.zip"
    log "protoc 安装完成 (${PROTOC_VERSION})"
fi

echo ""
echo "============================================"
echo "  启动本地 Redis"
echo "============================================"

if docker ps --format '{{.Names}}' 2>/dev/null | grep -q cloudsoa-redis; then
    log "Redis 容器已在运行"
elif docker ps -a --format '{{.Names}}' 2>/dev/null | grep -q cloudsoa-redis; then
    docker start cloudsoa-redis
    log "Redis 容器已启动"
else
    docker run -d --name cloudsoa-redis \
        -p 6379:6379 \
        --restart unless-stopped \
        redis:7-alpine \
        redis-server --maxmemory 256mb --maxmemory-policy allkeys-lru
    log "Redis 容器已创建并启动"
fi

echo ""
echo "============================================"
echo "  编译项目"
echo "============================================"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

cd "${PROJECT_ROOT}"
dotnet restore --verbosity quiet
dotnet build --nologo --verbosity quiet
log "项目编译成功"

echo ""
echo "============================================"
echo "  运行单元测试"
echo "============================================"

dotnet test --nologo --filter "Category!=Integration" --verbosity quiet
log "单元测试全部通过"

echo ""
echo "============================================"
echo "  ✅ 开发环境安装完成！"
echo "============================================"
echo ""
echo "  启动 Broker:"
echo "    cd src/CloudSOA.Broker && dotnet run"
echo ""
echo "  端点:"
echo "    REST:   http://localhost:5000"
echo "    gRPC:   http://localhost:5001"
echo "    健康:   http://localhost:5000/healthz"
echo "    指标:   http://localhost:5000/metrics"
echo ""
