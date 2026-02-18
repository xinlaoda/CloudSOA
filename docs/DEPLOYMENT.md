# CloudSOA 安装部署文档

## 目录

- [1. 系统概览](#1-系统概览)
- [2. 环境要求](#2-环境要求)
- [3. 本地开发部署](#3-本地开发部署)
- [4. Azure 基础设施部署](#4-azure-基础设施部署)
- [5. 容器镜像构建](#5-容器镜像构建)
- [6. AKS 集群部署](#6-aks-集群部署)
- [7. 配置说明](#7-配置说明)
- [8. 验证与测试](#8-验证与测试)
- [9. 监控与告警](#9-监控与告警)
- [10. 运维手册](#10-运维手册)
- [11. 故障排除](#11-故障排除)

---

## 1. 系统概览

### 1.1 架构图

```
                        ┌─────────────────────┐
                        │    SOA Clients       │
                        │  (CloudSOA.Client)   │
                        └──────────┬───────────┘
                                   │ REST / gRPC
                                   ▼
                    ┌──────────────────────────────┐
                    │   Azure Load Balancer / AGIC  │
                    │   DNS: soa.mycompany.com      │
                    └──────────────┬────────────────┘
                                   │
            ┌──────────────────────┼──────────────────────┐
            │                AKS Cluster                   │
            │                                              │
            │  ┌───────────────────────────────────────┐   │
            │  │     CloudSOA.Broker (2+ 副本)          │   │
            │  │     :5000 REST  :5001 gRPC             │   │
            │  │     /healthz  /metrics                 │   │
            │  └──────────────┬────────────────────────┘   │
            │                 │ gRPC                        │
            │  ┌──────────────▼────────────────────────┐   │
            │  │  CloudSOA.ServiceHost (0-50 Pod, KEDA) │   │
            │  │  :5010 gRPC (ComputeService)           │   │
            │  └───────────────────────────────────────┘   │
            │                                              │
            │  ┌───────────────────────────────────────┐   │
            │  │  基础设施: Redis | Service Bus | CosmosDB │ │
            │  └───────────────────────────────────────┘   │
            └──────────────────────────────────────────────┘
```

### 1.2 组件清单

| 组件 | 端口 | 说明 |
|------|------|------|
| CloudSOA.Broker | 5000 (REST), 5001 (gRPC) | Session管理、请求路由、调度引擎 |
| CloudSOA.ServiceHost | 5010 (gRPC) | 计算节点，加载用户服务DLL |
| Redis | 6379 | Session元数据、请求队列(Streams)、响应缓存 |
| Azure Service Bus | - | 持久化消息队列 (Durable Session) |
| Azure CosmosDB | - | Session元数据持久化 (可选) |

---

## 2. 环境要求

### 2.1 开发环境

| 工具 | 最低版本 | 安装命令 |
|------|---------|---------|
| .NET SDK | 8.0 | `sudo apt install dotnet-sdk-8.0` |
| Docker | 20.10+ | [官方文档](https://docs.docker.com/engine/install/) |
| Git | 2.30+ | `sudo apt install git` |
| Azure CLI | 2.50+ | `curl -sL https://aka.ms/InstallAzureCLIDeb \| sudo bash` |
| kubectl | 1.27+ | 见下方安装脚本 |
| Helm | 3.12+ | `curl https://raw.githubusercontent.com/helm/helm/main/scripts/get-helm-3 \| bash` |
| Terraform | 1.5+ | [官方文档](https://developer.hashicorp.com/terraform/install) |

### 2.2 Azure 资源要求

| 资源 | SKU | 用途 |
|------|-----|------|
| AKS 集群 | Standard_D4s_v3 × 3 (system) | 系统节点池 |
| AKS 计算池 | Standard_D8s_v3 × 0-50 (autoscale) | Service Host Pod |
| Azure Redis Cache | Standard C1 | Session存储 + 请求队列 |
| Azure Service Bus | Standard | 持久化消息队列 |
| Azure CosmosDB | Serverless | Session 元数据 |
| Azure Container Registry | Standard | 容器镜像仓库 |

### 2.3 网络要求

| 方向 | 端口 | 协议 | 说明 |
|------|------|------|------|
| Client → Broker | 443 (HTTPS) | REST/gRPC | 通过 Ingress |
| Broker → ServiceHost | 5010 | gRPC (HTTP/2) | ClusterIP |
| Broker → Redis | 6379 | TCP | 集群内部 |
| Broker → Service Bus | 5671 | AMQP/TLS | Azure 服务 |

---

## 3. 本地开发部署

### 3.1 一键安装开发环境

```bash
chmod +x scripts/setup-dev.sh
./scripts/setup-dev.sh
```

### 3.2 手动步骤

#### 3.2.1 安装依赖

```bash
# 安装 .NET 8 SDK (Ubuntu/Debian)
wget https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
sudo apt-get update && sudo apt-get install -y dotnet-sdk-8.0
rm packages-microsoft-prod.deb
```

#### 3.2.2 启动 Redis

```bash
docker run -d --name cloudsoa-redis \
  -p 6379:6379 \
  --restart unless-stopped \
  redis:7-alpine \
  redis-server --maxmemory 256mb --maxmemory-policy allkeys-lru
```

#### 3.2.3 编译与测试

```bash
cd /path/to/CloudSOA
dotnet restore
dotnet build
dotnet test --filter "Category!=Integration"
```

#### 3.2.4 启动 Broker

```bash
cd src/CloudSOA.Broker
dotnet run

# 端点:
#   REST:   http://localhost:5000
#   gRPC:   http://localhost:5001
#   健康:   http://localhost:5000/healthz
#   指标:   http://localhost:5000/metrics
```

#### 3.2.5 快速验证

```bash
# 创建 Session
curl -X POST http://localhost:5000/api/v1/sessions \
  -H "Content-Type: application/json" \
  -d '{"serviceName":"TestService","minimumUnits":1,"maximumUnits":10}'

# 使用返回的 sessionId 发送请求
curl -X POST http://localhost:5000/api/v1/sessions/{sessionId}/requests \
  -H "Content-Type: application/json" \
  -d '{"requests":[{"action":"Echo","payload":"aGVsbG8=","userData":"test-1"}]}'

# 等待2秒后拉取响应
curl http://localhost:5000/api/v1/sessions/{sessionId}/responses
```

---

## 4. Azure 基础设施部署

### 4.1 前置条件

```bash
az login
az account set --subscription "<YOUR_SUBSCRIPTION_ID>"
az provider register --namespace Microsoft.ContainerService
az provider register --namespace Microsoft.ContainerRegistry
az provider register --namespace Microsoft.Cache
az provider register --namespace Microsoft.ServiceBus
az provider register --namespace Microsoft.DocumentDB
```

### 4.2 一键部署

```bash
chmod +x scripts/deploy-infra.sh
./scripts/deploy-infra.sh --prefix cloudsoa --location eastus --environment dev
```

### 4.3 手动 Terraform 部署

```bash
cd infra/terraform

cat > terraform.tfvars <<EOF
prefix         = "cloudsoa"
location       = "eastus"
aks_node_count = 3
aks_vm_size    = "Standard_D4s_v3"
redis_sku      = "Standard"
redis_capacity = 1
tags = {
  project     = "CloudSOA"
  environment = "dev"
  owner       = "your-team"
}
EOF

# Terraform state 后端存储
az group create -n cloudsoa-tfstate -l eastus
az storage account create -n cloudsoatfstate -g cloudsoa-tfstate -l eastus --sku Standard_LRS
az storage container create -n tfstate --account-name cloudsoatfstate

cat > backend.tfvars <<EOF
resource_group_name  = "cloudsoa-tfstate"
storage_account_name = "cloudsoatfstate"
container_name       = "tfstate"
key                  = "cloudsoa.terraform.tfstate"
EOF

terraform init -backend-config=backend.tfvars
terraform plan -out=tfplan
terraform apply tfplan
```

### 4.4 获取部署凭证

```bash
az aks get-credentials --resource-group cloudsoa-rg --name cloudsoa-aks
kubectl get nodes

ACR_SERVER=$(terraform output -raw acr_login_server)
REDIS_HOST=$(terraform output -raw redis_hostname)
REDIS_KEY=$(terraform output -raw redis_primary_key)
```

---

## 5. 容器镜像构建

### 5.1 一键构建

```bash
chmod +x scripts/build-images.sh
./scripts/build-images.sh --acr cloudsoacr --tag v1.0.0
```

### 5.2 手动构建

```bash
ACR_NAME="cloudsoacr"
az acr login --name $ACR_NAME
ACR_SERVER="${ACR_NAME}.azurecr.io"
TAG="v1.0.0"

docker build -t ${ACR_SERVER}/broker:${TAG} -f src/CloudSOA.Broker/Dockerfile .
docker build -t ${ACR_SERVER}/servicehost:${TAG} -f src/CloudSOA.ServiceHost/Dockerfile .
docker push ${ACR_SERVER}/broker:${TAG}
docker push ${ACR_SERVER}/servicehost:${TAG}
```

---

## 6. AKS 集群部署

### 6.1 一键部署

```bash
chmod +x scripts/deploy-k8s.sh
./scripts/deploy-k8s.sh \
  --acr cloudsoacr.azurecr.io \
  --tag v1.0.0 \
  --redis-host "cloudsoa-redis.redis.cache.windows.net:6380" \
  --redis-password "<REDIS_KEY>"
```

### 6.2 手动部署

```bash
# 1. 命名空间
kubectl apply -f deploy/k8s/namespace.yaml

# 2. Secrets
kubectl create secret generic redis-secret -n cloudsoa \
  --from-literal=connection-string="${REDIS_HOST}:6380,password=${REDIS_KEY},ssl=True,abortConnect=False"

kubectl create secret generic broker-auth -n cloudsoa \
  --from-literal=api-key="$(openssl rand -hex 32)"

# 3. ConfigMap + Deployments
kubectl apply -f deploy/k8s/broker-configmap.yaml
kubectl apply -f deploy/k8s/broker-deployment.yaml
kubectl apply -f deploy/k8s/servicehost-deployment.yaml

# 4. 等待就绪
kubectl -n cloudsoa rollout status deployment/broker

# 5. 安装 KEDA
helm repo add kedacore https://kedacore.github.io/charts
helm install keda kedacore/keda --namespace keda --create-namespace
```

### 6.3 验证

```bash
kubectl -n cloudsoa get pods
kubectl -n cloudsoa port-forward svc/broker-service 5000:80 &
curl http://localhost:5000/healthz
```

---

## 7. 配置说明

### 7.1 Broker 配置项

| 配置项 | 环境变量 | 默认值 | 说明 |
|--------|---------|--------|------|
| Redis 连接串 | `ConnectionStrings__Redis` | `localhost:6379` | Redis 地址 |
| REST 端口 | `Kestrel__Endpoints__Http__Url` | `http://0.0.0.0:5000` | REST 监听 |
| gRPC 端口 | `Kestrel__Endpoints__Grpc__Url` | `http://0.0.0.0:5001` | gRPC 监听 |
| API Key | `Authentication__ApiKey` | *(空=禁用)* | 认证密钥 |

### 7.2 ServiceHost 配置项

| 配置项 | 环境变量 | 默认值 | 说明 |
|--------|---------|--------|------|
| 服务 DLL 路径 | `SERVICE_DLL_PATH` | `/app/services/service.dll` | 用户服务 DLL |
| 监听端口 | `ASPNETCORE_URLS` | `http://+:5010` | gRPC 监听 |

### 7.3 配置优先级

```
环境变量 > appsettings.{Environment}.json > appsettings.json > 代码默认值
```

---

## 8. 验证与测试

### 8.1 冒烟测试

```bash
chmod +x scripts/smoke-test.sh
./scripts/smoke-test.sh http://localhost:5000
```

### 8.2 验证清单

| # | 测试项 | 命令 | 预期 |
|---|--------|------|------|
| 1 | 健康检查 | `curl /healthz` | `Healthy` (200) |
| 2 | 指标端点 | `curl /metrics` | Prometheus 格式 |
| 3 | 创建 Session | `POST /api/v1/sessions` | 201 |
| 4 | 发送请求 | `POST .../requests` | 202, enqueued>0 |
| 5 | 拉取响应 | `GET .../responses` | 200, count>0 |
| 6 | 关闭 Session | `DELETE .../sessions/{id}` | 204 |
| 7 | 404 场景 | `GET .../sessions/invalid` | 404 |

---

## 9. 监控与告警

### 9.1 Prometheus 指标

| 指标名 | 类型 | 说明 |
|--------|------|------|
| `cloudsoa_sessions_active` | Gauge | 活跃 Session 数 |
| `cloudsoa_sessions_created_total` | Counter | 累计创建数 |
| `cloudsoa_requests_enqueued_total` | Counter | 入队请求数 |
| `cloudsoa_requests_processed_total` | Counter | 已处理数 |
| `cloudsoa_requests_failed_total` | Counter | 失败数 |
| `cloudsoa_queue_depth` | Gauge | 队列深度 |
| `cloudsoa_request_duration_seconds` | Histogram | 处理耗时 |

### 9.2 推荐告警

| 告警 | 条件 | 严重程度 |
|------|------|---------|
| Broker 不可用 | Pod Ready < 1 持续 1min | Critical |
| 队列积压 | queue_depth > 5000 持续 5min | Warning |
| 高错误率 | failed/total > 5% 持续 3min | Warning |
| 高延迟 | P99 > 10s 持续 5min | Warning |

---

## 10. 运维手册

### 10.1 常用命令

```bash
kubectl -n cloudsoa get pods -o wide                       # 查看 Pod
kubectl -n cloudsoa logs -l app=broker --tail=100 -f       # 查看日志
kubectl -n cloudsoa scale deployment/broker --replicas=3   # 扩缩容
kubectl -n cloudsoa rollout undo deployment/broker         # 回滚
kubectl -n cloudsoa get hpa                                # HPA 状态
```

### 10.2 版本更新

```bash
./scripts/build-images.sh --acr cloudsoacr --tag v1.1.0
./scripts/deploy-k8s.sh --tag v1.1.0
./scripts/smoke-test.sh http://localhost:5000
# 失败时回滚: kubectl -n cloudsoa rollout undo deployment/broker
```

---

## 11. 故障排除

| 问题 | 原因 | 排查 | 修复 |
|------|------|------|------|
| Broker 启动失败 | Redis 连接超时 | 查看 Pod 日志 grep Redis | 检查 ConfigMap Redis 配置 |
| 请求无响应 | Dispatcher 未启动 | 日志 grep Dispatch | 确认 ServiceHost Pod 就绪 |
| gRPC UNAVAILABLE | HTTP/2 未配置 | 检查 Kestrel 配置 | 设置 Protocols=Http2 |
| KEDA 不伸缩 | 配置错误 | `describe scaledobject` | 检查 Redis 地址和队列名 |

```bash
# 全面诊断
chmod +x scripts/diagnose.sh
./scripts/diagnose.sh
```
