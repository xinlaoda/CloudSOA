# CloudSOA — HPC Pack SOA-Compatible Cloud-Native Service Platform

[![Build](https://github.com/xinlaoda/CloudSOA/actions/workflows/ci.yaml/badge.svg)](https://github.com/xinlaoda/CloudSOA/actions/workflows/ci.yaml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/)

CloudSOA is a cloud-native SOA service platform fully compatible with [Microsoft HPC Pack SOA](https://learn.microsoft.com/en-us/powershell/high-performance-computing/overview). It enables seamless migration of existing HPC Pack SOA workloads to Azure Kubernetes Service (AKS) — **service DLLs run without code changes**, and clients only need a one-line namespace swap.

Compared to on-premises HPC Pack SOA, CloudSOA delivers **better scalability, higher availability, and lower operational cost** by leveraging cloud-native infrastructure (Kubernetes, Redis, Azure managed services).

## 🆚 CloudSOA vs. HPC Pack SOA

| Capability | HPC Pack SOA (On-Premises) | CloudSOA (AKS) |
|------------|---------------------------|-----------------|
| **Broker** | Single Head Node (stateful, single point of failure) | Stateless Broker pods (multi-replica, auto-scaling via HPA) |
| **Compute Nodes** | Physical/VM nodes, manual provisioning | Kubernetes pods, KEDA auto-scaling (0→50 in seconds) |
| **High Availability** | Active/passive failover, manual setup | Kubernetes self-healing, rolling updates, leader election |
| **Scaling** | Manual or scheduled, limited by hardware | Automatic on queue depth, scale to zero when idle |
| **Service Deployment** | Install DLL on each compute node manually | Upload DLL once → auto-deployed to all pods via Blob Storage |
| **Service DLL Compatibility** | WCF [ServiceContract] DLLs | ✅ Same DLLs, zero code changes (Windows container) |
| **Client SDK Compatibility** | `Microsoft.Hpc.Scheduler.Session` | ✅ Same API — change `using` namespace only |
| **Session Types** | Interactive + Durable | ✅ Interactive + Durable |
| **Broker Back-Pressure** | Limited throttling | Three-tier flow control (Accept / Throttle / Reject) |
| **Monitoring** | HPC Pack Cluster Manager (Windows app) | Web-based Portal (Dashboard, Monitoring, Service Mgmt) |
| **Infrastructure Cost** | Dedicated servers, always-on | Pay-per-use, scale to zero, Azure spot instances |
| **Observability** | Windows Event Log, limited metrics | Prometheus metrics, structured logging, health endpoints |
| **Update/Rollback** | Service downtime during update | Zero-downtime rolling updates, instant rollback |
| **Network Protocol** | WCF (NetTcp/BasicHttp) | REST + gRPC (modern, firewall-friendly) |

## ✨ Features

- **Session Management** — Create/Attach/Close sessions with idle timeout
- **Request Routing** — Redis Streams queue with dispatcher engine and round-robin load balancing
- **Response Caching** — Redis-backed response store with TTL and fetch-and-delete semantics
- **Dual Protocol** — REST API + gRPC for all operations
- **Client SDK** — Drop-in replacement for HPC Pack SOA client (change namespace only)
- **WCF Service Hosting** — Run existing HPC Pack SOA DLLs in Windows containers, no recompilation
- **Service Management** — Upload, deploy, and monitor service DLLs via Portal or API
- **Auto-Scaling** — KEDA-based scaling on queue depth (0→50 pods)
- **Flow Control** — Three-tier back-pressure: Accept / Throttle / Reject
- **Leader Election** — Redis-based leader election for dispatcher coordination
- **Observability** — Prometheus metrics at `/metrics`, health checks at `/healthz`, web-based Portal
- **Authentication** — API Key middleware (production: Azure AD / JWT)

## 📐 Architecture

```
  SOA Clients (CloudSOA.Client SDK)
        │  REST / gRPC
        ▼
  Azure LB / Ingress → CloudSOA.Broker (2+ replicas, HPA)
        │                  ├── Session Manager
        │                  ├── Request Queue (Redis Streams)
        │                  ├── Dispatcher Engine
        │                  └── Response Cache (Redis)
        │  gRPC
        ▼
  CloudSOA.ServiceHost      (Linux, CoreWCF — new services)
  CloudSOA.ServiceHost.Wcf  (Windows container — existing HPC Pack DLLs)
        └── User Service DLL (dynamic loading from Azure Blob)

  CloudSOA.ServiceManager   (Service registry, DLL storage, deployment)
  CloudSOA.Portal           (Web UI — dashboard, monitoring, service management)
```

## 🚀 Quick Start

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Docker](https://docs.docker.com/get-docker/)

### Local Development

```bash
# 1. Install dev environment
./scripts/setup-dev.sh     # Linux/macOS
./scripts/setup-dev.ps1    # Windows

# 2. Start Redis
docker run -d --name cloudsoa-redis -p 6379:6379 redis:7-alpine

# 3. Run Broker
cd src/CloudSOA.Broker && dotnet run

# 4. Test
curl http://localhost:5000/healthz
```

## 📦 Migrating an Existing HPC Pack SOA Service

If you have a WCF service DLL that currently runs on HPC Pack SOA (e.g. `CalculatorService.dll`), you can deploy it to CloudSOA **without changing the DLL**. Only two things change:

### Step 1 — Create a Service Configuration File

Create a `.cloudsoa.config` XML file describing your service:

```xml
<?xml version="1.0" encoding="utf-8" ?>
<ServiceRegistration xmlns="urn:cloudsoa:service-config">
  <ServiceName>CalculatorService</ServiceName>
  <Version>1.0.0</Version>
  <Runtime>wcf-netfx</Runtime>                                <!-- existing .NET Fx WCF DLL -->
  <AssemblyName>CalculatorService.dll</AssemblyName>
  <ServiceContractType>CalculatorService.ICalculator</ServiceContractType>
  <Resources>
    <MinInstances>1</MinInstances>
    <MaxInstances>5</MaxInstances>
    <CpuPerInstance>250m</CpuPerInstance>
    <MemoryPerInstance>256Mi</MemoryPerInstance>
  </Resources>
</ServiceRegistration>
```

The `Runtime` field determines which host is used:

| Runtime | Host | Container | Description |
|---------|------|-----------|-------------|
| `wcf-netfx` | ServiceHost.Wcf | Windows | Existing HPC Pack SOA DLLs (WCF/.NET Framework) |
| `corewcf` | ServiceHost | Linux | New services using CoreWCF/.NET 8 |

### Step 2 — Upload via Portal or API

**Via Portal** — Navigate to `http://<portal-ip>/services/upload`, select your DLL and config file, then click Upload.

**Via API:**

```bash
# Register the service
curl -X POST http://<servicemanager>/api/v1/services \
  -F "config=@CalculatorService.cloudsoa.config" \
  -F "assembly=@CalculatorService.dll"

# Deploy it
curl -X POST http://<servicemanager>/api/v1/services/CalculatorService/deploy
```

### Step 3 — Update Client Code (one-line change)

The only change in your client code is the `using` statement — replace the HPC Pack namespace with `CloudSOA.Client`:

```diff
- using Microsoft.Hpc.Scheduler.Session;
+ using CloudSOA.Client;
```

All the HPC Pack types are available: `Session`, `DurableSession`, `BrokerClient<T>`, `BrokerResponse<T>`, `SessionStartInfo`.

**Before (HPC Pack):**
```csharp
using Microsoft.Hpc.Scheduler.Session;

SessionStartInfo info = new SessionStartInfo("my-headnode", "CalculatorService");
using (Session session = Session.CreateSession(info))
{
    using (BrokerClient<ICalculator> client = new BrokerClient<ICalculator>(session))
    {
        client.SendRequest<AddRequest>(new AddRequest(1, 2));
        client.EndRequests();
        foreach (BrokerResponse<AddResponse> resp in client.GetResponses<AddResponse>())
            Console.WriteLine(resp.Result.AddResult);
    }
    session.Close();
}
```

**After (CloudSOA) — only the using and connection string change:**
```csharp
using CloudSOA.Client;

SessionStartInfo info = new SessionStartInfo("http://broker:5000", "CalculatorService");
using (Session session = Session.CreateSession(info))
{
    using (BrokerClient<ICalculator> client = new BrokerClient<ICalculator>(session))
    {
        client.SendRequest<AddRequest>(new AddRequest(1, 2));
        client.EndRequests();
        foreach (BrokerResponse<AddResponse> resp in client.GetResponses<AddResponse>())
            Console.WriteLine(resp.Result.AddResult);
    }
    session.Close();
}
```

> 💡 For new services that don't need WCF compatibility, use the **simplified API** with `CloudSession` and `CloudBrokerClient` — see `samples/CalculatorClient/` for both styles.

## 🏗️ Project Structure

```
CloudSOA/
├── src/
│   ├── CloudSOA.Common/            Shared models, interfaces, enums
│   ├── CloudSOA.Broker/            Session management, request routing, dispatch
│   │   ├── Controllers/            REST API (sessions, metrics)
│   │   ├── Services/               gRPC service, session manager
│   │   ├── Queue/                  Redis Streams request queue + response store
│   │   ├── Dispatch/               Dispatcher engine
│   │   ├── HA/                     Leader election
│   │   └── Metrics/                Prometheus metrics
│   ├── CloudSOA.ServiceHost/       Linux compute node (CoreWCF, new services)
│   ├── CloudSOA.ServiceHost.Wcf/   Windows compute node (WCF/.NET Fx, existing DLLs)
│   ├── CloudSOA.ServiceManager/    Service registry + DLL storage (Azure Blob + CosmosDB)
│   ├── CloudSOA.Portal/            Blazor web UI (dashboard, monitoring, service mgmt)
│   └── CloudSOA.Client/            Client SDK (HPC Pack-compatible + simplified API)
├── samples/
│   ├── CalculatorService/          Sample WCF service DLL (ICalculator)
│   └── CalculatorClient/           Sample client (HPC-compat + raw API examples)
├── tests/                          Unit + Integration tests
├── deploy/k8s/                     Kubernetes manifests
├── infra/terraform/                Azure infrastructure (IaC)
├── scripts/                        Build, deploy, test scripts (sh + ps1)
└── docs/                           Documentation
```

## 🧪 Testing

```bash
# Unit tests
dotnet test --filter "Category!=Integration"

# Integration tests (requires running Broker)
dotnet test --filter "Category=Integration"

# Smoke test
./scripts/smoke-test.ps1 -BrokerUrl http://localhost:5000    # Windows
./scripts/smoke-test.sh http://localhost:5000                 # Linux
```

## 🚢 Deployment

See [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) for the full deployment guide.

```powershell
# Azure infrastructure (PowerShell)
.\scripts\deploy-infra.ps1 -Prefix cloudsoa -Location eastus

# Build & push Docker images
.\scripts\build-images.ps1 -AcrName cloudsoacr -Tag v1.0.0

# Deploy to AKS
.\scripts\deploy-k8s.ps1 -AcrServer cloudsoacr.azurecr.io -Tag v1.0.0
```

## 📊 API Reference

### Session Management (Broker)

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/v1/sessions` | Create session |
| GET | `/api/v1/sessions` | List all sessions |
| GET | `/api/v1/sessions/{id}` | Get session |
| POST | `/api/v1/sessions/{id}/attach` | Attach to session |
| DELETE | `/api/v1/sessions/{id}` | Close session |
| GET | `/api/v1/sessions/{id}/status` | Get session status |

### Broker Client

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/v1/sessions/{id}/requests` | Send requests (batch) |
| POST | `/api/v1/sessions/{id}/requests/flush` | End requests |
| GET | `/api/v1/sessions/{id}/responses` | Get responses |

### Cluster Metrics (Broker)

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/v1/metrics` | Cluster health, pod status, queue depths |

### Service Management (ServiceManager)

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/v1/services` | List registered services |
| POST | `/api/v1/services` | Register service (upload DLL + config) |
| GET | `/api/v1/services/{name}` | Get service details |
| POST | `/api/v1/services/{name}/deploy` | Deploy service to AKS |
| POST | `/api/v1/services/{name}/stop` | Stop service |

### System

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/healthz` | Health check |
| GET | `/metrics` | Prometheus metrics |

## 📝 License

This project is licensed under the MIT License — see the [LICENSE](LICENSE) file for details.
