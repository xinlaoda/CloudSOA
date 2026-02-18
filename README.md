# CloudSOA — Cloud-Native SOA Broker for AKS

[![Build](https://github.com/xinlaoda/CloudSOA/actions/workflows/ci.yaml/badge.svg)](https://github.com/xinlaoda/CloudSOA/actions/workflows/ci.yaml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/)

A cloud-native SOA Broker service that replaces Microsoft HPC Pack Head Node + Broker Node, deployed on Azure Kubernetes Service (AKS).

## ✨ Features

- **Session Management** — Create/Attach/Close sessions with idle timeout
- **Request Routing** — Redis Streams queue with dispatcher engine and round-robin load balancing
- **Response Caching** — Redis-backed response store with TTL and fetch-and-delete semantics
- **Dual Protocol** — REST API + gRPC for all operations
- **Client SDK** — Drop-in replacement for HPC Pack SOA client (change namespace only)
- **Auto-Scaling** — KEDA-based scaling on queue depth (0→50 pods)
- **Flow Control** — Three-tier back-pressure: Accept / Throttle / Reject
- **Leader Election** — Redis-based leader election for dispatcher coordination
- **Observability** — Prometheus metrics at `/metrics`, health checks at `/healthz`
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
  CloudSOA.ServiceHost (0-50 pods, KEDA)
        └── User Service DLL (dynamic loading)
```

## 🚀 Quick Start

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Docker](https://docs.docker.com/get-docker/)

### Local Development

```bash
# 1. Install dev environment
./scripts/setup-dev.sh

# 2. Start Redis
docker run -d --name cloudsoa-redis -p 6379:6379 redis:7-alpine

# 3. Run Broker
cd src/CloudSOA.Broker && dotnet run

# 4. Test
curl http://localhost:5000/healthz
```

### Client SDK Usage

```csharp
using CloudSOA.Client;

// Create session (replaces Microsoft.Hpc.Scheduler.Session)
var session = await CloudSession.CreateSessionAsync(
    new SessionStartInfo("http://broker:5000", "MyService")
    {
        MinimumUnits = 4,
        MaximumUnits = 100
    });

// Send requests
using var client = new CloudBrokerClient(session);
client.SendRequest("Calculate", payload, "item-1");
client.SendRequest("Calculate", payload, "item-2");
await client.EndRequestsAsync();

// Get responses
var responses = await client.GetAllResponsesAsync(expectedCount: 2);
foreach (var resp in responses)
    Console.WriteLine(resp.GetPayloadString());

await session.CloseAsync();
```

## 🏗️ Project Structure

```
CloudSOA/
├── src/
│   ├── CloudSOA.Common/          Shared models, interfaces, enums
│   ├── CloudSOA.Broker/          Main Broker service
│   │   ├── Controllers/          REST API endpoints
│   │   ├── Services/             Session Manager + gRPC service
│   │   ├── Queue/                Request queue + Response store
│   │   ├── Dispatch/             Dispatcher engine
│   │   ├── HA/                   Leader election
│   │   ├── Metrics/              Prometheus metrics
│   │   └── Protos/               gRPC proto definitions
│   ├── CloudSOA.ServiceHost/     Compute node (loads user DLLs)
│   └── CloudSOA.Client/          Client SDK
├── tests/                        Unit + Integration tests
├── deploy/k8s/                   Kubernetes manifests
├── infra/terraform/              Azure infrastructure (IaC)
├── scripts/                      Build, deploy, test scripts
└── docs/                         Documentation
```

## 🧪 Testing

```bash
# Unit tests
dotnet test --filter "Category!=Integration"

# Integration tests (requires running Broker)
dotnet test --filter "Category=Integration"

# Smoke test
./scripts/smoke-test.sh http://localhost:5000

# Load test
./scripts/load-test.sh http://localhost:5000 --requests 1000 --concurrency 10
```

## 🚢 Deployment

See [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) for detailed deployment guide.

```bash
# Azure infrastructure
./scripts/deploy-infra.sh --prefix cloudsoa --location eastus

# Build & push Docker images
./scripts/build-images.sh --acr cloudsoacr --tag v1.0.0

# Deploy to AKS
./scripts/deploy-k8s.sh --acr cloudsoacr.azurecr.io --tag v1.0.0

# Local Docker Compose
docker compose -f scripts/docker-compose.yaml up -d
```

## 📊 API Reference

### Session Management

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/v1/sessions` | Create session |
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

### System

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/healthz` | Health check |
| GET | `/metrics` | Prometheus metrics |

## 📝 License

This project is licensed under the MIT License — see the [LICENSE](LICENSE) file for details.
