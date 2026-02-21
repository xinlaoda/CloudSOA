# CloudSOA Deployment Guide

## Table of Contents

- [1. System Overview](#1-system-overview)
- [2. Prerequisites](#2-prerequisites)
- [3. Local Development](#3-local-development)
- [4. Azure Infrastructure Deployment](#4-azure-infrastructure-deployment)
- [5. Container Image Build](#5-container-image-build)
- [6. AKS Cluster Deployment](#6-aks-cluster-deployment)
- [7. Service Components](#7-service-components)
- [8. Configuration Reference](#8-configuration-reference)
- [9. Verification & Testing](#9-verification--testing)
- [10. Monitoring & Alerting](#10-monitoring--alerting)
- [11. Operations Runbook](#11-operations-runbook)
- [12. Troubleshooting](#12-troubleshooting)

---

## 1. System Overview

### 1.1 Architecture Diagram

```
                        ┌───────────────────────────┐
                        │    SOA Clients             │
                        │  CloudSOA.Client (.NET 8)  │
                        │  CloudSOA.Client.NetFx     │
                        │  (.NET Framework 4.8)      │
                        └──────────┬────────────────┘
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
            │  │  CloudSOA.Broker (2+ replicas)         │   │
            │  │  :5000 REST  :5001 gRPC                │   │
            │  └──────────────┬────────────────────────┘   │
            │                 │                             │
            │  ┌──────────────▼────────────────────────┐   │
            │  │  Service Hosts (0-50 Pods, KEDA)       │   │
            │  │                                        │   │
            │  │  windows-netfx48:                      │   │
            │  │    ServiceHost.Wcf + NetFxBridge        │   │
            │  │    (Windows Server Core, .NET Fx 4.8)  │   │
            │  │                                        │   │
            │  │  linux-corewcf:                        │   │
            │  │    ServiceHost.CoreWcf                  │   │
            │  │    (Linux, .NET 8 CoreWCF)             │   │
            │  │                                        │   │
            │  │  linux-net8 / windows-net8:            │   │
            │  │    ServiceHost                          │   │
            │  │    (Linux or Windows, .NET 8 native)   │   │
            │  └───────────────────────────────────────┘   │
            │                                              │
            │  ┌───────────────────────────────────────┐   │
            │  │  CloudSOA.ServiceManager               │   │
            │  │  (Service registry, DLL storage)       │   │
            │  └───────────────────────────────────────┘   │
            │                                              │
            │  ┌───────────────────────────────────────┐   │
            │  │  CloudSOA.Portal (Web UI)              │   │
            │  │  Dashboard, Monitoring, Service Mgmt   │   │
            │  └───────────────────────────────────────┘   │
            │                                              │
            │  ┌───────────────────────────────────────┐   │
            │  │  Infrastructure:                       │   │
            │  │  Redis | Service Bus | CosmosDB | Blob │   │
            │  └───────────────────────────────────────┘   │
            └──────────────────────────────────────────────┘
```

### 1.2 Component Summary

| Component | Port | Description |
|-----------|------|-------------|
| CloudSOA.Broker | 5000 (REST), 5001 (gRPC) | Session management, request routing, dispatch engine, cluster metrics |
| CloudSOA.ServiceHost | 5010 (gRPC) | Compute node — loads .NET 8 native ISOAService DLLs (Linux or Windows) |
| CloudSOA.ServiceHost.Wcf | 5010 (gRPC) | Windows compute node — loads existing HPC Pack WCF/.NET Fx 4.8 DLLs via NetFxBridge |
| CloudSOA.ServiceHost.CoreWcf | 5010 (gRPC) | Linux compute node — loads .NET 8 CoreWCF service DLLs |
| CloudSOA.ServiceManager | 80 (REST) | Service registry (CosmosDB), DLL + dependency storage (Azure Blob), deployment orchestration |
| CloudSOA.Portal | 80 (HTTP) | Blazor web UI — dashboard, sessions, monitoring, service upload (multi-file) |
| CloudSOA.Client | — | Client SDK for .NET 8 (HPC Pack-compatible + simplified API) |
| CloudSOA.Client.NetFx | — | Client SDK for .NET Framework 4.8 (HPC Pack-compatible API) |
| Redis | 6379 | Session metadata, request queue (Streams), response cache |
| Azure Service Bus | — | Durable message queue (Durable Sessions) |
| Azure CosmosDB | — | Service registration metadata (serverless) |
| Azure Blob Storage | — | Service DLL package storage (main + dependency DLLs) |

---

## 2. Prerequisites

### 2.1 Development Tools

| Tool | Minimum Version | Installation |
|------|----------------|--------------|
| .NET SDK | 8.0 | [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/8.0) |
| Docker | 20.10+ | [docs.docker.com](https://docs.docker.com/get-docker/) |
| Git | 2.30+ | [git-scm.com](https://git-scm.com/) |
| Azure CLI | 2.50+ | [aka.ms/installazurecli](https://aka.ms/installazurecli) |
| kubectl | 1.27+ | `az aks install-cli` |
| Helm | 3.12+ | [helm.sh](https://helm.sh/docs/intro/install/) |
| Terraform | 1.5+ | [terraform.io](https://developer.hashicorp.com/terraform/install) |

### 2.2 Azure Resource Requirements

| Resource | SKU | Purpose |
|----------|-----|---------|
| AKS Cluster | Standard_D2s_v3 × 2 (system) | System node pool (Broker, Portal, ServiceManager) |
| AKS Compute Pool | Standard_D4s_v3 × 0-50 (autoscale) | ServiceHost pods |
| Azure Redis Cache | Basic C0 or Standard C1 | Session store + request queue |
| Azure Service Bus | Standard | Durable message queue |
| Azure CosmosDB | Serverless | Service registration metadata |
| Azure Container Registry | Standard | Container image registry |
| Azure Blob Storage | Standard LRS | Service DLL package storage |

### 2.3 Network Requirements

| Direction | Port | Protocol | Description |
|-----------|------|----------|-------------|
| Client → Broker | 443 (HTTPS) | REST/gRPC | Via Ingress/LoadBalancer |
| Broker → ServiceHost | 5010 | gRPC (HTTP/2) | ClusterIP (internal) |
| Broker → Redis | 6379 | TCP | Internal |
| Broker → Service Bus | 5671 | AMQP/TLS | Azure service |
| Portal → Broker | 80 | HTTP | Internal (ClusterIP) |
| Portal → ServiceManager | 80 | HTTP | Internal (ClusterIP) |

---

## 3. Local Development

### 3.1 Quick Setup (Windows)

```powershell
.\scripts\setup-dev.ps1
```

### 3.2 Quick Setup (Linux/macOS)

```bash
chmod +x scripts/setup-dev.sh && ./scripts/setup-dev.sh
```

### 3.3 Manual Steps

```bash
# Install .NET 8 SDK — see https://dotnet.microsoft.com/download/dotnet/8.0

# Start Redis
docker run -d --name cloudsoa-redis -p 6379:6379 --restart unless-stopped \
  redis:7-alpine redis-server --maxmemory 256mb --maxmemory-policy allkeys-lru

# Build and test
cd CloudSOA
dotnet restore
dotnet build
dotnet test --filter "Category!=Integration"

# Run Broker
cd src/CloudSOA.Broker && dotnet run
# REST: http://localhost:5000  |  gRPC: http://localhost:5001
# Health: http://localhost:5000/healthz  |  Metrics: http://localhost:5000/metrics
```

### 3.4 Quick Verification

```bash
# Create a session
curl -X POST http://localhost:5000/api/v1/sessions \
  -H "Content-Type: application/json" \
  -d '{"serviceName":"TestService","minimumUnits":1,"maximumUnits":10}'

# Send requests (replace {sessionId})
curl -X POST http://localhost:5000/api/v1/sessions/{sessionId}/requests \
  -H "Content-Type: application/json" \
  -d '{"requests":[{"action":"Echo","payload":"aGVsbG8=","userData":"test-1"}]}'

# Get responses
curl http://localhost:5000/api/v1/sessions/{sessionId}/responses
```

---

## 4. Azure Infrastructure Deployment

### 4.1 Prerequisites

```bash
az login
az account set --subscription "<YOUR_SUBSCRIPTION_ID>"
az provider register --namespace Microsoft.ContainerService
az provider register --namespace Microsoft.ContainerRegistry
az provider register --namespace Microsoft.Cache
az provider register --namespace Microsoft.ServiceBus
az provider register --namespace Microsoft.DocumentDB
```

### 4.2 One-Command Deployment

```powershell
# Windows
.\scripts\deploy-infra.ps1 -Prefix cloudsoa -Location eastus -Environment dev
```

```bash
# Linux
./scripts/deploy-infra.sh --prefix cloudsoa --location eastus --environment dev
```

### 4.3 Manual Terraform Deployment

```bash
cd infra/terraform

cat > terraform.tfvars <<EOF
prefix         = "cloudsoa"
location       = "eastus"
aks_node_count = 2
aks_vm_size    = "Standard_D2s_v3"
redis_sku      = "Basic"
redis_capacity = 0
tags = {
  project     = "CloudSOA"
  environment = "dev"
}
EOF

terraform init
terraform plan -out=tfplan
terraform apply tfplan
```

### 4.4 Retrieve Deployment Credentials

```bash
az aks get-credentials --resource-group cloudsoa-rg --name cloudsoa-aks
kubectl get nodes
```

---

## 5. Container Image Build

### 5.1 One-Command Build

```powershell
# Windows
.\scripts\build-images.ps1 -AcrName cloudsoacr -Tag v1.0.0
```

### 5.2 Manual Build

```bash
ACR_NAME="cloudsoacr"
az acr login --name $ACR_NAME
ACR_SERVER="${ACR_NAME}.azurecr.io"
TAG="v1.0.0"

# Broker
docker build -t ${ACR_SERVER}/broker:${TAG} -f src/CloudSOA.Broker/Dockerfile .
docker push ${ACR_SERVER}/broker:${TAG}

# ServiceHost (Linux — native .NET 8 ISOAService)
docker build -t ${ACR_SERVER}/servicehost:${TAG} -f src/CloudSOA.ServiceHost/Dockerfile .
docker push ${ACR_SERVER}/servicehost:${TAG}

# ServiceHost.Wcf (Windows — existing HPC Pack .NET Fx 4.8 DLLs via NetFxBridge)
docker build -t ${ACR_SERVER}/servicehost-wcf-netfx:${TAG} -f src/CloudSOA.ServiceHost.Wcf/Dockerfile.windows-netfx .
docker push ${ACR_SERVER}/servicehost-wcf-netfx:${TAG}

# ServiceHost.CoreWcf (Linux — .NET 8 CoreWCF services)
docker build -t ${ACR_SERVER}/servicehost-corewcf:${TAG} -f src/CloudSOA.ServiceHost.CoreWcf/Dockerfile .
docker push ${ACR_SERVER}/servicehost-corewcf:${TAG}

# ServiceHost (Windows — .NET 8 native on Nano Server)
docker build -t ${ACR_SERVER}/servicehost-net8-win:${TAG} -f src/CloudSOA.ServiceHost/Dockerfile.windows .
docker push ${ACR_SERVER}/servicehost-net8-win:${TAG}

# ServiceManager
docker build -t ${ACR_SERVER}/servicemanager:${TAG} -f src/CloudSOA.ServiceManager/Dockerfile .
docker push ${ACR_SERVER}/servicemanager:${TAG}

# Portal
docker build -t ${ACR_SERVER}/portal:${TAG} -f src/CloudSOA.Portal/Dockerfile .
docker push ${ACR_SERVER}/portal:${TAG}
```

---

## 6. AKS Cluster Deployment

### 6.1 One-Command Deployment

```powershell
.\scripts\deploy-k8s.ps1 -AcrServer cloudsoacr.azurecr.io -Tag v1.0.0 `
  -RedisHost "cloudsoa-redis.redis.cache.windows.net:6380" `
  -RedisPassword "<REDIS_KEY>"
```

### 6.2 Manual Deployment

```bash
# 1. Namespace
kubectl apply -f deploy/k8s/namespace.yaml

# 2. Secrets
kubectl create secret generic redis-secret -n cloudsoa \
  --from-literal=connection-string="${REDIS_HOST},password=${REDIS_KEY},ssl=True,abortConnect=False"

kubectl create secret generic servicemanager-secrets -n cloudsoa \
  --from-literal=blob-connection-string="${BLOB_CONN}" \
  --from-literal=cosmosdb-connection-string="${COSMOS_CONN}"

kubectl create secret docker-registry acr-secret -n cloudsoa \
  --docker-server=${ACR_SERVER} --docker-username=${ACR_USER} --docker-password=${ACR_PASS}

# 3. RBAC for Broker (K8s API access for pod monitoring)
kubectl apply -f deploy/k8s/broker-rbac.yaml

# 4. Deployments
kubectl apply -f deploy/k8s/broker-deployment.yaml
kubectl apply -f deploy/k8s/servicemanager-deployment.yaml
kubectl apply -f deploy/k8s/portal-deployment.yaml
kubectl apply -f deploy/k8s/servicehost-deployment.yaml

# 5. Wait for readiness
kubectl -n cloudsoa rollout status deployment/broker
kubectl -n cloudsoa rollout status deployment/servicemanager
kubectl -n cloudsoa rollout status deployment/portal

# 6. (Optional) Install KEDA for auto-scaling
helm repo add kedacore https://kedacore.github.io/charts
helm install keda kedacore/keda --namespace keda --create-namespace
```

### 6.3 Verify

```bash
kubectl -n cloudsoa get pods
kubectl -n cloudsoa get svc

# Portal (external)
PORTAL_IP=$(kubectl -n cloudsoa get svc portal-service -o jsonpath='{.status.loadBalancer.ingress[0].ip}')
echo "Portal: http://${PORTAL_IP}"

# Broker health check
kubectl -n cloudsoa port-forward svc/broker-service 5000:80 &
curl http://localhost:5000/healthz
```

---

## 7. Service Components

### 7.1 Broker (`CloudSOA.Broker`)

The Broker is the central hub of CloudSOA. It manages sessions, routes requests through Redis Streams, dispatches work to ServiceHost pods, and caches responses.

**Key features:**
- Session lifecycle (create, attach, close, timeout)
- Request batching and dispatching with round-robin load balancing
- Response caching with TTL and fetch-and-delete semantics
- Three-tier flow control (Accept / Throttle / Reject)
- Redis-based leader election for HA
- Cluster metrics API (pod status, queue depths, health checks)
- Prometheus metrics and health endpoints

**Kubernetes manifest:** `deploy/k8s/broker-deployment.yaml`

### 7.2 ServiceHost (`CloudSOA.ServiceHost`)

The compute node for services built with .NET 8 native `ISOAService` interface. Runs on both Linux and Windows (Nano Server). Dynamically loads user service DLLs and exposes them via gRPC. Scaled by KEDA based on Redis queue depth.

**Runtimes:** `linux-net8`, `windows-net8`

**Kubernetes manifest:** `deploy/k8s/servicehost-deployment.yaml`

### 7.3 ServiceHost.Wcf (`CloudSOA.ServiceHost.Wcf`)

The Windows container-based compute node for **existing HPC Pack SOA DLLs** (.NET Framework 4.0–4.8). Uses a **dual-process architecture**: the .NET 8 gRPC host communicates with a NetFxBridge process (.NET Framework 4.8) that loads and executes the legacy WCF DLL. This enables zero-effort migration from HPC Pack SOA — no DLL recompilation needed.

**Runtime:** `windows-netfx48`

**Kubernetes manifest:** `deploy/k8s/servicehost-wcf-deployment.yaml`

### 7.4 ServiceHost.CoreWcf (`CloudSOA.ServiceHost.CoreWcf`)

The Linux compute node for **new CoreWCF services** built on .NET 8. Loads DLLs with `[ServiceContract]` interfaces via CoreWCF framework. This is the recommended runtime for new WCF-compatible service development.

**Runtime:** `linux-corewcf`

**Kubernetes manifest:** `deploy/k8s/servicehost-corewcf-deployment.yaml`

### 7.5 ServiceManager (`CloudSOA.ServiceManager`)

The service registry and deployment orchestrator. It stores service metadata in CosmosDB and service DLL packages in Azure Blob Storage.

**Key features:**
- Service registration (upload DLL + dependency DLLs + config)
- Automatic runtime-to-container-image mapping (4 runtimes)
- Service deployment to AKS (creates K8s Deployments with correct node selector)
- Service lifecycle management (start, stop, scale)
- DLL + dependency storage in Azure Blob

**Kubernetes manifest:** `deploy/k8s/servicemanager-deployment.yaml`

### 7.6 Portal (`CloudSOA.Portal`)

A Blazor Server web application providing a management UI for CloudSOA, similar to HPC Pack SOA's management console.

**Pages:**
- **Dashboard** (`/`) — Cluster health overview, active sessions, running services
- **Services** (`/services`) — Registered services list with runtime badges, deploy/stop actions
- **Service Upload** (`/services/upload`) — Upload new service DLL + dependencies, select runtime
- **Sessions** (`/sessions`) — Active and closed sessions list
- **Monitoring** (`/monitoring`) — Pod status, queue depths, cluster health

**Kubernetes manifest:** `deploy/k8s/portal-deployment.yaml`

### 7.7 Client SDKs

CloudSOA provides two client libraries — both are drop-in replacements for `Microsoft.Hpc.Scheduler.Session`:

| Library | Target | API |
|---------|--------|-----|
| `CloudSOA.Client` (.NET 8) | New clients or upgraded clients | HPC Pack-compatible (`Session`, `BrokerClient<T>`) + Simplified (`CloudSession`, `CloudBrokerClient`) |
| `CloudSOA.Client.NetFx` (.NET Framework 4.8) | Legacy clients staying on .NET Fx | HPC Pack-compatible only (`Session`, `BrokerClient<T>`, `BrokerResponse<T>`) |

Both use the same `using CloudSOA.Client;` namespace. Client migration requires only changing `using` + broker URL.

---

## 8. Configuration Reference

### 8.1 Broker Configuration

| Config Key | Environment Variable | Default | Description |
|------------|---------------------|---------|-------------|
| Redis connection | `ConnectionStrings__Redis` | `localhost:6379` | Redis address |
| REST port | `Kestrel__Endpoints__Http__Url` | `http://0.0.0.0:5000` | REST listener |
| gRPC port | `Kestrel__Endpoints__Grpc__Url` | `http://0.0.0.0:5001` | gRPC listener |
| API Key | `Authentication__ApiKey` | *(empty=disabled)* | Auth key |

### 8.2 ServiceManager Configuration

| Config Key | Environment Variable | Default | Description |
|------------|---------------------|---------|-------------|
| Blob connection | `AzureBlob__ConnectionString` | — | Azure Blob Storage connection string |
| CosmosDB connection | `CosmosDb__ConnectionString` | — | Azure CosmosDB connection string |

### 8.3 Portal Configuration

| Config Key | Environment Variable | Default | Description |
|------------|---------------------|---------|-------------|
| Broker URL | `ApiEndpoints__Broker` | `http://broker-service` | Internal Broker service URL |
| ServiceManager URL | `ApiEndpoints__ServiceManager` | `http://servicemanager-service` | Internal ServiceManager URL |

### 8.4 Configuration Priority

```
Environment variables > appsettings.{Environment}.json > appsettings.json > Code defaults
```

---

## 9. Verification & Testing

### 9.1 Smoke Test

```powershell
.\scripts\smoke-test.ps1 -BrokerUrl http://localhost:5000    # Windows
```

```bash
./scripts/smoke-test.sh http://localhost:5000                 # Linux
```

### 9.2 Verification Checklist

| # | Test | Command | Expected |
|---|------|---------|----------|
| 1 | Health check | `curl /healthz` | `Healthy` (200) |
| 2 | Metrics endpoint | `curl /metrics` | Prometheus format |
| 3 | Create session | `POST /api/v1/sessions` | 201 |
| 4 | Send requests | `POST .../requests` | 202, enqueued>0 |
| 5 | Get responses | `GET .../responses` | 200, count>0 |
| 6 | Close session | `DELETE .../sessions/{id}` | 204 |
| 7 | 404 scenario | `GET .../sessions/invalid` | 404 |
| 8 | List services | `GET /api/v1/services` (ServiceManager) | 200, array |
| 9 | Cluster metrics | `GET /api/v1/metrics` (Broker) | 200, pod data |
| 10 | Portal loads | `GET http://<portal-ip>/` | 200, HTML |

---

## 10. Monitoring & Alerting

### 10.1 Prometheus Metrics

| Metric | Type | Description |
|--------|------|-------------|
| `cloudsoa_sessions_active` | Gauge | Active sessions |
| `cloudsoa_sessions_created_total` | Counter | Total sessions created |
| `cloudsoa_requests_enqueued_total` | Counter | Enqueued requests |
| `cloudsoa_requests_processed_total` | Counter | Processed requests |
| `cloudsoa_requests_failed_total` | Counter | Failed requests |
| `cloudsoa_queue_depth` | Gauge | Queue depth |
| `cloudsoa_request_duration_seconds` | Histogram | Processing latency |

### 10.2 Portal Monitoring

The Portal at `http://<portal-ip>/monitoring` provides a real-time view of:
- Broker pod status (name, node, CPU, memory, restarts)
- ServiceHost pod status
- Queue depths (pending, processing)

### 10.3 Recommended Alerts

| Alert | Condition | Severity |
|-------|-----------|----------|
| Broker unavailable | Pod Ready < 1 for 1min | Critical |
| Queue backlog | queue_depth > 5000 for 5min | Warning |
| High error rate | failed/total > 5% for 3min | Warning |
| High latency | P99 > 10s for 5min | Warning |

---

## 11. Operations Runbook

### 11.1 Common Commands

```bash
kubectl -n cloudsoa get pods -o wide                       # List pods
kubectl -n cloudsoa logs -l app=broker --tail=100 -f       # Stream logs
kubectl -n cloudsoa scale deployment/broker --replicas=3   # Scale
kubectl -n cloudsoa rollout undo deployment/broker         # Rollback
kubectl -n cloudsoa get hpa                                # HPA status
```

### 11.2 Version Update

```powershell
# Windows
.\scripts\build-images.ps1 -AcrName cloudsoacr -Tag v1.1.0
.\scripts\deploy-k8s.ps1 -AcrServer cloudsoacr.azurecr.io -Tag v1.1.0
.\scripts\smoke-test.ps1 -BrokerUrl http://localhost:5000

# Rollback on failure
kubectl -n cloudsoa rollout undo deployment/broker
```

### 11.3 Upload a New Service DLL

```bash
# Via API
curl -X POST http://<servicemanager>/api/v1/services \
  -F "config=@MyService.cloudsoa.config" \
  -F "assembly=@MyService.dll"

# With dependency DLLs
curl -X POST http://<servicemanager>/api/v1/services \
  -F "config=@MyService.cloudsoa.config" \
  -F "assembly=@MyService.dll" \
  -F "dependencies=@MyHelper.dll" \
  -F "dependencies=@ThirdParty.dll"

# Deploy
curl -X POST http://<servicemanager>/api/v1/services/MyService/deploy
```

---

## 12. Troubleshooting

| Problem | Cause | Diagnosis | Fix |
|---------|-------|-----------|-----|
| Broker won't start | Redis connection timeout | Check pod logs for Redis errors | Verify ConfigMap Redis config |
| Requests get no response | Dispatcher not started | Grep logs for "Dispatch" | Ensure ServiceHost pods are ready |
| gRPC UNAVAILABLE | HTTP/2 not configured | Check Kestrel config | Set Protocols=Http2 |
| KEDA doesn't scale | Config error | `describe scaledobject` | Check Redis address and queue name |
| Portal shows no data | Broker API unreachable | Exec into Portal pod, test connectivity | Verify ApiEndpoints env vars |
| Monitoring pods empty | RBAC missing | Check Broker logs for 403 | Apply `deploy/k8s/broker-rbac.yaml` |
| Service upload fails | Blob Storage unreachable | Check ServiceManager logs | Verify `servicemanager-secrets` |

```bash
# Full diagnostics
./scripts/diagnose.ps1   # Windows
./scripts/diagnose.sh    # Linux
```
