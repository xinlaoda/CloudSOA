# HPC Pack SOA Broker → AKS 迁移方案

## 问题陈述

将 HPC Pack SOA 的 Head Node + Broker Node 功能迁移到 Azure AKS 集群中，自研实现一个云原生的 SOA Broker 服务，对外提供统一 DNS 地址，完全替代 HPC Pack Head Node 的 SOA Session 管理和 Broker 消息路由功能。

## 架构设计

### 整体架构

```
                        ┌─────────────────────┐
                        │    SOA Clients       │
                        │  (现有客户端代码)      │
                        └──────────┬───────────┘
                                   │ gRPC / REST / WCF-compatible
                                   ▼
                    ┌──────────────────────────────┐
                    │   Azure Load Balancer / AGIC  │
                    │   统一 DNS: soa.mycompany.com  │
                    └──────────────┬────────────────┘
                                   │
            ┌──────────────────────┼──────────────────────┐
            │                AKS Cluster                   │
            │                                              │
            │  ┌────────────────────────────────────────┐  │
            │  │         SOA Broker Service              │  │
            │  │  (Deployment, 多副本 + HPA)             │  │
            │  │                                        │  │
            │  │  ┌──────────┐  ┌───────────────────┐   │  │
            │  │  │ Session  │  │   Dispatcher      │   │  │
            │  │  │ Manager  │  │   Engine          │   │  │
            │  │  │ API      │  │                   │   │  │
            │  │  └──────────┘  └───────────────────┘   │  │
            │  │  ┌──────────┐  ┌───────────────────┐   │  │
            │  │  │ Request  │  │   Response        │   │  │
            │  │  │ Queue    │  │   Cache           │   │  │
            │  │  │ (ASQ/SB) │  │   (Redis)         │   │  │
            │  │  └──────────┘  └───────────────────┘   │  │
            │  └────────────────────────────────────────┘  │
            │                                              │
            │  ┌────────────────────────────────────────┐  │
            │  │      Service Host Pods (计算节点)       │  │
            │  │  ┌────────┐ ┌────────┐ ┌────────┐     │  │
            │  │  │ SvcHost│ │ SvcHost│ │ SvcHost│ ... │  │
            │  │  │  Pod   │ │  Pod   │ │  Pod   │     │  │
            │  │  └────────┘ └────────┘ └────────┘     │  │
            │  │  (Deployment/Job + KEDA 自动伸缩)      │  │
            │  └────────────────────────────────────────┘  │
            │                                              │
            │  ┌────────────────────────────────────────┐  │
            │  │        基础设施组件                      │  │
            │  │  Redis │ Azure Service Bus │ SQL/CosmosDB│ │
            │  └────────────────────────────────────────┘  │
            └──────────────────────────────────────────────┘
```

### 核心组件设计

#### 1. Session Manager (会话管理器)
- **职责**：创建/附加/关闭 Session，管理 Session 生命周期
- **API**：兼容 HPC Pack Session API 语义
- **存储**：Session 元数据存储在 Redis / CosmosDB
- **超时管理**：Session idle timeout, client idle timeout

#### 2. Dispatcher Engine (调度引擎)
- **职责**：将请求从队列分发到 Service Host Pod
- **负载均衡**：Round-robin / Least-connection / Queue-depth-aware
- **容错**：Pod 失败时自动重试，请求重新入队
- **流控**：基于队列深度的 back-pressure 机制

#### 3. Request Queue (请求队列)
- **选项 A**：Azure Service Bus Queue（持久化，Durable Session）
- **选项 B**：Azure Storage Queue（大吞吐，低成本）
- **选项 C**：Redis Streams（低延迟，Interactive Session）
- **建议**：Interactive Session 用 Redis，Durable Session 用 Service Bus

#### 4. Response Cache (响应缓存)
- **存储**：Azure Redis Cache
- **策略**：TTL 过期自动清理，客户端拉取后删除
- **Durable**：响应持久化到 Service Bus / Blob Storage

#### 5. Service Host Pod (计算Pod)
- **容器化**：将用户 WCF 服务 DLL 打包为 Docker 镜像
- **通信**：gRPC（推荐）或 WCF over NetTcp
- **伸缩**：KEDA 基于队列深度自动扩缩 Pod 数量

#### 6. Auto-Scaler (弹性伸缩)
- **KEDA**：监听 Service Bus / Redis 队列深度
- **HPA**：基于 CPU/Memory 的 Pod 水平伸缩
- **Cluster Autoscaler**：AKS 节点池自动伸缩

---

## 技术选型

| 组件 | 技术选型 | 理由 |
|------|---------|------|
| 编程语言 | C# / .NET 8 | 与现有 HPC Pack SOA 客户端兼容 |
| 服务框架 | ASP.NET Core + gRPC | 高性能，云原生 |
| 客户端兼容层 | WCF Core / CoreWCF | 兼容现有客户端代码 |
| 消息队列 | Azure Service Bus | 持久化、事务支持 |
| 缓存 | Azure Redis Cache | 低延迟响应缓存 |
| 元数据存储 | Azure CosmosDB / SQL | Session 元数据 |
| 容器编排 | AKS | 托管 K8s |
| 弹性伸缩 | KEDA + HPA + Cluster Autoscaler | 多层伸缩 |
| 服务网格 | 可选 Istio / Linkerd | mTLS, 可观测性 |
| 监控 | Azure Monitor + Prometheus + Grafana | 全栈监控 |
| DNS/Ingress | Azure Application Gateway Ingress Controller | 统一入口 |

---

## API 设计（兼容 HPC Pack SOA 语义）

### Session Management API

```
POST   /api/v1/sessions                    # CreateSession
GET    /api/v1/sessions/{sessionId}         # GetSession
POST   /api/v1/sessions/{sessionId}/attach  # AttachSession
DELETE /api/v1/sessions/{sessionId}         # CloseSession
GET    /api/v1/sessions/{sessionId}/status  # GetSessionStatus
```

### Broker Client API

```
POST   /api/v1/sessions/{sessionId}/requests          # SendRequest (batch)
POST   /api/v1/sessions/{sessionId}/requests/flush     # EndRequests
GET    /api/v1/sessions/{sessionId}/responses           # GetResponses (polling/streaming)
WebSocket /ws/v1/sessions/{sessionId}/responses         # GetResponses (push)
```

### gRPC Service Definition

```protobuf
service BrokerService {
  rpc CreateSession(CreateSessionRequest) returns (SessionInfo);
  rpc AttachSession(AttachSessionRequest) returns (SessionInfo);
  rpc CloseSession(CloseSessionRequest) returns (Empty);
  rpc SendRequests(stream BrokerRequest) returns (SendSummary);
  rpc GetResponses(GetResponsesRequest) returns (stream BrokerResponse);
  rpc SendAndGetResponses(stream BrokerRequest) returns (stream BrokerResponse);
}
```

### 客户端兼容 SDK

```csharp
// 目标：最小化客户端代码变更
// 替换命名空间即可
// Before: using Microsoft.Hpc.Scheduler.Session;
// After:  using CloudSOA.Client;

var session = await CloudSession.CreateSessionAsync(
    new SessionStartInfo("soa.mycompany.com", "MyService")
    {
        MinimumUnits = 4,
        MaximumUnits = 100,
        TransportScheme = TransportScheme.Grpc  // 新增 gRPC 选项
    });

using var client = new CloudBrokerClient<IMyService>(session);
// 后续代码与 HPC Pack SOA 完全一致
client.SendRequest(new MyRequest(data), userData: i);
client.EndRequests();
foreach (var resp in client.GetResponses<MyResponse>())
{
    var result = resp.Result;
}
```

---

## 实施计划（分阶段）

### Phase 1: 基础框架与 Session 管理
- 搭建 AKS 集群基础设施（Terraform/Bicep）
- 实现 Session Manager 服务（创建/附加/关闭/超时）
- 部署 Redis + Service Bus 基础组件
- 实现 Session 元数据存储（CosmosDB）

### Phase 2: 消息路由与调度
- 实现 Request Queue（入队/出队/持久化）
- 实现 Dispatcher Engine（负载均衡分发）
- 实现 Response Cache（结果缓存与拉取）
- 实现基本的流控机制

### Phase 3: Service Host 容器化
- 设计 Service Host 基础镜像
- 实现服务 DLL 动态加载机制
- 实现 Service Host ↔ Broker 通信（gRPC）
- KEDA 自动伸缩配置

### Phase 4: 客户端 SDK
- 实现兼容层 SDK（CloudSession / CloudBrokerClient）
- 支持 Interactive Session
- 支持 Durable Session
- 单元测试 + 集成测试

### Phase 5: 高级功能与生产就绪
- Broker 多副本 HA（Leader Election）
- 安全认证（Azure AD / mTLS）
- 监控告警（Prometheus metrics, Azure Monitor）
- 灰度迁移策略（双写/流量切换）

### Phase 6: 迁移与验证
- 选取典型 SOA 服务进行试点
- 性能基准测试（对比 HPC Pack）
- 逐步迁移生产工作负载
- 下线 HPC Pack Head Node

---

## 关键设计决策

### 1. 客户端兼容性策略
- **推荐**：提供新的 CloudSOA.Client SDK，API 语义与 HPC Pack 一致，仅改命名空间
- **可选**：通过 CoreWCF 提供完全二进制兼容的 WCF 端点（复杂度高）

### 2. Broker HA 策略
- 多副本 Deployment，无状态设计
- Session 状态外置到 Redis/CosmosDB
- 使用 K8s Leader Election 选举主调度器（避免重复分发）

### 3. 请求持久化
- Interactive Session：Redis Streams（低延迟）
- Durable Session：Azure Service Bus（持久化保证）

### 4. Service Host 部署模型
- **方案 A**：每个服务版本一个 Deployment（推荐，隔离性好）
- **方案 B**：通用 Service Host Pod + 动态加载 DLL（灵活但复杂）

### 5. 网络模型
- AKS Internal：Broker ↔ Service Host 使用 ClusterIP
- External：Client → Broker 使用 Azure LB + Ingress + TLS

---

## 风险与应对

| 风险 | 影响 | 应对措施 |
|------|------|---------|
| 客户端改造成本 | 现有客户端需要修改代码 | 提供高兼容 SDK，最小化改动 |
| 性能差距 | 云网络延迟 vs HPC InfiniBand | gRPC + 批量传输优化 |
| 消息丢失 | 队列故障导致请求丢失 | Service Bus 事务 + Dead Letter |
| Broker 单点故障 | 服务中断 | 多副本 + 无状态 + Leader Election |
| 大规模伸缩延迟 | KEDA/CA 扩容速度慢 | 预热 Pod 池 + 节点池预留 |
