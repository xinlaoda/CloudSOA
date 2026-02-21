# CloudSOA Enterprise Security Configuration & Deployment Guide

> **Version**: v1.6.0 | **Last Updated**: 2026-02-21 | **Target Audience**: Enterprise environments (finance, government, healthcare) with strict security requirements

---

## Table of Contents

1. [Security Architecture Overview](#1-security-architecture-overview)
2. [Transport Security (TLS/HTTPS)](#2-transport-security-tlshttps)
3. [Authentication](#3-authentication)
4. [Role-Based Access Control (RBAC)](#4-role-based-access-control-rbac)
5. [Audit Logging](#5-audit-logging)
6. [Network Isolation (Network Policies)](#6-network-isolation-network-policies)
7. [Client Security Configuration](#7-client-security-configuration)
8. [Deployment Patterns & Best Practices](#8-deployment-patterns--best-practices)
9. [HPC Pack SOA Security Comparison](#9-hpc-pack-soa-security-comparison)
10. [Security Hardening Checklist](#10-security-hardening-checklist)

---

## 1. Security Architecture Overview

CloudSOA employs a **Defense-in-Depth** strategy with five security layers:

```
┌─────────────────────────────────────────────────────────────────────┐
│  External Clients (.NET 8 / .NET Fx 4.8)                            │
│  ┌───────────────────────────────────────────────────────────────┐  │
│  │  ① Transport Security — TLS 1.2/1.3 encrypted communication  │  │
│  │  ② Authentication — JWT Bearer / API Key / Azure AD           │  │
│  │  ③ Authorization — RBAC (Admin / User / Reader)               │  │
│  └──────────────────┬────────────────────────────────────────────┘  │
│                     │ HTTPS / gRPC+TLS                              │
│  ┌──────────────────▼────────────────────────────────────────────┐  │
│  │  Broker (API Gateway)                                         │  │
│  │  ├─ AuthenticationMiddleware   (JWT validation / API Key)     │  │
│  │  ├─ AuthorizationMiddleware    (RBAC role enforcement)        │  │
│  │  ├─ AuditLoggingMiddleware     (④ Audit logging)              │  │
│  │  └─ Business Logic (Session/Request/Response)                 │  │
│  └──────────────────┬────────────────────────────────────────────┘  │
│                     │ gRPC (internal)                                │
│  ┌──────────────────▼────────────────────────────────────────────┐  │
│  │  ⑤ K8s NetworkPolicy Network Isolation                        │  │
│  │  ├─ ServiceHost: only Broker allowed (gRPC :5010)             │  │
│  │  ├─ Redis: only Broker allowed (:6379)                        │  │
│  │  ├─ ServiceManager: only Portal + Broker (:5020)              │  │
│  │  └─ Default deny all ingress (default-deny-ingress)           │  │
│  └───────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────┘
```

### Security Components by Version

| Security Layer | Component | Introduced | Configuration |
|----------------|-----------|-----------|---------------|
| Transport Security | Kestrel TLS / Nginx Ingress | v1.2.0 | `Tls:Mode` |
| Authentication | AuthenticationMiddleware | v1.3.0 | `Authentication:Mode` |
| Authorization | AuthorizationMiddleware | v1.4.0 | (linked to authentication) |
| Audit Logging | AuditLoggingMiddleware | v1.5.0 | (always enabled) |
| Network Isolation | K8s NetworkPolicy + Calico | v1.6.0 | `network-policies.yaml` |

---

## 2. Transport Security (TLS/HTTPS)

### 2.1 Three TLS Deployment Modes

CloudSOA supports three TLS termination strategies to suit different enterprise scenarios:

| Mode | Config Value | TLS Termination Point | Use Case | Cost |
|------|-------------|----------------------|----------|------|
| **Direct** | `Tls:Mode=direct` | Kestrel (Broker process) | End-to-end encryption, no Ingress | Free |
| **Ingress** | `Tls:Mode=ingress` | Nginx Ingress Controller | Standard enterprise deployment | Free |
| **AGIC** | `Tls:Mode=ingress` | Azure Application Gateway | Enterprise WAF + TLS offloading | ~$200+/mo |
| **None** | `Tls:Mode=none` | No encryption | Development/testing only | — |

### 2.2 Mode 1: Direct (Kestrel Built-in TLS)

Broker embeds TLS natively. Clients establish end-to-end encrypted connections without any external components.

**Configuration (appsettings.json or environment variables):**

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

**Equivalent environment variables:**

```bash
Tls__Mode=direct
Tls__CertPath=/certs/server.pfx
Tls__CertPassword=your-cert-password
```

**Port Mapping:**

| Port | Protocol | Purpose |
|------|----------|---------|
| 5000 | HTTP | Health checks + Prometheus metrics (auto 308 redirect) |
| 5443 | HTTPS | REST API (client access) |
| 5001 | HTTP/2 | gRPC (internal communication) |
| 5444 | gRPC+TLS | gRPC encrypted communication |

**Automatic HTTP Redirect:** Non-health/metrics requests on HTTP `:5000` are automatically redirected (308 Permanent Redirect) to `https://<host>:5443`.

**Generate Self-Signed Certificate (testing only):**

```bash
# Generate PFX certificate using OpenSSL
openssl req -x509 -newkey rsa:4096 -keyout key.pem -out cert.pem -days 365 -nodes \
  -subj "/CN=cloudsoa-broker"
openssl pkcs12 -export -out server.pfx -inkey key.pem -in cert.pem -password pass:MyPassword

# Create K8s Secret
kubectl create secret generic broker-tls-cert \
  --from-file=server.pfx=server.pfx \
  -n cloudsoa
```

**Mount Certificate in Deployment:**

```yaml
# Add to broker-deployment.yaml
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

### 2.3 Mode 2: Nginx Ingress Controller

TLS terminates at the Nginx Ingress layer; Broker runs plain HTTP internally. Suitable for standard Kubernetes deployments.

**Installation Steps:**

```bash
# 1. Install Nginx Ingress Controller
helm repo add ingress-nginx https://kubernetes.github.io/ingress-nginx
helm install ingress-nginx ingress-nginx/ingress-nginx \
  --namespace ingress-nginx --create-namespace \
  --set controller.service.annotations."service\.beta\.kubernetes\.io/azure-load-balancer-health-probe-request-path"=/healthz

# 2. (Optional) Install cert-manager for automated Let's Encrypt certificates
helm repo add jetstack https://charts.jetstack.io
helm install cert-manager jetstack/cert-manager \
  --namespace cert-manager --create-namespace \
  --set crds.enabled=true

# 3. Create TLS Secret (choose one)
# Option A: Manually upload enterprise CA-signed certificate
kubectl create secret tls cloudsoa-tls \
  --cert=tls.crt --key=tls.key -n cloudsoa

# Option B: Use cert-manager + Let's Encrypt (auto-issuance)
# Uncomment the ClusterIssuer section in broker-ingress-nginx.yaml

# 4. Deploy Ingress rules
kubectl apply -f deploy/k8s/broker-ingress-nginx.yaml
```

**Key Ingress Annotations (`broker-ingress-nginx.yaml`):**

```yaml
annotations:
  nginx.ingress.kubernetes.io/ssl-redirect: "true"          # Force HTTPS
  nginx.ingress.kubernetes.io/proxy-read-timeout: "300"     # Long-running SOA operations
  nginx.ingress.kubernetes.io/proxy-send-timeout: "300"
  nginx.ingress.kubernetes.io/proxy-body-size: "100m"       # DLL upload size limit
spec:
  tls:
    - hosts:
        - soa.yourcompany.com          # Replace with your domain
      secretName: cloudsoa-tls
```

### 2.4 Mode 3: Azure Application Gateway (AGIC)

For environments requiring Web Application Firewall (WAF) and DDoS protection:

```bash
# Enable AGIC add-on
az aks enable-addons -n xxin-cloudsoa-aks -g xxin-cloudsoa-rg \
  --addons ingress-appgw --appgw-subnet-cidr 10.225.0.0/16

# Deploy Ingress (using appgw class in broker-ingress.yaml)
kubectl apply -f deploy/k8s/broker-ingress.yaml
```

### 2.5 Client TLS Configuration

**Mutual TLS (mTLS):**

Clients can present certificates for two-way authentication in zero-trust environments:

```csharp
// .NET 8 Client
var info = new SessionStartInfo("https://soa.yourcompany.com", "MyService")
{
    ClientCertificate = new X509Certificate2("client.pfx", "password"),
    AcceptUntrustedCertificates = false  // Must be false in production
};

// .NET Framework 4.8 Client
var info = new SessionStartInfo("https://soa.yourcompany.com", "MyService")
{
    ClientCertificate = new X509Certificate2("client.pfx", "password")
};
```

---

## 3. Authentication

### 3.1 Authentication Modes Overview

| Mode | Config Value | Use Case | Security Level |
|------|-------------|----------|----------------|
| **JWT Bearer** | `Authentication:Mode=jwt` | Azure AD / enterprise SSO / custom tokens | ★★★★★ |
| **API Key** | `Authentication:Mode=apikey` | Service-to-service / automation scripts | ★★★☆☆ |
| **Anonymous** | `Authentication:Mode=none` | Development/testing | ★☆☆☆☆ |

> ⚠️ **Production environments must use `jwt` or `apikey` mode.** The `none` mode skips all authentication checks.

### 3.2 JWT Bearer Authentication

Suitable for enterprise SSO integration, Azure Active Directory, and custom identity providers.

**Broker Configuration:**

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

**Equivalent K8s Environment Variables:**

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

#### 3.2.1 Azure AD Integration

Prerequisites: Requires Azure AD Application Administrator or Global Administrator permissions.

**Steps:**

1. **Register Azure AD Application:**
   ```bash
   # Register the API application (Broker)
   az ad app create --display-name "CloudSOA Broker" \
     --identifier-uris "api://cloudsoa-broker"
   
   # Create application roles
   az ad app update --id <app-id> --app-roles '[
     {"displayName":"SOA Admin","value":"Admin","allowedMemberTypes":["User","Application"]},
     {"displayName":"SOA User","value":"User","allowedMemberTypes":["User","Application"]},
     {"displayName":"SOA Reader","value":"Reader","allowedMemberTypes":["User","Application"]}
   ]'
   ```

2. **Configure Broker:**
   ```
   Authentication__Mode=jwt
   Authentication__Jwt__Issuer=https://login.microsoftonline.com/{tenant-id}/v2.0
   Authentication__Jwt__Audience=api://cloudsoa-broker
   # Leave SigningKey empty — Azure AD uses OIDC discovery for signing key retrieval
   ```

3. **Client Token Acquisition:**
   ```csharp
   // Use MSAL to acquire Azure AD token
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

#### 3.2.2 Custom JWT (Non-Azure AD)

Use HMAC-SHA256 symmetric key signing for private environments:

```
Authentication__Mode=jwt
Authentication__Jwt__Issuer=cloudsoa-issuer
Authentication__Jwt__Audience=cloudsoa-broker
Authentication__Jwt__SigningKey=YourSuperSecretKeyAtLeast256BitsLong!
```

**Required JWT Payload:**

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

> Supported role claim fields:
> - `http://schemas.microsoft.com/ws/2008/06/identity/claims/role` (WCF/Azure AD standard)
> - `roles` (Azure AD v2.0 application roles)

#### 3.2.3 JWT + API Key Dual Authentication

In `jwt` mode, API Key authentication is automatically enabled as a **fallback mechanism**. The authentication order is:

1. First check for `Authorization: Bearer <token>` header
2. If no Bearer token, check for `X-Api-Key: <key>` header
3. If neither is present, return `401 Unauthorized`

This allows automation scripts to use API Keys while users authenticate via SSO/JWT.

### 3.3 API Key Authentication

Suitable for service-to-service calls, CI/CD pipelines, and automation scripts.

**Broker Configuration:**

```yaml
Authentication__Mode=apikey
Authentication__ApiKey=your-strong-random-api-key-here
```

**Security Features:**
- Uses `CryptographicOperations.FixedTimeEquals` for **constant-time comparison** to prevent timing attacks
- API Key users are automatically assigned the `Admin` role
- Recommended: use 64+ character random keys

**Generate Strong Keys:**

```bash
# Linux/macOS
openssl rand -hex 32

# PowerShell
-join ((1..64) | ForEach-Object { '{0:x}' -f (Get-Random -Maximum 16) })
```

### 3.4 Public Endpoints (No Authentication Required)

The following endpoints are always accessible, regardless of authentication mode:

| Endpoint | Purpose |
|----------|---------|
| `/healthz` | Kubernetes health check probes |
| `/metrics` | Prometheus metrics scraping |
| `/` | Service identification (returns service name) |

### 3.5 Username/Password Authentication (Not Supported)

> ⚠️ **CloudSOA does not support Username/Password authentication.**

HPC Pack SOA uses Windows Integrated Authentication (NTLM/Kerberos) with Active Directory. CloudSOA runs on Linux containers in a cloud-native environment and does not support Windows domain authentication.

For API compatibility, the `SessionStartInfo.Username` and `SessionStartInfo.Password` properties are **preserved** but setting a non-empty value at runtime will immediately throw a `NotSupportedException`:

```csharp
// ❌ Throws NotSupportedException at runtime
var info = new SessionStartInfo("https://broker", "MyService")
{
    Username = "domain\\user",   // Will cause failure
    Password = "password"
};

// ✅ Correct migration approach
var info = new SessionStartInfo("https://broker", "MyService")
{
    BearerToken = "your-jwt-token"  // Or: ApiKey = "your-api-key"
};
```

---

## 4. Role-Based Access Control (RBAC)

### 4.1 Role Hierarchy

CloudSOA uses a three-tier role system where **higher roles automatically inherit all permissions from lower roles**:

```
Admin ──→ User ──→ Reader
  │         │         │
  │         │         └─ Read-only (list, query, status)
  │         └─ Session operations (create, close, submit requests)
  └─ Administrative operations (service register, deploy, delete, scale)
```

### 4.2 Endpoint-to-Role Mapping

| API Endpoint | HTTP Method | Minimum Role | Description |
|-------------|-------------|-------------|-------------|
| `GET /api/v1/sessions` | GET | Reader | List all sessions |
| `POST /api/v1/sessions` | POST | User | Create session |
| `DELETE /api/v1/sessions/{id}` | DELETE | User | Close session |
| `POST /api/v1/sessions/{id}/requests` | POST | User | Submit compute request |
| `GET /api/v1/sessions/{id}/results` | GET | Reader | Retrieve compute results |
| `GET /api/v1/sessions/{id}/status` | GET | Reader | Query session status |
| `GET /api/v1/services` | GET | Reader | List services |
| `POST /api/v1/services` | POST | Admin | Register new service |
| `POST /api/v1/services/{name}/deploy` | POST | Admin | Deploy service |
| `POST /api/v1/services/{name}/stop` | POST | Admin | Stop service |
| `POST /api/v1/services/{name}/scale` | POST | Admin | Scale service |
| `DELETE /api/v1/services/{name}` | DELETE | Admin | Delete service |
| `PUT /api/v1/services/{name}` | PUT | Admin | Update service |
| `GET /api/v1/metrics` | GET | Reader | Cluster metrics |
| Other `/api/` paths | Any | User | Default minimum role |

### 4.3 Role Source by Authentication Method

| Auth Method | Role Assignment |
|-------------|----------------|
| JWT Bearer | Extracted from `ClaimTypes.Role` or `roles` claim in token |
| API Key | Always assigned `Admin` (API Key represents an administrator) |
| Anonymous (none) | Authorization check skipped (development mode) |

### 4.4 Azure AD Application Roles

After configuring application roles in Azure AD, assign users/groups to the corresponding roles. The JWT token will automatically include the `roles` claim:

```json
{
  "roles": ["Admin"],
  "preferred_username": "admin@company.com"
}
```

### 4.5 Authorization Failure Responses

| Status Code | Meaning | Example |
|-------------|---------|---------|
| `401 Unauthorized` | Not authenticated or invalid token | Missing/expired Bearer token |
| `403 Forbidden` | Authenticated but insufficient permissions | Reader role attempting session creation |

**403 Response Body:**
```
Forbidden: requires Admin role.
```

**Broker Log Entry:**
```
warn: CloudSOA.Broker.Middleware.AuthorizationMiddleware
  Forbidden: user=john.doe roles=[Reader] required=Admin path=/api/v1/services
```

---

## 5. Audit Logging

### 5.1 Audit Policy

CloudSOA logs all security-relevant events to meet financial industry compliance requirements (SOC 2, PCI-DSS, ISO 27001):

| Event Type | HTTP Methods | Log Level | Trigger Condition |
|-----------|-------------|-----------|-------------------|
| Mutating operations | POST/PUT/PATCH/DELETE | `Information` | Always logged |
| Failed read attempts | GET (4xx/5xx) | `Warning` | Logged on error responses |
| Successful reads | GET (2xx) | — | Not logged (noise reduction) |
| Non-API paths | Any | — | Not logged |

### 5.2 Log Format

```
[AUDIT] {Method} {Path} by {Identity} → {StatusCode} ({Duration}ms)
```

**Sample Output:**

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

### 5.3 Identity Format Rules

| Auth State | Identity Format | Example |
|-----------|----------------|---------|
| JWT authenticated | `{name}[{roles}]` | `john.doe[Admin]` |
| API Key authenticated | `apikey-user[Admin]` | `apikey-user[Admin]` |
| Unauthenticated (none) | `anonymous` | `anonymous` |

### 5.4 Log Collection & Monitoring

Audit logs are written to **stdout**, captured by the AKS container runtime, and automatically flow into Azure Monitor:

```bash
# View real-time audit logs
kubectl logs -f -l app=broker -n cloudsoa | grep "\[AUDIT\]"

# Azure Monitor Query (KQL)
ContainerLog
| where LogEntry contains "[AUDIT]"
| parse LogEntry with * "[AUDIT] " Method " " Path " by " Identity " → " StatusCode " (" Duration "ms)"
| project TimeGenerated, Method, Path, Identity, StatusCode, Duration
| order by TimeGenerated desc
```

**Recommended Alert Rules:**

| Alert | Condition | Severity |
|-------|-----------|----------|
| Authentication failure spike | >50 × 401 within 5 minutes | High |
| Authorization failure | Any 403 event | Medium |
| Service registration/deletion | Any service lifecycle change | Informational |
| Abnormally high latency | Duration > 30000ms | Medium |

---

## 6. Network Isolation (Network Policies)

### 6.1 Zero-Trust Network Architecture

CloudSOA uses Kubernetes NetworkPolicy for **microsegmentation**:

```
                     ┌──────────────────────┐
                     │  External Clients     │
                     └──────┬───────────────┘
                            │ :5000/:5443 (REST)
                            │ :5001/:5444 (gRPC)
                     ┌──────▼───────────────┐
               ┌─────│    Broker             │─────┐
               │     └──────────────────────┘     │
               │ :5010 (gRPC)              :6379  │
        ┌──────▼───────────────┐   ┌──────▼──────┐
        │  ServiceHost (svc)   │   │   Redis      │
        │  svc-calculator      │   └─────────────┘
        │  svc-heavycompute    │
        └──────────────────────┘
                            │
               ┌────────────┘  :5020
        ┌──────▼───────────────┐    ┌────────────────┐
        │  ServiceManager      │◄───│  Portal (:5030) │◄── Admins
        └──────────────────────┘    └────────────────┘
```

**Traffic Matrix (✅ Allowed / ❌ Denied):**

| Source → Target | Broker | ServiceHost | ServiceManager | Portal | Redis |
|----------------|--------|-------------|---------------|--------|-------|
| **External** | ✅ | ❌ | ❌ | ✅ | ❌ |
| **Broker** | — | ✅ :5010 | ✅ :5020 | ❌ | ✅ :6379 |
| **Portal** | ✅ :5000 | ❌ | ✅ :5020 | — | ❌ |
| **ServiceHost** | ❌ | ❌ | ❌ | ❌ | ❌ |

### 6.2 Deploying Network Policies

```bash
# Prerequisite: AKS cluster must have a network policy engine (Azure/Calico)
az aks show -g <rg> -n <aks> --query "networkProfile.networkPolicy"
# Should return "calico" or "azure"

# Deploy all policies
kubectl apply -f deploy/k8s/network-policies.yaml

# Verify
kubectl get networkpolicies -n cloudsoa
```

**Included Policies:**

| Policy Name | Protected Target | Allowed Sources |
|------------|-----------------|-----------------|
| `default-deny-ingress` | All pods in namespace | (none — deny all by default) |
| `allow-broker-ingress` | Broker | Any source (LoadBalancer) + Portal |
| `allow-servicehost-ingress` | All service pods | Broker only (:5010) |
| `allow-servicemanager-ingress` | ServiceManager | Portal + Broker only (:5020) |
| `allow-portal-ingress` | Portal | Any source (:5030) |
| `allow-redis-ingress` | Redis | Broker only (:6379) |

### 6.3 Enabling Calico on a New AKS Cluster

```bash
az aks create -g <rg> -n <aks> \
  --network-plugin azure \
  --network-policy calico \
  --node-count 3
```

---

## 7. Client Security Configuration

### 7.1 .NET 8 Client (CloudSOA.Client)

```csharp
using CloudSOA.Client;

// === Scenario 1: JWT Bearer Authentication ===
var info = new SessionStartInfo("https://soa.yourcompany.com", "CalculatorService")
{
    BearerToken = "eyJhbGciOiJIUzI1NiIs...",  // JWT token
    Secure = true
};

// === Scenario 2: API Key Authentication ===
var info = new SessionStartInfo("https://soa.yourcompany.com", "CalculatorService")
{
    ApiKey = "your-api-key-here"
};

// === Scenario 3: Mutual TLS (mTLS) + JWT ===
var info = new SessionStartInfo("https://soa.yourcompany.com", "CalculatorService")
{
    BearerToken = token,
    ClientCertificate = new X509Certificate2("client.pfx", "password"),
    AcceptUntrustedCertificates = false  // Must be false in production
};

// === Scenario 4: Custom Headers (Advanced) ===
var info = new SessionStartInfo("https://soa.yourcompany.com", "CalculatorService");
info.Properties["X-Correlation-Id"] = Guid.NewGuid().ToString();
info.Properties["X-Tenant-Id"] = "tenant-123";

// Create session and use
using var session = await CloudSession.CreateSessionAsync(info);
using var client = new BrokerClient<ICalculator>(session);
client.SendRequest(new CalculateRequest { A = 100, B = 200 });
var responses = client.GetResponses<CalculateResult>();
```

### 7.2 .NET Framework 4.8 Client (CloudSOA.Client.NetFx)

For HPC Pack SOA migration scenarios, with API-compatible code:

```csharp
using CloudSOA.Client;

// HPC Pack migration: replace headNode with CloudSOA broker endpoint
var info = new SessionStartInfo("https://soa.yourcompany.com", "CalculatorService")
{
    // Old code (not supported, remove):
    // Username = "domain\\user",
    // Password = "password",
    
    // New code:
    BearerToken = "your-jwt-token",        // Or: ApiKey = "your-key"
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

**TLS Security Behavior (.NET Framework):**
- Automatically sets `ServicePointManager.SecurityProtocol = Tls12 | Tls13`
- Rejects TLS 1.0 / TLS 1.1 insecure protocols

### 7.3 Client Security Properties Reference

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `BearerToken` | string? | null | JWT Bearer token; automatically adds `Authorization: Bearer` header |
| `ApiKey` | string? | null | API Key; automatically adds `X-Api-Key` header |
| `ClientCertificate` | X509Certificate2? | null | Client certificate for mutual TLS |
| `AcceptUntrustedCertificates` | bool | false | Accept self-signed certificates (development only) |
| `Secure` | bool | true | HPC Pack compatibility field |
| `Username` | string? | null | **Not supported** — throws NotSupportedException at runtime |
| `Password` | string? | null | **Not supported** — throws NotSupportedException at runtime |
| `Properties` | Dictionary | (empty) | Custom HTTP headers (key-value pairs) |

---

## 8. Deployment Patterns & Best Practices

### 8.1 Production Deployment Configuration

Recommended enterprise-grade production configuration:

```yaml
# broker-configmap-production.yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: broker-config
  namespace: cloudsoa
data:
  # TLS (terminated at Ingress)
  Tls__Mode: "ingress"
  
  # Authentication (JWT + API Key dual)
  Authentication__Mode: "jwt"
  Authentication__Jwt__Issuer: "https://login.microsoftonline.com/{tenant-id}/v2.0"
  Authentication__Jwt__Audience: "api://cloudsoa-broker"
  
  # Redis (use Azure Redis Cache)
  ConnectionStrings__Redis: "your-redis.redis.cache.windows.net:6380,password=...,ssl=True,abortConnect=False"
  
  # Log level
  Logging__LogLevel__Default: "Information"
```

```yaml
# broker-secrets.yaml (use kubectl create secret, never commit plaintext)
apiVersion: v1
kind: Secret
metadata:
  name: broker-secrets
  namespace: cloudsoa
type: Opaque
data:
  api-key: <base64-encoded-api-key>
  jwt-signing-key: <base64-encoded-signing-key>   # Only for custom JWT
```

```bash
# Create Secret (recommended approach, avoids plaintext in YAML)
kubectl create secret generic broker-secrets \
  --from-literal=api-key="$(openssl rand -hex 32)" \
  --from-literal=jwt-signing-key="YourSigningKeyAtLeast256BitsLong!" \
  -n cloudsoa
```

### 8.2 Deployment Environment Matrix

| Environment | TLS Mode | Auth Mode | Network Policies | Audit Logging | Replicas |
|------------|---------|-----------|-----------------|---------------|----------|
| **Development** | none | none | Not deployed | ✅ (default) | 1 |
| **Testing** | direct (self-signed) | apikey | Deployed | ✅ | 1–2 |
| **Staging** | ingress (enterprise CA) | jwt | Deployed | ✅ | 2–3 |
| **Production** | ingress (enterprise CA) | jwt + apikey | Deployed | ✅ | 3+ (HPA) |

### 8.3 One-Click Deployment Script

```bash
#!/bin/bash
# deploy-secure.sh — Secure production deployment

RESOURCE_GROUP="your-rg"
AKS_NAME="your-aks"
ACR_NAME="your-acr"
NAMESPACE="cloudsoa"
DOMAIN="soa.yourcompany.com"

# 1. Connect to AKS
az aks get-credentials -g $RESOURCE_GROUP -n $AKS_NAME

# 2. Create namespace
kubectl create namespace $NAMESPACE --dry-run=client -o yaml | kubectl apply -f -

# 3. Deploy TLS certificate
kubectl create secret tls cloudsoa-tls \
  --cert=certs/server.crt --key=certs/server.key \
  -n $NAMESPACE

# 4. Create authentication secrets
kubectl create secret generic broker-secrets \
  --from-literal=api-key="$(openssl rand -hex 32)" \
  -n $NAMESPACE

# 5. Deploy infrastructure
kubectl apply -f deploy/k8s/namespace.yaml
kubectl apply -f deploy/k8s/redis.yaml
kubectl apply -f deploy/k8s/broker-configmap.yaml

# 6. Deploy network policies
kubectl apply -f deploy/k8s/network-policies.yaml

# 7. Deploy services
kubectl apply -f deploy/k8s/broker-deployment.yaml
kubectl apply -f deploy/k8s/servicemanager-deployment.yaml
kubectl apply -f deploy/k8s/portal-deployment.yaml

# 8. Deploy Ingress (choose one)
# Option A: Nginx Ingress
kubectl apply -f deploy/k8s/broker-ingress-nginx.yaml
# Option B: AGIC
# kubectl apply -f deploy/k8s/broker-ingress.yaml

# 9. Verify
kubectl get pods -n $NAMESPACE
kubectl get svc -n $NAMESPACE
kubectl get networkpolicies -n $NAMESPACE
echo "Broker: $(kubectl get svc broker-service -n $NAMESPACE -o jsonpath='{.status.loadBalancer.ingress[0].ip}')"
```

### 8.4 Secrets Management Recommendations

| Secret Type | Storage Location | Recommended Approach |
|------------|-----------------|---------------------|
| API Key | K8s Secret | Azure Key Vault + CSI Driver |
| JWT Signing Key | K8s Secret | Azure Key Vault + CSI Driver |
| TLS Certificate | K8s TLS Secret | cert-manager + Let's Encrypt or enterprise CA |
| Redis Password | K8s Secret | Azure Key Vault + CSI Driver |
| Blob/Cosmos Connection String | K8s Secret | Azure Managed Identity |

**Azure Key Vault Integration (Recommended):**

```bash
# Install CSI Driver
az aks enable-addons -g <rg> -n <aks> --addons azure-keyvault-secrets-provider

# Create Key Vault
az keyvault create -n cloudsoa-kv -g <rg>

# Store secrets
az keyvault secret set --vault-name cloudsoa-kv --name api-key --value "$(openssl rand -hex 32)"
az keyvault secret set --vault-name cloudsoa-kv --name jwt-signing-key --value "YourKey..."
```

---

## 9. HPC Pack SOA Security Comparison

| Security Capability | HPC Pack SOA | CloudSOA | Notes |
|--------------------|-------------|----------|-------|
| **Transport Encryption** | WCF Transport Security (HTTPS/NetTcp) | TLS 1.2/1.3 (Kestrel/Ingress) | Equivalent |
| **Message Encryption** | WCF Message Security | — | CloudSOA relies on transport-layer encryption |
| **Windows Domain Auth** | NTLM / Kerberos | ❌ Not supported | Cloud-native environment has no AD domain |
| **Azure AD** | Azure AD (HPC Pack 2019+) | ✅ JWT Bearer | Fully supported |
| **Certificate Auth** | X.509 client certificates | ✅ mTLS | Equivalent |
| **Username/Password** | ✅ (AD-validated) | ❌ Property preserved, runtime rejection | Migration requires JWT/ApiKey |
| **RBAC** | HPC Job/Task permissions | Admin/User/Reader (3 tiers) | More granular |
| **Audit Logging** | Windows Event Log | Structured logs → Azure Monitor | Easier SIEM integration |
| **Network Isolation** | Windows Firewall | K8s NetworkPolicy (Calico) | Finer microsegmentation |
| **Secrets Management** | Windows Certificate Store | K8s Secrets / Azure Key Vault | Equivalent |
| **WAF** | None | Azure Application Gateway (AGIC) | CloudSOA optional enhancement |

### Migration Security Checklist

- [ ] Replace `Username/Password` with `BearerToken` or `ApiKey`
- [ ] Change `headNode` address from intranet to CloudSOA Broker's HTTPS endpoint
- [ ] If using client certificates, convert to PFX format and configure `ClientCertificate`
- [ ] Confirm `AcceptUntrustedCertificates = false` in production
- [ ] If using Azure AD, register the application and configure application roles
- [ ] Update firewall rules to allow access to the AKS Load Balancer IP

---

## 10. Security Hardening Checklist

### 10.1 Pre-Deployment (Required)

- [ ] **TLS**: Set `Tls:Mode=direct` or `Tls:Mode=ingress`; never use `none`
- [ ] **Authentication**: Set `Authentication:Mode=jwt` or `apikey`; never use `none`
- [ ] **API Key Strength**: Minimum 32-byte random value (64 hexadecimal characters)
- [ ] **JWT Signing Key**: Minimum 256 bits (32 bytes)
- [ ] **Network Policies**: Deploy `network-policies.yaml`
- [ ] **Secrets Storage**: Use K8s Secrets (minimum) or Azure Key Vault (recommended)
- [ ] **Image Tags**: Use specific version tags (e.g., `v1.6.0`); never use `latest`

### 10.2 Post-Deployment (Verification)

- [ ] **HTTPS Test**: `curl -v https://broker-endpoint/healthz` to confirm TLS handshake
- [ ] **401 Test**: Access API without credentials; should return 401
- [ ] **403 Test**: Reader role attempting admin operations; should return 403
- [ ] **Audit Logs**: Perform a mutating operation and verify `[AUDIT]` log entry
- [ ] **Network Isolation**: From a ServiceHost pod, attempt to access Redis; should timeout
- [ ] **Certificate Expiry**: Confirm TLS certificate is valid and auto-rotation is configured

### 10.3 Ongoing Operations

- [ ] **Certificate Rotation**: cert-manager auto-renewal, or set alert (30 days before expiry)
- [ ] **API Key Rotation**: Recommended every 90 days
- [ ] **Security Scanning**: Regularly run container image vulnerability scans (Trivy/Defender)
- [ ] **Log Retention**: Azure Monitor log retention ≥ 90 days (compliance requirement)
- [ ] **Access Audit**: Monthly review of RBAC role assignments
- [ ] **Kubernetes Upgrades**: Follow AKS security patches; keep cluster version current

### 10.4 Known Limitations & Future Roadmap

| Item | Current Status | Planned |
|------|---------------|---------|
| Azure AD OIDC Discovery | Signature validation bypassed (pending) | Integrate OIDC discovery for automatic signing key retrieval |
| Azure Key Vault Integration | Manual K8s Secrets | CSI Driver automatic injection |
| Rate Limiting | Not implemented | Planned for v1.7.0 |
| CORS Policy | Unrestricted | Configure as needed |
| Private Endpoints | Not implemented | Azure Private Link (Redis/Cosmos) |
| Encryption at Rest | Relies on Azure disk encryption | Application-layer encryption under evaluation |

---

> **Document Version**: v1.6.0  
> **Security Framework Versions**: v1.2.0 (TLS) → v1.3.0 (Auth) → v1.4.0 (RBAC) → v1.5.0 (Audit) → v1.6.0 (Network)  
> **Test Environment**: Azure AKS (xxin-cloudsoa-aks), Calico network policy, Broker v1.6.0
