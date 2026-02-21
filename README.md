# CloudSOA — HPC Pack SOA-Compatible Cloud-Native Service Platform

[![Build](https://github.com/xinlaoda/CloudSOA/actions/workflows/ci.yaml/badge.svg)](https://github.com/xinlaoda/CloudSOA/actions/workflows/ci.yaml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/)

CloudSOA is a cloud-native SOA service platform fully compatible with [Microsoft HPC Pack SOA](https://learn.microsoft.com/en-us/powershell/high-performance-computing/overview). It enables seamless migration of existing HPC Pack SOA workloads to Azure Kubernetes Service (AKS) — **service DLLs run without code changes**, and clients only need a one-line namespace swap.

CloudSOA provides **two development paths**:

| Path | Service DLL | Client Library | Use Case |
|------|------------|----------------|----------|
| **Migration** | Existing .NET Framework 4.8 WCF DLL (no changes) | `CloudSOA.Client.NetFx` (net48) | Migrate HPC Pack SOA services to cloud |
| **New Development** | New .NET 8 + CoreWCF DLL | `CloudSOA.Client` (net8.0) | Build new SOA services or upgrade existing ones |

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
- **Client SDK** — Drop-in replacement for HPC Pack SOA client: `.NET Framework 4.8` (CloudSOA.Client.NetFx) and `.NET 8` (CloudSOA.Client) — change namespace only
- **WCF Service Hosting** — Run existing HPC Pack SOA DLLs (.NET Framework 4.0–4.8) in Windows containers via NetFxBridge, no recompilation
- **CoreWCF Support** — Build new WCF-compatible services on .NET 8 + CoreWCF, runs on Linux containers
- **Multiple Runtimes** — `windows-netfx48`, `linux-corewcf`, `linux-net8`, `windows-net8`
- **Service Management** — Upload DLL + dependencies, deploy, and monitor via Portal or API
- **Auto-Scaling** — KEDA-based scaling on queue depth (0→50 pods)
- **Flow Control** — Three-tier back-pressure: Accept / Throttle / Reject
- **Leader Election** — Redis-based leader election for dispatcher coordination
- **Observability** — Prometheus metrics at `/metrics`, health checks at `/healthz`, web-based Portal
- **Authentication** — API Key middleware (production: Azure AD / JWT)

## 📐 Architecture

```
  SOA Clients
    ├── CloudSOA.Client.NetFx (.NET Framework 4.8 — for migrated HPC Pack clients)
    └── CloudSOA.Client        (.NET 8 — for new development)
         │  REST (HTTP)
         ▼
  Azure LB / Ingress → CloudSOA.Broker (2+ replicas, HPA)
         │                  ├── Session Manager
         │                  ├── Request Queue (Redis Streams)
         │                  ├── Dispatcher Engine
         │                  └── Response Cache (Redis)
         │  gRPC
         ▼
  ┌─────────────────────────────────────────────────────────────┐
  │  Service Hosts (pods auto-scaled by KEDA)                   │
  │                                                             │
  │  windows-netfx48  → ServiceHost.Wcf + NetFxBridge           │
  │                     Windows Server Core container            │
  │                     Loads .NET Framework 4.0–4.8 WCF DLLs   │
  │                                                             │
  │  linux-corewcf    → ServiceHost.CoreWcf                     │
  │                     Linux container                          │
  │                     Loads .NET 8 CoreWCF DLLs                │
  │                                                             │
  │  linux-net8       → ServiceHost                             │
  │                     Linux container                          │
  │                     Loads .NET 8 ISOAService DLLs            │
  │                                                             │
  │  windows-net8     → ServiceHost                             │
  │                     Windows Nano Server container             │
  │                     Loads .NET 8 ISOAService DLLs            │
  └─────────────────────────────────────────────────────────────┘
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

If you have a WCF service DLL that currently runs on HPC Pack SOA (e.g. `CalculatorService.dll`), you can deploy it to CloudSOA **without changing the DLL**. Only two things change in client code:

1. `using Microsoft.Hpc.Scheduler.Session;` → `using CloudSOA.Client;`
2. Head node name → Broker HTTP URL

The client can stay on **.NET Framework 4.8** — no need to upgrade to .NET 8.

### Step 1 — Create a Service Configuration File

Create a `.cloudsoa.config` XML file describing your service:

```xml
<?xml version="1.0" encoding="utf-8" ?>
<ServiceRegistration xmlns="urn:cloudsoa:service-config">
  <ServiceName>CalculatorService</ServiceName>
  <Version>1.0.0</Version>
  <Runtime>windows-netfx48</Runtime>
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

### Service Runtimes

The `Runtime` field determines which container hosts your service:

| Runtime | Container | OS | Use Case |
|---------|-----------|------|----------|
| `windows-netfx48` | ServiceHost.Wcf + NetFxBridge | Windows Server Core | **Existing HPC Pack SOA DLLs** (.NET Framework 4.0–4.8, no recompilation) |
| `linux-corewcf` | ServiceHost.CoreWcf | Linux | **New CoreWCF services** (.NET 8, WCF-compatible contracts) |
| `linux-net8` | ServiceHost | Linux | **New native services** (.NET 8, ISOAService interface) |
| `windows-net8` | ServiceHost | Windows Nano Server | .NET 8 services requiring Windows APIs |

> **How `windows-netfx48` works:** The .NET 8 gRPC host communicates with a **NetFxBridge** process — a .NET Framework 4.8 console app that loads and executes the legacy WCF DLL via stdin/stdout JSON protocol. This dual-process design allows the container to run both .NET 8 (for broker communication) and .NET Framework 4.8 (for your DLL) simultaneously.

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

### Step 3 — Update Client Code (two changes only)

Replace the HPC Pack namespace with `CloudSOA.Client`, and change the head node name to a broker URL:

```diff
- using Microsoft.Hpc.Scheduler.Session;
+ using CloudSOA.Client;

- SessionStartInfo info = new SessionStartInfo("my-headnode", "CalculatorService");
+ SessionStartInfo info = new SessionStartInfo("http://broker:5000", "CalculatorService");
```

All existing code works as-is — `Session`, `BrokerClient<T>`, `BrokerResponse<T>`, `SessionStartInfo`, `client.Close()` are all supported.

### Client Library — Choose Your .NET Version

| Client Library | NuGet Package | Target Framework | When to Use |
|----------------|--------------|-----------------|-------------|
| `CloudSOA.Client.NetFx` | `CloudSOA.Client.NetFx` | **.NET Framework 4.8** | Migrating existing HPC Pack clients (keep all existing code) |
| `CloudSOA.Client` | `CloudSOA.Client` | **.NET 8** | New client development, or upgrading existing clients |

Both libraries provide the **same API** — the same `using CloudSOA.Client;` namespace, the same classes. The only difference is the target framework.

**Existing client stays on .NET Framework 4.8:**
```xml
<!-- Client.csproj — just replace the HPC SDK reference -->
<PackageReference Include="CloudSOA.Client.NetFx" Version="1.0.0" />
<!-- Remove: <PackageReference Include="Microsoft.HPC.SDK" Version="5.1.6124" /> -->
```

**New client on .NET 8:**
```xml
<PackageReference Include="CloudSOA.Client" Version="1.0.0" />
```

### Complete Migration Example

**Before (HPC Pack, .NET Framework 4.8):**
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
        client.Close();
    }
    session.Close();
}
```

**After (CloudSOA, .NET Framework 4.8 — same framework, minimal changes):**
```csharp
using CloudSOA.Client;  // ← only this line changes

SessionStartInfo info = new SessionStartInfo("http://broker:5000", "CalculatorService");  // ← URL
using (Session session = Session.CreateSession(info))
{
    using (BrokerClient<ICalculator> client = new BrokerClient<ICalculator>(session))
    {
        client.SendRequest<AddRequest>(new AddRequest(1, 2));
        client.EndRequests();
        foreach (BrokerResponse<AddResponse> resp in client.GetResponses<AddResponse>())
            Console.WriteLine(resp.Result.AddResult);  // ← works! throws on fault (HPC Pack behavior)
        client.Close();  // ← still supported
    }
    session.Close();
}
```

> 💡 **For new services that don't need WCF compatibility**, use the **simplified API** with `CloudSession` and `CloudBrokerClient` — see `samples/CalculatorClient/` for both styles.

## 🏗️ Project Structure

```
CloudSOA/
├── src/
│   ├── CloudSOA.Common/              Shared models, interfaces, enums
│   ├── CloudSOA.Broker/              Session management, request routing, dispatch
│   │   ├── Controllers/              REST API (sessions, metrics)
│   │   ├── Services/                 gRPC service, session manager
│   │   ├── Queue/                    Redis Streams request queue + response store
│   │   ├── Dispatch/                 Dispatcher engine
│   │   ├── HA/                       Leader election
│   │   └── Metrics/                  Prometheus metrics
│   ├── CloudSOA.ServiceHost/         Linux/Windows compute node (.NET 8 native services)
│   ├── CloudSOA.ServiceHost.Wcf/     Windows compute node (existing .NET Fx 4.8 WCF DLLs)
│   │   └── Bridge/NetFxBridge        .NET Framework 4.8 bridge process
│   ├── CloudSOA.ServiceHost.CoreWcf/ Linux compute node (new .NET 8 CoreWCF services)
│   ├── CloudSOA.NetFxBridge/         .NET Framework 4.8 bridge (loads legacy DLLs)
│   ├── CloudSOA.ServiceManager/      Service registry + DLL storage (Azure Blob + CosmosDB)
│   ├── CloudSOA.Portal/              Blazor web UI (dashboard, monitoring, service mgmt)
│   ├── CloudSOA.Client/              Client SDK for .NET 8 (HPC Pack-compatible API)
│   └── CloudSOA.Client.NetFx/        Client SDK for .NET Framework 4.8 (same API)
├── samples/
│   ├── CalculatorService/            Sample WCF service DLL (ICalculator)
│   └── CalculatorClient/             Sample client (HPC-compat + raw API examples)
├── tests/                            Unit + Integration tests
├── deploy/k8s/                       Kubernetes manifests
├── infra/terraform/                  Azure infrastructure (IaC)
├── scripts/                          Build, deploy, test scripts (sh + ps1)
└── docs/                             Documentation
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
