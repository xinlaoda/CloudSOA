# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - 2026-02-18

### Added

#### Phase 1: Foundation & Session Management
- .NET 8 solution structure (Broker, Common, ServiceHost, Client)
- Session Manager with full lifecycle (Create/Attach/Close/Status/Timeout)
- Redis-backed session metadata store with TTL
- Dual-protocol API: REST + gRPC (broker.proto)
- Terraform IaC for Azure (AKS, Redis, Service Bus, CosmosDB, ACR)
- Kubernetes manifests (Deployment, Service, HPA, ConfigMap)
- Multi-stage Dockerfile for Broker

#### Phase 2: Message Routing & Dispatch
- Redis Streams request queue with consumer groups
- Dispatcher engine with per-session dispatch loops
- Redis response cache (fetch-and-delete semantics)
- Flow controller with 3-tier back-pressure (Accept/Throttle/Reject)
- Broker Client REST API (SendRequests, EndRequests, GetResponses)
- Dead letter queue for failed requests

#### Phase 3: Service Host
- ServiceHost gRPC service (compute.proto: Execute/HealthCheck/GetServiceInfo)
- Dynamic DLL loader for user services (ISOAService interface)
- Built-in EchoService for testing
- ServiceHost Dockerfile
- KEDA ScaledObject for queue-depth autoscaling (0-50 pods)
- Broker-side gRPC client with round-robin load balancing

#### Phase 4: Client SDK
- CloudSession — CreateSession/AttachSession/GetStatus/Close
- CloudBrokerClient — SendRequest/EndRequests/GetResponses
- Polling-based GetAllResponsesAsync with timeout
- HPC Pack API-compatible semantics

#### Phase 5: Production Readiness
- Redis-based Leader Election for dispatcher coordination
- API Key authentication middleware
- Prometheus metrics (22 custom + HTTP auto-instrumentation)
- `/metrics` and `/healthz` endpoints

#### DevOps & Documentation
- CI/CD GitHub Actions workflow
- 8 deployment/operational scripts
- Docker Compose for local development
- Comprehensive deployment documentation
- 22 unit tests + 4 integration tests
