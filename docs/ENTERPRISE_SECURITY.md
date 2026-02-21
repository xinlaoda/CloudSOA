# CloudSOA 企业级安全配置与部署指南

> **版本**: v1.6.0 | **最后更新**: 2026-02-21 | **适用场景**: 企业级金融、政府、医疗等高安全要求环境

---

## 目录

1. [安全架构总览](#1-安全架构总览)
2. [传输安全 (TLS/HTTPS)](#2-传输安全-tlshttps)
3. [身份认证 (Authentication)](#3-身份认证-authentication)
4. [角色授权 (RBAC Authorization)](#4-角色授权-rbac-authorization)
5. [审计日志 (Audit Logging)](#5-审计日志-audit-logging)
6. [网络隔离 (Network Policies)](#6-网络隔离-network-policies)
7. [客户端安全配置](#7-客户端安全配置)
8. [部署方案与最佳实践](#8-部署方案与最佳实践)
9. [与 HPC Pack SOA 安全对比](#9-与-hpc-pack-soa-安全对比)
10. [安全加固清单](#10-安全加固清单)

---

## 1. 安全架构总览

CloudSOA 采用 **纵深防御 (Defense-in-Depth)** 策略，提供五层安全保护：

```
┌────────────────────────────────────────────────────────────────────┐
│  外部客户端 (.NET 8 / .NET Fx 4.8)                                 │
│  ┌──────────────────────────────────────────────────────────────┐  │
│  │  ① 传输安全 — TLS 1.2/1.3 加密通信                           │  │
│  │  ② 身份认证 — JWT Bearer / API Key / Azure AD               │  │
│  │  ③ 授权检查 — RBAC (Admin / User / Reader)                  │  │
│  └──────────────────┬───────────────────────────────────────────┘  │
│                     │ HTTPS / gRPC+TLS                             │
│  ┌──────────────────▼───────────────────────────────────────────┐  │
│  │  Broker (API Gateway)                                        │  │
│  │  ├─ AuthenticationMiddleware   (JWT 验证 / API Key 校验)     │  │
│  │  ├─ AuthorizationMiddleware    (RBAC 角色检查)               │  │
│  │  ├─ AuditLoggingMiddleware     (④ 审计日志)                  │  │
│  │  └─ 业务逻辑 (Session/Request/Response)                      │  │
│  └──────────────────┬───────────────────────────────────────────┘  │
│                     │ gRPC (内部)                                   │
│  ┌──────────────────▼───────────────────────────────────────────┐  │
│  │  ⑤ K8s NetworkPolicy 网络隔离                                │  │
│  │  ├─ ServiceHost: 仅 Broker 可访问 (gRPC :5010)               │  │
│  │  ├─ Redis: 仅 Broker 可访问 (:6379)                          │  │
│  │  ├─ ServiceManager: 仅 Portal + Broker 可访问 (:5020)        │  │
│  │  └─ 默认拒绝所有入站流量 (default-deny-ingress)              │  │
│  └──────────────────────────────────────────────────────────────┘  │
└────────────────────────────────────────────────────────────────────┘
```

### 安全组件与版本对应

| 安全层 | 组件 | 引入版本 | 配置项 |
|--------|------|---------|--------|
| 传输安全 | Kestrel TLS / Nginx Ingress | v1.2.0 | `Tls:Mode` |
| 身份认证 | AuthenticationMiddleware | v1.3.0 | `Authentication:Mode` |
| 角色授权 | AuthorizationMiddleware | v1.4.0 | (与认证联动) |
| 审计日志 | AuditLoggingMiddleware | v1.5.0 | (自动启用) |
| 网络隔离 | K8s NetworkPolicy + Calico | v1.6.0 | `network-policies.yaml` |

---

## 2. 传输安全 (TLS/HTTPS)

### 2.1 三种 TLS 部署模式

CloudSOA 支持三种 TLS 终止方式，适应不同的企业部署场景：

| 模式 | 配置值 | TLS 终止点 | 适用场景 | 成本 |
|------|--------|-----------|----------|------|
| **Direct** | `Tls:Mode=direct` | Kestrel (Broker 进程) | 端到端加密, 无 Ingress | 免费 |
| **Ingress** | `Tls:Mode=ingress` | Nginx Ingress Controller | 标准企业部署 | 免费 |
| **AGIC** | `Tls:Mode=ingress` | Azure Application Gateway | 企业级 WAF + TLS 卸载 | ~$200+/月 |
| **None** | `Tls:Mode=none` | 不加密 | 仅限开发/测试 | - |

### 2.2 模式一：Direct (Kestrel 直接 TLS)

Broker 内嵌 TLS，客户端直接建立端到端加密连接，不依赖任何外部组件。

**配置 (appsettings.json 或环境变量):**

```json
{
  "Tls": {
    "Mode": "direct",
    "CertPath": "/certs/server.pfx",
    "CertPassword": "your-cert-password",
    "KeyPath": ""
  }
}
```

**等效环境变量:**

```bash
Tls__Mode=direct
Tls__CertPath=/certs/server.pfx
Tls__CertPassword=your-cert-password
```

**端口说明:**

| 端口 | 协议 | 用途 |
|------|------|------|
| 5000 | HTTP | 健康检查 + Prometheus 指标 (自动 308 重定向) |
| 5443 | HTTPS | REST API (客户端访问) |
| 5001 | HTTP/2 | gRPC (内部通信) |
| 5444 | gRPC+TLS | gRPC 加密通信 |

**HTTP 自动重定向**: 对 HTTP `:5000` 上的非 `/healthz`、`/metrics` 路径，Broker 自动返回 308 重定向到 `https://<host>:5443`。

**生成自签名证书 (测试用):**

```bash
# 使用 OpenSSL 生成 PFX 证书
openssl req -x509 -newkey rsa:4096 -keyout key.pem -out cert.pem -days 365 -nodes \
  -subj "/CN=cloudsoa-broker"
openssl pkcs12 -export -out server.pfx -inkey key.pem -in cert.pem -password pass:MyPassword

# 创建 K8s Secret
kubectl create secret generic broker-tls-cert \
  --from-file=server.pfx=server.pfx \
  -n cloudsoa
```

**Deployment 挂载证书:**

```yaml
# 在 broker-deployment.yaml 中添加
spec:
  template:
    spec:
      containers:
        - name: broker
          volumeMounts:
            - name: tls-cert
              mountPath: /certs
              readOnly: true
          env:
            - name: Tls__Mode
              value: "direct"
            - name: Tls__CertPath
              value: "/certs/server.pfx"
            - name: Tls__CertPassword
              valueFrom:
                secretKeyRef:
                  name: broker-tls-cert
                  key: password
      volumes:
        - name: tls-cert
          secret:
            secretName: broker-tls-cert
```

### 2.3 模式二：Nginx Ingress Controller

TLS 在 Nginx Ingress 层终止，Broker 内部保持 HTTP 明文。适合标准 Kubernetes 部署。

**安装步骤:**

```bash
# 1. 安装 Nginx Ingress Controller
helm repo add ingress-nginx https://kubernetes.github.io/ingress-nginx
helm install ingress-nginx ingress-nginx/ingress-nginx \
  --namespace ingress-nginx --create-namespace \
  --set controller.service.annotations."service\.beta\.kubernetes\.io/azure-load-balancer-health-probe-request-path"=/healthz

# 2. (可选) 安装 cert-manager 自动签发 Let's Encrypt 证书
helm repo add jetstack https://charts.jetstack.io
helm install cert-manager jetstack/cert-manager \
  --namespace cert-manager --create-namespace \
  --set crds.enabled=true

# 3. 创建 TLS Secret (二选一)
# 方式 A: 手动上传企业 CA 签发的证书
kubectl create secret tls cloudsoa-tls \
  --cert=tls.crt --key=tls.key -n cloudsoa

# 方式 B: 使用 cert-manager + Let's Encrypt (自动签发)
# 取消 broker-ingress-nginx.yaml 中 ClusterIssuer 的注释

# 4. 部署 Ingress 规则
kubectl apply -f deploy/k8s/broker-ingress-nginx.yaml
```

**Ingress 配置关键项 (`broker-ingress-nginx.yaml`):**

```yaml
annotations:
  nginx.ingress.kubernetes.io/ssl-redirect: "true"          # 强制 HTTPS
  nginx.ingress.kubernetes.io/proxy-read-timeout: "300"     # SOA 长操作超时
  nginx.ingress.kubernetes.io/proxy-send-timeout: "300"
  nginx.ingress.kubernetes.io/proxy-body-size: "100m"       # DLL 上传大小限制
spec:
  tls:
    - hosts:
        - soa.yourcompany.com          # 替换为你的域名
      secretName: cloudsoa-tls
```

### 2.4 模式三：Azure Application Gateway (AGIC)

适合需要 Web 应用防火墙 (WAF)、DDoS 防护的企业级场景。

```bash
# 安装 AGIC 插件
az aks enable-addons -n xxin-cloudsoa-aks -g xxin-cloudsoa-rg \
  --addons ingress-appgw --appgw-subnet-cidr 10.225.0.0/16

# 部署 Ingress (使用 broker-ingress.yaml 中 appgw class)
kubectl apply -f deploy/k8s/broker-ingress.yaml
```

### 2.5 客户端 TLS 配置

**互信 TLS (Mutual TLS / mTLS):**

客户端可提供证书进行双向认证，用于零信任环境：

```csharp
// .NET 8 客户端
var info = new SessionStartInfo("https://soa.yourcompany.com", "MyService")
{
    ClientCertificate = new X509Certificate2("client.pfx", "password"),
    AcceptUntrustedCertificates = false  // 生产环境必须为 false
};

// .NET Framework 4.8 客户端
var info = new SessionStartInfo("https://soa.yourcompany.com", "MyService")
{
    ClientCertificate = new X509Certificate2("client.pfx", "password")
};
```

---

## 3. 身份认证 (Authentication)

### 3.1 认证模式概览

| 模式 | 配置值 | 适用场景 | 安全等级 |
|------|--------|---------|---------|
| **JWT Bearer** | `Authentication:Mode=jwt` | Azure AD / 企业 SSO / 自定义令牌 | ★★★★★ |
| **API Key** | `Authentication:Mode=apikey` | 服务间调用 / 自动化脚本 | ★★★☆☆ |
| **Anonymous** | `Authentication:Mode=none` | 开发/测试 | ★☆☆☆☆ |

> ⚠️ **生产环境必须使用 `jwt` 或 `apikey` 模式**。`none` 模式不进行任何认证检查。

### 3.2 JWT Bearer 认证

适用于企业级 SSO 集成、Azure Active Directory、以及自定义身份提供者。

**Broker 配置:**

```json
{
  "Authentication": {
    "Mode": "jwt",
    "Jwt": {
      "Issuer": "https://login.microsoftonline.com/{tenant-id}/v2.0",
      "Audience": "api://cloudsoa-broker",
      "SigningKey": ""
    },
    "ApiKey": "fallback-api-key-for-automation"
  }
}
```

**等效 K8s 环境变量:**

```yaml
env:
  - name: Authentication__Mode
    value: "jwt"
  - name: Authentication__Jwt__Issuer
    value: "https://login.microsoftonline.com/{tenant-id}/v2.0"
  - name: Authentication__Jwt__Audience
    value: "api://cloudsoa-broker"
  - name: Authentication__ApiKey
    valueFrom:
      secretKeyRef:
        name: broker-secrets
        key: api-key
```

#### 3.2.1 Azure AD 集成

前置条件: 需要 Azure AD 应用程序管理员 或 全局管理员权限。

**步骤:**

1. **注册 Azure AD 应用:**
   ```bash
   # 注册 API 应用 (Broker)
   az ad app create --display-name "CloudSOA Broker" \
     --identifier-uris "api://cloudsoa-broker"
   
   # 创建应用角色
   az ad app update --id <app-id> --app-roles '[
     {"displayName":"SOA Admin","value":"Admin","allowedMemberTypes":["User","Application"]},
     {"displayName":"SOA User","value":"User","allowedMemberTypes":["User","Application"]},
     {"displayName":"SOA Reader","value":"Reader","allowedMemberTypes":["User","Application"]}
   ]'
   ```

2. **配置 Broker:**
   ```
   Authentication__Mode=jwt
   Authentication__Jwt__Issuer=https://login.microsoftonline.com/{tenant-id}/v2.0
   Authentication__Jwt__Audience=api://cloudsoa-broker
   # SigningKey 留空 — Azure AD 使用 OIDC 发现端点自动获取签名密钥
   ```

3. **客户端获取令牌:**
   ```csharp
   // 使用 MSAL 获取 Azure AD 令牌
   var app = ConfidentialClientApplicationBuilder
       .Create("{client-id}")
       .WithClientSecret("{client-secret}")
       .WithAuthority("https://login.microsoftonline.com/{tenant-id}")
       .Build();
   var token = await app.AcquireTokenForClient(
       new[] { "api://cloudsoa-broker/.default" }).ExecuteAsync();
   
   var info = new SessionStartInfo("https://broker-endpoint", "MyService")
   {
       BearerToken = token.AccessToken
   };
   ```

#### 3.2.2 自定义 JWT (非 Azure AD)

使用 HMAC-SHA256 对称密钥签发自定义 JWT，适用于私有环境：

```
Authentication__Mode=jwt
Authentication__Jwt__Issuer=cloudsoa-issuer
Authentication__Jwt__Audience=cloudsoa-broker
Authentication__Jwt__SigningKey=YourSuperSecretKeyAtLeast256BitsLong!
```

**JWT Payload 要求:**

```json
{
  "sub": "user-id",
  "iss": "cloudsoa-issuer",
  "aud": "cloudsoa-broker",
  "iat": 1740100000,
  "exp": 1740103600,
  "http://schemas.microsoft.com/ws/2008/06/identity/claims/role": ["User"],
  "preferred_username": "john.doe@company.com"
}
```

> 支持的角色声明 (Claim) 字段:
> - `http://schemas.microsoft.com/ws/2008/06/identity/claims/role` (WCF/Azure AD 标准)
> - `roles` (Azure AD v2.0 应用角色)

#### 3.2.3 JWT + API Key 双重认证

在 `jwt` 模式下，API Key 作为 **回退机制** 自动启用。认证顺序为：

1. 优先检查 `Authorization: Bearer <token>` 头
2. 若无 Bearer Token，检查 `X-Api-Key: <key>` 头
3. 两者均无则返回 `401 Unauthorized`

这允许自动化脚本使用 API Key，而用户通过 SSO 获取 JWT。

### 3.3 API Key 认证

适用于服务间调用、CI/CD 管道、自动化脚本。

**Broker 配置:**

```yaml
Authentication__Mode=apikey
Authentication__ApiKey=your-strong-random-api-key-here
```

**安全特性:**
- 使用 `CryptographicOperations.FixedTimeEquals` 进行 **恒定时间比较**，防止计时攻击
- API Key 用户自动获得 `Admin` 角色
- 建议使用 64+ 字符的随机密钥

**生成强密钥:**

```bash
# Linux/macOS
openssl rand -hex 32

# PowerShell
-join ((1..64) | ForEach-Object { '{0:x}' -f (Get-Random -Maximum 16) })
```

### 3.4 公开端点 (无需认证)

以下端点始终开放，不受认证模式影响：

| 端点 | 用途 |
|------|------|
| `/healthz` | K8s 健康检查探针 |
| `/metrics` | Prometheus 指标抓取 |
| `/` | 服务标识 (返回服务名) |

### 3.5 用户名/密码认证 (不支持)

> ⚠️ **CloudSOA 不支持用户名/密码认证**。

HPC Pack SOA 使用 Windows 集成认证 (NTLM/Kerberos) 和 Active Directory。CloudSOA 作为云原生平台运行在 Linux 容器中，不支持 Windows 域认证。

为保持 API 兼容性，`SessionStartInfo.Username` 和 `SessionStartInfo.Password` 属性被 **保留** 但在运行时设置非空值将立即抛出 `NotSupportedException`：

```csharp
// ❌ 运行时抛出 NotSupportedException
var info = new SessionStartInfo("https://broker", "MyService")
{
    Username = "domain\\user",   // 设置后调用会失败
    Password = "password"
};

// ✅ 正确迁移方式
var info = new SessionStartInfo("https://broker", "MyService")
{
    BearerToken = "your-jwt-token"  // 或 ApiKey = "your-api-key"
};
```

---

## 4. 角色授权 (RBAC Authorization)

### 4.1 角色层级

CloudSOA 采用三级角色体系，**上级角色自动包含下级角色所有权限**：

```
Admin ──→ User ──→ Reader
  │         │         │
  │         │         └─ 只读 (列表、查询、状态)
  │         └─ 会话操作 (创建、关闭、提交请求)
  └─ 管理操作 (服务注册、部署、删除、伸缩)
```

### 4.2 端点与角色映射

| API 端点 | HTTP 方法 | 最低角色 | 说明 |
|----------|-----------|---------|------|
| `GET /api/v1/sessions` | GET | Reader | 列出所有会话 |
| `POST /api/v1/sessions` | POST | User | 创建会话 |
| `DELETE /api/v1/sessions/{id}` | DELETE | User | 关闭会话 |
| `POST /api/v1/sessions/{id}/requests` | POST | User | 提交计算请求 |
| `GET /api/v1/sessions/{id}/results` | GET | Reader | 获取计算结果 |
| `GET /api/v1/sessions/{id}/status` | GET | Reader | 查询会话状态 |
| `GET /api/v1/services` | GET | Reader | 列出服务 |
| `POST /api/v1/services` | POST | Admin | 注册新服务 |
| `POST /api/v1/services/{name}/deploy` | POST | Admin | 部署服务 |
| `POST /api/v1/services/{name}/stop` | POST | Admin | 停止服务 |
| `POST /api/v1/services/{name}/scale` | POST | Admin | 伸缩服务 |
| `DELETE /api/v1/services/{name}` | DELETE | Admin | 删除服务 |
| `PUT /api/v1/services/{name}` | PUT | Admin | 更新服务 |
| `GET /api/v1/metrics` | GET | Reader | 集群指标 |
| 其他 `/api/` 路径 | 任意 | User | 默认最低权限 |

### 4.3 角色来源

| 认证方式 | 角色获取方式 |
|---------|------------|
| JWT Bearer | 从令牌中提取 `ClaimTypes.Role` 或 `roles` 声明 |
| API Key | 固定为 `Admin` (API Key 代表管理员) |
| Anonymous (none) | 跳过授权检查 (开发模式) |

### 4.4 Azure AD 应用角色配置

在 Azure AD 中配置应用角色后，分配用户/组到对应角色。JWT 令牌中会自动包含 `roles` 声明：

```json
{
  "roles": ["Admin"],
  "preferred_username": "admin@company.com"
}
```

### 4.5 授权失败响应

| 状态码 | 含义 | 示例 |
|--------|------|------|
| `401 Unauthorized` | 未认证或令牌无效 | 缺少/过期的 Bearer Token |
| `403 Forbidden` | 已认证但权限不足 | Reader 角色尝试创建会话 |

**403 响应体:**
```
Forbidden: requires Admin role.
```

**Broker 日志:**
```
warn: CloudSOA.Broker.Middleware.AuthorizationMiddleware
  Forbidden: user=john.doe roles=[Reader] required=Admin path=/api/v1/services
```

---

## 5. 审计日志 (Audit Logging)

### 5.1 审计策略

CloudSOA 记录所有安全相关事件，以满足金融行业合规要求 (SOC 2, PCI-DSS, 等保):

| 事件类型 | HTTP 方法 | 日志级别 | 触发条件 |
|---------|-----------|---------|---------|
| 变更操作 | POST/PUT/PATCH/DELETE | `Information` | 始终记录 |
| 读取失败 | GET (4xx/5xx) | `Warning` | 返回错误时 |
| 成功读取 | GET (2xx) | — | 不记录 (降低噪音) |
| 非 API 路径 | 任意 | — | 不记录 |

### 5.2 日志格式

```
[AUDIT] {Method} {Path} by {Identity} → {StatusCode} ({Duration}ms)
```

**示例输出:**

```
info: CloudSOA.Broker.Middleware.AuditLoggingMiddleware
  [AUDIT] POST /api/v1/sessions by john.doe[User] → 201 (361ms)

info: CloudSOA.Broker.Middleware.AuditLoggingMiddleware
  [AUDIT] POST /api/v1/services/MyService/deploy by apikey-user[Admin] → 200 (1250ms)

warn: CloudSOA.Broker.Middleware.AuditLoggingMiddleware
  [AUDIT] DELETE /api/v1/services/MyService by reader-user[Reader] → 403 (2ms)

warn: CloudSOA.Broker.Middleware.AuditLoggingMiddleware
  [AUDIT] GET /api/v1/sessions by anonymous → 401 (1ms)
```

### 5.3 身份标识规则

| 认证状态 | 标识格式 | 示例 |
|---------|---------|------|
| JWT 认证 | `{name}[{roles}]` | `john.doe[Admin]` |
| API Key 认证 | `apikey-user[Admin]` | `apikey-user[Admin]` |
| 未认证 (none) | `anonymous` | `anonymous` |

### 5.4 日志收集与监控

审计日志输出到 **stdout**，被 AKS 容器运行时捕获，自动流入 Azure Monitor：

```bash
# 查看实时审计日志
kubectl logs -f -l app=broker -n cloudsoa | grep "\[AUDIT\]"

# Azure Monitor 查询 (KQL)
ContainerLog
| where LogEntry contains "[AUDIT]"
| parse LogEntry with * "[AUDIT] " Method " " Path " by " Identity " → " StatusCode " (" Duration "ms)"
| project TimeGenerated, Method, Path, Identity, StatusCode, Duration
| order by TimeGenerated desc
```

**建议的告警规则:**

| 告警 | 条件 | 严重级别 |
|------|------|---------|
| 认证失败激增 | 5分钟内 >50 次 401 | 高 |
| 授权失败 | 任何 403 事件 | 中 |
| 服务注册/删除 | 任何服务生命周期变更 | 信息 |
| 异常高延迟 | Duration > 30000ms | 中 |

---

## 6. 网络隔离 (Network Policies)

### 6.1 零信任网络架构

CloudSOA 使用 Kubernetes NetworkPolicy 实现 **微分段 (Microsegmentation)**：

```
                     ┌──────────────────────┐
                     │    外部客户端          │
                     └──────┬───────────────┘
                            │ :5000/:5443 (REST)
                            │ :5001/:5444 (gRPC)
                     ┌──────▼───────────────┐
               ┌─────│    Broker             │─────┐
               │     └──────────────────────┘     │
               │ :5010 (gRPC)              :6379  │
        ┌──────▼───────────────┐   ┌──────▼──────┐
        │  ServiceHost (服务)   │   │   Redis      │
        │  svc-calculator      │   └─────────────┘
        │  svc-heavycompute    │
        └──────────────────────┘
                            │
               ┌────────────┘  :5020
        ┌──────▼───────────────┐    ┌────────────────┐
        │  ServiceManager      │◄───│  Portal (:5030) │◄── 外部管理员
        └──────────────────────┘    └────────────────┘
```

**流量矩阵 (✅ 允许 / ❌ 禁止):**

| 源 → 目标 | Broker | ServiceHost | ServiceManager | Portal | Redis |
|-----------|--------|-------------|---------------|--------|-------|
| **外部** | ✅ | ❌ | ❌ | ✅ | ❌ |
| **Broker** | - | ✅ :5010 | ✅ :5020 | ❌ | ✅ :6379 |
| **Portal** | ✅ :5000 | ❌ | ✅ :5020 | - | ❌ |
| **ServiceHost** | ❌ | ❌ | ❌ | ❌ | ❌ |

### 6.2 部署网络策略

```bash
# 前提: AKS 集群须启用网络策略引擎 (Azure/Calico)
az aks show -g <rg> -n <aks> --query "networkProfile.networkPolicy"
# 应返回 "calico" 或 "azure"

# 部署所有策略
kubectl apply -f deploy/k8s/network-policies.yaml

# 验证
kubectl get networkpolicies -n cloudsoa
```

**包含的策略:**

| 策略名 | 保护目标 | 允许来源 |
|--------|---------|---------|
| `default-deny-ingress` | 命名空间所有 Pod | (无 — 默认拒绝) |
| `allow-broker-ingress` | Broker | 任意来源 (LoadBalancer) + Portal |
| `allow-servicehost-ingress` | 所有服务 Pod | 仅 Broker (:5010) |
| `allow-servicemanager-ingress` | ServiceManager | 仅 Portal + Broker (:5020) |
| `allow-portal-ingress` | Portal | 任意来源 (:5030) |
| `allow-redis-ingress` | Redis | 仅 Broker (:6379) |

### 6.3 新建 AKS 集群时启用 Calico

```bash
az aks create -g <rg> -n <aks> \
  --network-plugin azure \
  --network-policy calico \
  --node-count 3
```

---

## 7. 客户端安全配置

### 7.1 .NET 8 客户端 (CloudSOA.Client)

```csharp
using CloudSOA.Client;

// === 场景 1: JWT Bearer 认证 ===
var info = new SessionStartInfo("https://soa.yourcompany.com", "CalculatorService")
{
    BearerToken = "eyJhbGciOiJIUzI1NiIs...",  // JWT 令牌
    Secure = true
};

// === 场景 2: API Key 认证 ===
var info = new SessionStartInfo("https://soa.yourcompany.com", "CalculatorService")
{
    ApiKey = "your-api-key-here"
};

// === 场景 3: 互信 TLS (mTLS) + JWT ===
var info = new SessionStartInfo("https://soa.yourcompany.com", "CalculatorService")
{
    BearerToken = token,
    ClientCertificate = new X509Certificate2("client.pfx", "password"),
    AcceptUntrustedCertificates = false  // 生产环境必须 false
};

// === 场景 4: 自定义头 (高级) ===
var info = new SessionStartInfo("https://soa.yourcompany.com", "CalculatorService");
info.Properties["X-Correlation-Id"] = Guid.NewGuid().ToString();
info.Properties["X-Tenant-Id"] = "tenant-123";

// 创建会话并使用
using var session = await CloudSession.CreateSessionAsync(info);
using var client = new BrokerClient<ICalculator>(session);
client.SendRequest(new CalculateRequest { A = 100, B = 200 });
var responses = client.GetResponses<CalculateResult>();
```

### 7.2 .NET Framework 4.8 客户端 (CloudSOA.Client.NetFx)

用于 HPC Pack SOA 迁移场景，API 兼容现有代码：

```csharp
using CloudSOA.Client;

// HPC Pack 迁移: 替换 headNode 为 CloudSOA broker 地址
var info = new SessionStartInfo("https://soa.yourcompany.com", "CalculatorService")
{
    // 旧代码 (不支持, 删除):
    // Username = "domain\\user",
    // Password = "password",
    
    // 新代码:
    BearerToken = "your-jwt-token",        // 或 ApiKey = "your-key"
    AcceptUntrustedCertificates = false
};

using (var session = Session.CreateSession(info))
using (var client = new BrokerClient<ICalculator>(session))
{
    client.SendRequest(new CalculateRequest { A = 100, B = 200 });
    foreach (var resp in client.GetResponses<CalculateResult>())
    {
        Console.WriteLine($"Result: {resp.Result.Sum}");
    }
}
```

**TLS 安全行为 (.NET Framework):**
- 自动设置 `ServicePointManager.SecurityProtocol = Tls12 | Tls13`
- 拒绝 TLS 1.0 / TLS 1.1 等不安全协议

### 7.3 客户端安全属性参考

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `BearerToken` | string? | null | JWT Bearer 令牌, 设置后自动添加 `Authorization: Bearer` 头 |
| `ApiKey` | string? | null | API Key, 设置后自动添加 `X-Api-Key` 头 |
| `ClientCertificate` | X509Certificate2? | null | 客户端证书 (mTLS) |
| `AcceptUntrustedCertificates` | bool | false | 接受自签名证书 (仅限开发) |
| `Secure` | bool | true | HPC Pack 兼容字段 |
| `Username` | string? | null | **不支持** — 设置后运行时抛出 NotSupportedException |
| `Password` | string? | null | **不支持** — 设置后运行时抛出 NotSupportedException |
| `Properties` | Dictionary | (空) | 自定义 HTTP 头 (键值对) |

---

## 8. 部署方案与最佳实践

### 8.1 生产环境部署方案

以下为推荐的企业级生产部署配置：

```yaml
# broker-configmap-production.yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: broker-config
  namespace: cloudsoa
data:
  # TLS (通过 Ingress 终止)
  Tls__Mode: "ingress"
  
  # 认证 (JWT + API Key 双重)
  Authentication__Mode: "jwt"
  Authentication__Jwt__Issuer: "https://login.microsoftonline.com/{tenant-id}/v2.0"
  Authentication__Jwt__Audience: "api://cloudsoa-broker"
  
  # Redis (使用 Azure Redis Cache)
  ConnectionStrings__Redis: "your-redis.redis.cache.windows.net:6380,password=...,ssl=True,abortConnect=False"
  
  # 日志级别
  Logging__LogLevel__Default: "Information"
```

```yaml
# broker-secrets.yaml (使用 kubectl create secret 而非明文提交)
apiVersion: v1
kind: Secret
metadata:
  name: broker-secrets
  namespace: cloudsoa
type: Opaque
data:
  api-key: <base64-encoded-api-key>
  jwt-signing-key: <base64-encoded-signing-key>   # 仅自定义 JWT 需要
```

```bash
# 创建 Secret (推荐方式, 避免 YAML 中写明文)
kubectl create secret generic broker-secrets \
  --from-literal=api-key="$(openssl rand -hex 32)" \
  --from-literal=jwt-signing-key="YourSigningKeyAtLeast256BitsLong!" \
  -n cloudsoa
```

### 8.2 部署环境矩阵

| 环境 | TLS 模式 | 认证模式 | 网络策略 | 审计日志 | 副本数 |
|------|---------|---------|---------|---------|--------|
| **开发** | none | none | 不部署 | ✅ (默认) | 1 |
| **测试** | direct (自签名) | apikey | 部署 | ✅ | 1-2 |
| **预发布** | ingress (企业 CA) | jwt | 部署 | ✅ | 2-3 |
| **生产** | ingress (企业 CA) | jwt + apikey | 部署 | ✅ | 3+ (HPA) |

### 8.3 一键部署脚本

```bash
#!/bin/bash
# deploy-secure.sh — 安全生产环境部署

RESOURCE_GROUP="your-rg"
AKS_NAME="your-aks"
ACR_NAME="your-acr"
NAMESPACE="cloudsoa"
DOMAIN="soa.yourcompany.com"

# 1. 连接 AKS
az aks get-credentials -g $RESOURCE_GROUP -n $AKS_NAME

# 2. 创建命名空间
kubectl create namespace $NAMESPACE --dry-run=client -o yaml | kubectl apply -f -

# 3. 部署 TLS 证书
kubectl create secret tls cloudsoa-tls \
  --cert=certs/server.crt --key=certs/server.key \
  -n $NAMESPACE

# 4. 创建认证密钥
kubectl create secret generic broker-secrets \
  --from-literal=api-key="$(openssl rand -hex 32)" \
  -n $NAMESPACE

# 5. 部署基础设施
kubectl apply -f deploy/k8s/namespace.yaml
kubectl apply -f deploy/k8s/redis.yaml
kubectl apply -f deploy/k8s/broker-configmap.yaml

# 6. 部署网络策略
kubectl apply -f deploy/k8s/network-policies.yaml

# 7. 部署服务
kubectl apply -f deploy/k8s/broker-deployment.yaml
kubectl apply -f deploy/k8s/servicemanager-deployment.yaml
kubectl apply -f deploy/k8s/portal-deployment.yaml

# 8. 部署 Ingress (选择一种)
# 方式 A: Nginx Ingress
kubectl apply -f deploy/k8s/broker-ingress-nginx.yaml
# 方式 B: AGIC
# kubectl apply -f deploy/k8s/broker-ingress.yaml

# 9. 验证
kubectl get pods -n $NAMESPACE
kubectl get svc -n $NAMESPACE
kubectl get networkpolicies -n $NAMESPACE
echo "Broker: $(kubectl get svc broker-service -n $NAMESPACE -o jsonpath='{.status.loadBalancer.ingress[0].ip}')"
```

### 8.4 密钥管理建议

| 密钥类型 | 存储位置 | 推荐方案 |
|---------|---------|---------|
| API Key | K8s Secret | Azure Key Vault + CSI Driver |
| JWT Signing Key | K8s Secret | Azure Key Vault + CSI Driver |
| TLS 证书 | K8s TLS Secret | cert-manager + Let's Encrypt 或企业 CA |
| Redis 密码 | K8s Secret | Azure Key Vault + CSI Driver |
| Blob/Cosmos 连接串 | K8s Secret | Azure Managed Identity |

**Azure Key Vault 集成 (推荐):**

```bash
# 安装 CSI Driver
az aks enable-addons -g <rg> -n <aks> --addons azure-keyvault-secrets-provider

# 创建 Key Vault
az keyvault create -n cloudsoa-kv -g <rg>

# 存储密钥
az keyvault secret set --vault-name cloudsoa-kv --name api-key --value "$(openssl rand -hex 32)"
az keyvault secret set --vault-name cloudsoa-kv --name jwt-signing-key --value "YourKey..."
```

---

## 9. 与 HPC Pack SOA 安全对比

| 安全能力 | HPC Pack SOA | CloudSOA | 说明 |
|---------|-------------|----------|------|
| **传输加密** | WCF Transport Security (HTTPS/NetTcp) | TLS 1.2/1.3 (Kestrel/Ingress) | 等效 |
| **消息加密** | WCF Message Security | — | CloudSOA 依赖传输层加密 |
| **Windows 域认证** | NTLM / Kerberos | ❌ 不支持 | 云原生环境无 AD 域 |
| **Azure AD** | Azure AD (HPC Pack 2019+) | ✅ JWT Bearer | 完全支持 |
| **证书认证** | X.509 客户端证书 | ✅ mTLS | 等效 |
| **用户名/密码** | ✅ (AD 验证) | ❌ 属性保留, 运行时拒绝 | 迁移需改用 JWT/ApiKey |
| **RBAC** | HPC Job/Task 权限 | Admin/User/Reader 三级 | 粒度更细 |
| **审计日志** | Windows Event Log | 结构化日志 → Azure Monitor | 更易集成 SIEM |
| **网络隔离** | Windows 防火墙 | K8s NetworkPolicy (Calico) | 更精细微分段 |
| **密钥管理** | Windows 证书存储 | K8s Secrets / Azure Key Vault | 等效 |
| **WAF** | 无 | Azure Application Gateway (AGIC) | CloudSOA 可选增强 |

### 迁移安全配置检查清单

- [ ] 将 `Username/Password` 替换为 `BearerToken` 或 `ApiKey`
- [ ] 将 `headNode` 地址从内网改为 CloudSOA Broker 的 HTTPS 端点
- [ ] 如使用客户端证书, 转换为 PFX 格式并配置 `ClientCertificate`
- [ ] 确认 `AcceptUntrustedCertificates = false` (生产环境)
- [ ] 如使用 Azure AD, 注册应用并配置应用角色
- [ ] 更新防火墙规则, 允许访问 AKS 负载均衡器 IP

---

## 10. 安全加固清单

### 10.1 部署前 (必须)

- [ ] **TLS**: 配置 `Tls:Mode=direct` 或 `Tls:Mode=ingress`, 禁止 `none`
- [ ] **认证**: 设置 `Authentication:Mode=jwt` 或 `apikey`, 禁止 `none`
- [ ] **API Key 强度**: 至少 32 字节随机值 (64 个十六进制字符)
- [ ] **JWT 签名密钥**: 至少 256 位 (32 字节)
- [ ] **网络策略**: 部署 `network-policies.yaml`
- [ ] **密钥存储**: 使用 K8s Secrets (最低要求) 或 Azure Key Vault (推荐)
- [ ] **镜像**: 使用指定版本 tag (如 `v1.6.0`), 禁止使用 `latest`

### 10.2 部署后 (验证)

- [ ] **HTTPS 测试**: `curl -v https://broker-endpoint/healthz` 确认 TLS 握手
- [ ] **401 测试**: 无凭据访问 API 应返回 401
- [ ] **403 测试**: Reader 角色尝试管理操作应返回 403
- [ ] **审计日志**: 执行变更操作后检查 `[AUDIT]` 日志
- [ ] **网络隔离**: 从 ServiceHost Pod 尝试访问 Redis 应超时
- [ ] **证书有效期**: 确认 TLS 证书未过期且自动轮转已配置

### 10.3 持续运维

- [ ] **证书轮转**: cert-manager 自动续期, 或设置告警 (过期前 30 天)
- [ ] **API Key 轮转**: 建议每 90 天轮转一次
- [ ] **安全扫描**: 定期运行容器镜像漏洞扫描 (Trivy/Defender)
- [ ] **日志保留**: Azure Monitor 日志保留 ≥ 90 天 (合规要求)
- [ ] **访问审计**: 每月审查 RBAC 角色分配
- [ ] **Kubernetes 升级**: 跟随 AKS 安全补丁, 保持集群版本更新

### 10.4 已知限制与后续规划

| 项目 | 当前状态 | 计划 |
|------|---------|------|
| Azure AD OIDC 发现 | 签名验证跳过 (待实现) | 接入 OIDC 发现端点自动获取签名密钥 |
| Azure Key Vault 集成 | 手动 K8s Secret | CSI Driver 自动注入 |
| 速率限制 (Rate Limiting) | 未实现 | 计划 v1.7.0 |
| CORS 策略 | 未限制 | 计划按需配置 |
| 私有端点 | 未实现 | Azure Private Link (Redis/Cosmos) |
| 数据加密 (at rest) | 依赖 Azure 磁盘加密 | 应用层加密待评估 |

---

> **文档版本**: v1.6.0  
> **安全框架版本**: v1.2.0 (TLS) → v1.3.0 (Auth) → v1.4.0 (RBAC) → v1.5.0 (Audit) → v1.6.0 (Network)  
> **测试环境**: Azure AKS (xxin-cloudsoa-aks), Calico 网络策略, Broker v1.6.0
