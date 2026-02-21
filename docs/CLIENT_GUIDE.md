# CloudSOA End-to-End Client Guide

A complete guide to migrating HPC Pack SOA services and clients to CloudSOA, from building service DLLs to sending your first request.

## Table of Contents

- [1. Overview](#1-overview)
- [2. Prerequisites](#2-prerequisites)
- [3. Getting the CloudSOA Client SDK](#3-getting-the-cloudsoa-client-sdk)
- [4. Getting the Broker Endpoint URL](#4-getting-the-broker-endpoint-url)
- [5. Building and Uploading a Service DLL](#5-building-and-uploading-a-service-dll)
- [6. Deploying the Service](#6-deploying-the-service)
- [7. Migrating HPC Pack Client Code](#7-migrating-hpc-pack-client-code)
- [8. Writing a New Client from Scratch](#8-writing-a-new-client-from-scratch)
- [9. Message Contracts Reference](#9-message-contracts-reference)
- [10. Running and Testing](#10-running-and-testing)
- [11. Concurrency and Performance](#11-concurrency-and-performance)
- [12. Troubleshooting](#12-troubleshooting)
- [13. API Reference](#13-api-reference)

---

## 1. Overview

CloudSOA is a drop-in replacement for Microsoft HPC Pack SOA. It provides **two paths** for SOA service development:

| Path | Service DLL | Client Library | Runtime |
|------|------------|----------------|---------|
| **Migration** | Existing .NET Framework 4.8 WCF DLL (no changes) | `CloudSOA.Client.NetFx` (net48) | `windows-netfx48` |
| **New Development** | New .NET 8 + CoreWCF DLL | `CloudSOA.Client` (net8.0) | `linux-corewcf` / `linux-net8` |

Migrating an existing HPC Pack SOA application involves three steps:

1. **Service DLL** — Upload your existing WCF `[ServiceContract]` DLL as-is (no code changes, no recompilation)
2. **Client code** — Change one `using` line and the broker endpoint URL (client stays on .NET Framework 4.8)
3. **Connect** — Run the client against the CloudSOA Broker endpoint

```
┌──────────────────────────────────────────┐
│  Your Client Application                 │
│  using CloudSOA.Client;                  │  ← only change from HPC Pack
│  Broker URL: http://...                  │  ← new endpoint
│                                          │
│  .NET Framework 4.8 (CloudSOA.Client.NetFx)  ← legacy clients stay on .NET Fx
│  .NET 8              (CloudSOA.Client)        ← new clients use .NET 8
└──────────┬───────────────────────────────┘
           │ REST (HTTP)
           ▼
┌──────────────────────────────────────────┐
│  CloudSOA Broker (AKS)                   │  Sessions, Queuing, Dispatch
└──────────┬───────────────────────────────┘
           │ gRPC
           ▼
┌──────────────────────────────────────────┐
│  Service Hosts (per runtime):            │
│  windows-netfx48 → ServiceHost.Wcf      │  Existing HPC Pack DLLs (.NET Fx 4.8)
│                    + NetFxBridge         │  via dual-process architecture
│  linux-corewcf   → ServiceHost.CoreWcf  │  New CoreWCF services (.NET 8)
│  linux-net8      → ServiceHost          │  New native services (.NET 8)
│  windows-net8    → ServiceHost          │  .NET 8 on Windows
└──────────────────────────────────────────┘
```

---

## 2. Prerequisites

| Tool | Version | Purpose |
|------|---------|---------|
| .NET SDK | 8.0+ | Build new clients and service projects |
| .NET Framework | 4.8 *(optional)* | Build legacy clients that stay on .NET Framework |
| Azure CLI | 2.50+ | Manage Azure resources |
| kubectl | 1.28+ | Interact with AKS cluster |
| Git | Any | Clone the repository |

> **Note:** If your existing client uses .NET Framework 4.8 and you want to keep it that way, you only need the .NET Framework 4.8 Developer Pack — no .NET 8 SDK required on the client side.

Install .NET SDK:
```bash
# Windows (winget)
winget install Microsoft.DotNet.SDK.8

# macOS (brew)
brew install dotnet-sdk

# Linux (apt)
sudo apt-get install -y dotnet-sdk-8.0
```

---

## 3. Getting the CloudSOA Client SDK

CloudSOA provides **two client libraries** — both are drop-in replacements for `Microsoft.Hpc.Scheduler.Session` with the same API:

| Library | Target Framework | When to Use |
|---------|-----------------|-------------|
| `CloudSOA.Client.NetFx` | .NET Framework 4.8 | **Migrating existing HPC Pack clients** — keep all existing code and framework |
| `CloudSOA.Client` | .NET 8 | **New client development** — or upgrading existing clients to modern .NET |

Both libraries expose the same `using CloudSOA.Client;` namespace with the same classes (`Session`, `BrokerClient<T>`, `BrokerResponse<T>`, `SessionStartInfo`).

### Option A: Project Reference (Recommended for Development)

Clone the repository and reference the client project directly:

```bash
git clone https://github.com/xinlaoda/CloudSOA.git
```

In your client `.csproj`:
```xml
<!-- For .NET 8 clients -->
<ItemGroup>
  <ProjectReference Include="path\to\CloudSOA\src\CloudSOA.Client\CloudSOA.Client.csproj" />
</ItemGroup>

<!-- For .NET Framework 4.8 clients -->
<ItemGroup>
  <ProjectReference Include="path\to\CloudSOA\src\CloudSOA.Client.NetFx\CloudSOA.Client.NetFx.csproj" />
</ItemGroup>
```

### Option B: NuGet Package (Recommended for Production)

The CloudSOA Client SDK is published as a NuGet package on **GitHub Packages**.

**Step 1:** Create a Personal Access Token (PAT) with `read:packages` scope at
[GitHub Settings → Tokens](https://github.com/settings/tokens).

**Step 2:** Add the GitHub Packages source to your project:

```bash
dotnet nuget add source "https://nuget.pkg.github.com/xinlaoda/index.json" \
  --name CloudSOA \
  --username YOUR_GITHUB_USERNAME \
  --password YOUR_GITHUB_PAT
```

Or add a `nuget.config` file in your project/solution root:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="CloudSOA" value="https://nuget.pkg.github.com/xinlaoda/index.json" />
  </packageSources>
  <packageSourceCredentials>
    <CloudSOA>
      <add key="Username" value="YOUR_GITHUB_USERNAME" />
      <add key="ClearTextPassword" value="YOUR_GITHUB_PAT" />
    </CloudSOA>
  </packageSourceCredentials>
</configuration>
```

**Step 3:** Install the packages:

```bash
# For .NET 8 clients
dotnet add package CloudSOA.Client --version 1.0.0

# For .NET Framework 4.8 clients
dotnet add package CloudSOA.Client.NetFx --version 1.0.0
```

This automatically pulls the `CloudSOA.Common` dependency.

> **Note:** GitHub Packages requires authentication even for reading packages.
> In CI/CD, use `${{ secrets.GITHUB_TOKEN }}` as the PAT.

### Option C: Build and Pack Locally

Build local NuGet packages from the repository:

```bash
cd CloudSOA
dotnet pack src/CloudSOA.Common/CloudSOA.Common.csproj -c Release -o nupkgs
dotnet pack src/CloudSOA.Client/CloudSOA.Client.csproj -c Release -o nupkgs
dotnet pack src/CloudSOA.Client.NetFx/CloudSOA.Client.NetFx.csproj -c Release -o nupkgs
```

This produces `nupkgs/CloudSOA.Client.1.0.0.nupkg` and `nupkgs/CloudSOA.Client.NetFx.1.0.0.nupkg`.

Install from the local folder:
```bash
# .NET 8 client
dotnet add package CloudSOA.Client --source ./nupkgs

# .NET Framework 4.8 client
dotnet add package CloudSOA.Client.NetFx --source ./nupkgs
```

### SDK Dependencies (Automatically Resolved)

**CloudSOA.Client** (.NET 8):
- `CloudSOA.Common` — shared models and enums
- `System.ServiceModel.Primitives` — WCF DataContract serialization
- `Google.Protobuf` + `Grpc.Net.Client` — gRPC protocol support

**CloudSOA.Client.NetFx** (.NET Framework 4.8):
- `System.ServiceModel` — from .NET Framework GAC (WCF built-in)
- `Newtonsoft.Json` — HTTP REST communication
- No gRPC dependency (uses pure HTTP REST to communicate with broker)

---

## 4. Getting the Broker Endpoint URL

The client needs the **Broker endpoint URL** to connect. This is the URL of the CloudSOA Broker service running in your AKS cluster.

### Method 1: From the Portal UI

1. Open the CloudSOA Portal in your browser: `http://<PORTAL_EXTERNAL_IP>`
2. The **Dashboard** page displays a **Cluster Information** section at the top
3. The **Broker Endpoint** row shows the external URL (if the Broker has a LoadBalancer)
4. The **Broker (Internal)** row shows the in-cluster DNS address
5. The **Client SDK Usage** row shows the exact `SessionStartInfo` constructor call to copy into your code

To find the Portal IP:
```bash
kubectl get svc portal-service -n cloudsoa -o jsonpath='{.status.loadBalancer.ingress[0].ip}'
```

### Method 2: From the AKS Cluster Directly

If the Broker is exposed via a LoadBalancer or Ingress:

```bash
# If Broker has a LoadBalancer service
kubectl get svc broker-service -n cloudsoa -o jsonpath='{.status.loadBalancer.ingress[0].ip}'

# If using Ingress
kubectl get ingress -n cloudsoa
```

### Method 3: Port-Forward for Local Development

For development/testing, you can port-forward the Broker locally:

```bash
kubectl port-forward svc/broker-service 5050:80 -n cloudsoa
```

Then use `http://localhost:5050` as the broker endpoint.

### Method 4: Expose Broker with a LoadBalancer (Production)

To create a public endpoint for the Broker:

```bash
kubectl patch svc broker-service -n cloudsoa -p '{"spec": {"type": "LoadBalancer"}}'

# Wait for external IP
kubectl get svc broker-service -n cloudsoa -w
```

Once assigned, clients connect at `http://<EXTERNAL_IP>`.

> ⚠️ A plain LoadBalancer exposes your Broker over **unencrypted HTTP** with **no authentication**.
> This is acceptable for development/testing but **not for production**. See the next section.

### Method 5: Production Setup — AGIC + TLS + Azure AD (Recommended)

For production deployments, use **Azure Application Gateway Ingress Controller (AGIC)** to provide:

- **TLS/HTTPS termination** — all client traffic encrypted
- **Azure AD (Entra ID) authentication** — only authorized users/apps can call the API
- **Web Application Firewall (WAF)** — protection against common attacks
- **Custom domain + DNS** — e.g., `https://soa.yourcompany.com`

#### Architecture

```
Client App (CloudSOA.Client SDK)
    │  HTTPS + Bearer Token
    ▼
Azure Application Gateway (WAF + TLS)
    │  domain: soa.yourcompany.com
    │  cert:   *.yourcompany.com (Key Vault)
    │  auth:   Azure AD token validation
    ▼
AKS Ingress (AGIC) → broker-service:80 (ClusterIP, internal)
```

#### Step 1: Enable AGIC on AKS

```bash
# Enable AGIC add-on (creates an Application Gateway automatically)
az aks enable-addons \
  --resource-group <RESOURCE_GROUP> \
  --name <AKS_CLUSTER> \
  --addons ingress-appgw \
  --appgw-name cloudsoa-appgw \
  --appgw-subnet-cidr "10.225.0.0/16"
```

Or if you have an existing Application Gateway:

```bash
az aks enable-addons \
  --resource-group <RESOURCE_GROUP> \
  --name <AKS_CLUSTER> \
  --addons ingress-appgw \
  --appgw-id <EXISTING_APPGW_RESOURCE_ID>
```

#### Step 2: Create a TLS Certificate

Option A — Use Azure Key Vault + Let's Encrypt (recommended):

```bash
# Store your TLS cert in Key Vault
az keyvault certificate import \
  --vault-name <KEYVAULT_NAME> \
  --name cloudsoa-tls \
  --file soa-yourcompany-com.pfx \
  --password <PFX_PASSWORD>
```

Option B — Use a Kubernetes TLS Secret:

```bash
kubectl create secret tls cloudsoa-tls \
  --cert=tls.crt --key=tls.key \
  -n cloudsoa
```

#### Step 3: Create an Ingress Resource

Save the following as `deploy/k8s/broker-ingress.yaml`:

```yaml
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: broker-ingress
  namespace: cloudsoa
  annotations:
    kubernetes.io/ingress.class: azure/application-gateway
    # TLS redirect: HTTP → HTTPS
    appgw.ingress.kubernetes.io/ssl-redirect: "true"
    # Health probe path
    appgw.ingress.kubernetes.io/health-probe-path: /healthz
    # WAF policy (optional)
    appgw.ingress.kubernetes.io/waf-policy-for-path: /subscriptions/<SUB>/resourceGroups/<RG>/providers/Microsoft.Network/ApplicationGatewayWebApplicationFirewallPolicies/<WAF_POLICY>
    # Request timeout (seconds)
    appgw.ingress.kubernetes.io/request-timeout: "300"
spec:
  tls:
    - hosts:
        - soa.yourcompany.com
      secretName: cloudsoa-tls          # K8s secret or Key Vault reference
  rules:
    - host: soa.yourcompany.com
      http:
        paths:
          - path: /api/*
            pathType: ImplementationSpecific
            backend:
              service:
                name: broker-service
                port:
                  number: 80
          - path: /healthz
            pathType: Exact
            backend:
              service:
                name: broker-service
                port:
                  number: 80
```

Apply:
```bash
kubectl apply -f deploy/k8s/broker-ingress.yaml
```

#### Step 4: Configure DNS

Point your domain to the Application Gateway's public IP:

```bash
# Get the Application Gateway public IP
APPGW_IP=$(az network public-ip show \
  --resource-group MC_<RG>_<AKS>_<REGION> \
  --name <APPGW_PUBLIC_IP_NAME> \
  --query ipAddress -o tsv)

echo "Create a DNS A record: soa.yourcompany.com → $APPGW_IP"
```

Or use Azure DNS:
```bash
az network dns record-set a add-record \
  --resource-group <DNS_RG> \
  --zone-name yourcompany.com \
  --record-set-name soa \
  --ipv4-address $APPGW_IP
```

#### Step 5: Add Azure AD Authentication (Optional but Recommended)

Register a Microsoft Entra ID (Azure AD) app for the Broker API:

```bash
# 1. Register an app for the Broker API
az ad app create \
  --display-name "CloudSOA Broker API" \
  --identifier-uris "api://cloudsoa-broker" \
  --sign-in-audience AzureADMyOrg

# Note the Application (client) ID from the output

# 2. Register a client app for SOA clients
az ad app create \
  --display-name "CloudSOA Client" \
  --public-client-redirect-uris "http://localhost"

# Note the client Application ID
```

Configure the Broker to validate tokens by setting environment variables:

```bash
kubectl set env deployment/broker -n cloudsoa \
  AzureAd__Instance="https://login.microsoftonline.com/" \
  AzureAd__TenantId="<YOUR_TENANT_ID>" \
  AzureAd__ClientId="<BROKER_APP_CLIENT_ID>" \
  AzureAd__Audience="api://cloudsoa-broker"
```

Client code with Azure AD token:

```csharp
using Azure.Identity;
using CloudSOA.Client;

// Acquire token for the Broker API
var credential = new DefaultAzureCredential();
var token = await credential.GetTokenAsync(
    new TokenRequestContext(new[] { "api://cloudsoa-broker/.default" }));

// Pass the token via SessionStartInfo
var info = new SessionStartInfo("https://soa.yourcompany.com", "CalculatorService");
info.Properties["Authorization"] = $"Bearer {token.Token}";

using var session = Session.CreateSession(info);
// ... rest of the code is the same
```

#### Step 6: Verify the Setup

```bash
# Test HTTPS connectivity
curl -v https://soa.yourcompany.com/healthz
# Expected: HTTP/2 200 OK, body "Healthy"

# Test API (with token if Azure AD is configured)
curl -H "Authorization: Bearer <TOKEN>" \
  https://soa.yourcompany.com/api/v1/sessions
```

#### Production Checklist

| Item | Status |
|------|--------|
| TLS certificate installed | ☐ |
| HTTP → HTTPS redirect enabled | ☐ |
| Custom DNS configured | ☐ |
| WAF policy attached | ☐ |
| Azure AD app registered (Broker API) | ☐ |
| Azure AD app registered (Client) | ☐ |
| Broker validates tokens | ☐ |
| API Key middleware disabled (replaced by Azure AD) | ☐ |
| Network policy: only AGIC can reach Broker | ☐ |

### URL Format

| Environment | Broker URL |
|------------|------------|
| Local development | `http://localhost:5050` (port-forward) |
| In-cluster (pod to pod) | `http://broker-service.cloudsoa` |
| Azure LoadBalancer | `http://<EXTERNAL_IP>` |
| Azure Ingress + DNS | `https://soa.yourcompany.com` |

---

## 5. Building and Uploading a Service DLL

### 5.1 Your Existing WCF Service

If you have an existing HPC Pack SOA service DLL, it works as-is. A typical service looks like:

**ICalculator.cs** (Service Contract):
```csharp
using System.ServiceModel;

namespace CalculatorService
{
    [ServiceContract]
    public interface ICalculator
    {
        [OperationContract]
        double Add(double a, double b);

        [OperationContract]
        double Subtract(double a, double b);

        [OperationContract]
        double Multiply(double a, double b);

        [OperationContract]
        double Divide(double a, double b);

        [OperationContract]
        string Echo(string message);
    }
}
```

**CalculatorServiceImpl.cs** (Implementation):
```csharp
namespace CalculatorService
{
    public class CalculatorServiceImpl : ICalculator
    {
        public double Add(double a, double b) => a + b;
        public double Subtract(double a, double b) => a - b;
        public double Multiply(double a, double b) => a * b;
        public double Divide(double a, double b) => b == 0
            ? throw new DivideByZeroException("Cannot divide by zero")
            : a / b;
        public string Echo(string message) => $"Echo: {message}";
    }
}
```

**CalculatorService.csproj**:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="System.ServiceModel.Primitives" Version="4.10.*" />
  </ItemGroup>
</Project>
```

### 5.2 Build the Service DLL

```bash
cd samples/CalculatorService
dotnet publish -c Release -o ./publish
```

This produces `publish/CalculatorService.dll`.

### 5.3 Create a Service Configuration File

Create a `CalculatorService.cloudsoa.config` file alongside the DLL:

```json
{
  "serviceName": "CalculatorService",
  "version": "1.0.0",
  "runtime": "windows-netfx48",
  "assemblyName": "CalculatorService.dll",
  "serviceContractType": "CalculatorService.ICalculator",
  "resources": {
    "minInstances": 1,
    "maxInstances": 5,
    "cpuPerInstance": "250m",
    "memoryPerInstance": "256Mi"
  }
}
```

| Field | Description |
|-------|-------------|
| `serviceName` | Unique name for the service (used in client `SessionStartInfo`) |
| `version` | Semantic version string |
| `runtime` | See runtime table below |
| `assemblyName` | Filename of the compiled DLL |
| `serviceContractType` | Full type name of the `[ServiceContract]` interface |
| `resources.minInstances` | Minimum pod count (set to 0 for scale-to-zero) |
| `resources.maxInstances` | Maximum pod count for auto-scaling |

**Available Runtimes:**

| Runtime | Container | Use Case |
|---------|-----------|----------|
| `windows-netfx48` | Windows Server Core + NetFxBridge | **Existing HPC Pack SOA DLLs** (.NET Framework 4.0–4.8, no recompilation) |
| `linux-corewcf` | Linux + CoreWCF | **New CoreWCF services** (.NET 8, WCF-compatible contracts) |
| `linux-net8` | Linux | **New native services** (.NET 8, ISOAService interface) |
| `windows-net8` | Windows Nano Server | .NET 8 services requiring Windows APIs |

### 5.4 Register the Service via API

Upload the DLL and config to the ServiceManager:

```bash
# Port-forward to ServiceManager (if not publicly exposed)
kubectl port-forward svc/servicemanager-service 5060:80 -n cloudsoa &

# Register the service (main DLL only)
curl -X POST http://localhost:5060/api/v1/services/register \
  -F "config=@publish/CalculatorService.cloudsoa.config" \
  -F "dll=@publish/CalculatorService.dll"

# Register with dependency DLLs (if your service has extra dependencies)
curl -X POST http://localhost:5060/api/v1/services/register \
  -F "config=@publish/MyService.cloudsoa.config" \
  -F "dll=@publish/MyService.dll" \
  -F "dependencies=@publish/MyHelper.dll" \
  -F "dependencies=@publish/ThirdParty.dll"
```

**Expected response:**
```json
{
  "id": "...",
  "serviceName": "CalculatorService",
  "version": "1.0.0",
  "status": "registered"
}
```

### 5.5 Register via Portal UI

Alternatively, use the web Portal:

1. Open `http://<PORTAL_IP>` in a browser
2. Navigate to **Services** page
3. Click **Register Service**
4. Select the **Runtime** (e.g., `Windows .NET Framework 4.8` for HPC Pack DLLs)
5. Upload the main service DLL and config file
6. *(Optional)* Upload **dependency DLLs** — click "Add Dependencies" to upload additional DLLs your service requires
7. The service appears in the services list with a runtime badge

---

## 6. Deploying the Service

After registration, deploy the service to AKS:

```bash
# Via API
curl -X POST http://localhost:5060/api/v1/services/CalculatorService/deploy
```

**What happens behind the scenes:**
1. ServiceManager creates a Kubernetes Deployment (`svc-calculatorservice`) with the appropriate node selector (Windows for `windows-netfx48`/`windows-net8`, Linux for `linux-*`)
2. Creates a ClusterIP Service for the pods on port 5010
3. The pod pulls the appropriate container image (e.g., `servicehost-wcf-netfx` for `windows-netfx48`)
4. At startup, the pod downloads the DLL (and any dependencies) from Azure Blob Storage
5. **For `windows-netfx48`:** The .NET 8 gRPC host starts a NetFxBridge process (.NET Framework 4.8) that loads the legacy DLL
6. **For other runtimes:** The ServiceHost loads the DLL directly, discovers `[ServiceContract]` operations
7. The Broker routes incoming client requests to this service via K8s DNS

**Verify the deployment:**
```bash
# Check pod status
kubectl get pods -n cloudsoa -l app=svc-calculatorservice

# Check pod logs
kubectl logs -l app=svc-calculatorservice -n cloudsoa

# Expected log output:
# Downloading service DLL from blob: CalculatorService/1.0.0
#   Downloaded: CalculatorService.dll (5120 bytes)
# Service DLL ready at: C:\app\services\CalculatorService.dll
# Now listening on: http://[::]:5010
```

---

## 7. Migrating HPC Pack Client Code

### Choose Your Client Framework

| Your Current Client | Recommended CloudSOA Client | Changes Required |
|--------------------|-----------------------------|-----------------|
| .NET Framework 4.8 | `CloudSOA.Client.NetFx` | 2 code changes (using + URL), replace NuGet package |
| .NET Framework 4.8 (want to upgrade) | `CloudSOA.Client` | 2 code changes + upgrade to .NET 8 |
| .NET Core / .NET 5+ | `CloudSOA.Client` | 2 code changes, replace NuGet package |

### Side-by-Side Comparison

**Original HPC Pack Code:**
```csharp
using Microsoft.Hpc.Scheduler.Session;  // ← HPC Pack namespace

SessionStartInfo info = new SessionStartInfo("HeadNodeName", "CalculatorService");
using (Session session = Session.CreateSession(info))
{
    using (BrokerClient<ICalculator> client = new BrokerClient<ICalculator>(session))
    {
        client.SendRequest<AddRequest>(new AddRequest(5, 3), "req-1");
        client.EndRequests();

        foreach (BrokerResponse<AddResponse> resp in client.GetResponses<AddResponse>())
        {
            Console.WriteLine($"Result = {resp.Result.AddResult}");
        }
    }
    session.Close();
}
```

**Migrated CloudSOA Code:**
```csharp
using CloudSOA.Client;  // ← ONLY CHANGE: new namespace

SessionStartInfo info = new SessionStartInfo("http://broker-ip", "CalculatorService");
using (Session session = Session.CreateSession(info))                                  // same
{
    using (BrokerClient<ICalculator> client = new BrokerClient<ICalculator>(session))  // same
    {
        client.SendRequest<AddRequest>(new AddRequest(5, 3), "req-1");                 // same
        client.EndRequests();                                                          // same

        foreach (BrokerResponse<AddResponse> resp in client.GetResponses<AddResponse>()) // same
        {
            Console.WriteLine($"Result = {resp.Result.AddResult}");                    // same
        }
    }
    session.Close();                                                                   // same
}
```

### Migration Checklist

| Step | Change |
|------|--------|
| 1 | Replace `using Microsoft.Hpc.Scheduler.Session;` with `using CloudSOA.Client;` |
| 2 | Replace head node name with Broker URL: `"HeadNodeName"` → `"http://<BROKER_IP>"` |
| 3 | Update project reference: remove HPC Pack SDK, add `CloudSOA.Client` or `CloudSOA.Client.NetFx` |
| 4 | Keep all `BrokerClient<T>`, `Session`, `BrokerResponse<T>` usage as-is |
| 5 | Keep all message contracts (`AddRequest`, `AddResponse`, etc.) as-is |
| 6 | `client.Close()` and `session.Close()` still work |
| 7 | `resp.Result` throws on fault (no explicit `IsFault` check needed — matches HPC Pack behavior) |

### Update Your .csproj

Remove the old HPC Pack references:
```xml
<!-- REMOVE these -->
<Reference Include="Microsoft.Hpc.Scheduler.Session" />
<Reference Include="Microsoft.Hpc.Scheduler.Properties" />
```

Add CloudSOA.Client (**choose one based on your target framework**):
```xml
<!-- .NET Framework 4.8 client (legacy migration — no framework upgrade needed) -->
<ItemGroup>
  <PackageReference Include="CloudSOA.Client.NetFx" Version="1.0.0" />
</ItemGroup>

<!-- .NET 8 client (new development or framework upgrade) -->
<ItemGroup>
  <PackageReference Include="CloudSOA.Client" Version="1.0.0" />
</ItemGroup>
```

Or if using project reference:
```xml
<!-- .NET Framework 4.8 -->
<ItemGroup>
  <ProjectReference Include="path\to\CloudSOA\src\CloudSOA.Client.NetFx\CloudSOA.Client.NetFx.csproj" />
</ItemGroup>

<!-- .NET 8 -->
<ItemGroup>
  <ProjectReference Include="path\to\CloudSOA\src\CloudSOA.Client\CloudSOA.Client.csproj" />
</ItemGroup>
```

### Message Contracts

Your existing WCF message contracts (DataContract classes) work without changes. They must:

- Use `[DataContract]` and `[DataMember]` attributes
- Follow the naming convention: `{OperationName}Request` and `{OperationName}Response`
- The client derives the action name from the Request type: `AddRequest` → action `"Add"`

Example:
```csharp
[DataContract(Namespace = "http://tempuri.org/")]
public class AddRequest
{
    [DataMember(Order = 0)] public double a { get; set; }
    [DataMember(Order = 1)] public double b { get; set; }
    public AddRequest() { }
    public AddRequest(double a, double b) { this.a = a; this.b = b; }
}

[DataContract(Namespace = "http://tempuri.org/")]
public class AddResponse
{
    [DataMember(Order = 0)] public double AddResult { get; set; }
}
```

---

## 8. Writing a New Client from Scratch

### Step 1: Create a New Console Project

```bash
mkdir MyCalculatorClient && cd MyCalculatorClient
dotnet new console -n MyCalculatorClient
cd MyCalculatorClient
```

### Step 2: Add References

```bash
# Option A: Project reference
dotnet add reference ../../CloudSOA/src/CloudSOA.Client/CloudSOA.Client.csproj

# Also reference the service contract for the ICalculator interface
dotnet add reference ../../CloudSOA/samples/CalculatorService/CalculatorService.csproj

# Option B: NuGet package (if published)
dotnet add package CloudSOA.Client
```

### Step 3: Create Message Contracts

If you don't already have message contracts, create them to match the service operations:

**MessageContracts.cs:**
```csharp
using System.Runtime.Serialization;

[DataContract(Namespace = "http://tempuri.org/")]
public class AddRequest
{
    [DataMember(Order = 0)] public double a { get; set; }
    [DataMember(Order = 1)] public double b { get; set; }
    public AddRequest() { }
    public AddRequest(double a, double b) { this.a = a; this.b = b; }
}

[DataContract(Namespace = "http://tempuri.org/")]
public class AddResponse
{
    [DataMember(Order = 0)] public double AddResult { get; set; }
}
```

> **Important:** The `[DataMember]` property names and order must match the service method
> parameter names. For `Add(double a, double b)`, the request needs `a` and `b` properties.
> The response property name should be `{OperationName}Result` (e.g., `AddResult`).

### Step 4: Write the Client

**Program.cs:**
```csharp
using CloudSOA.Client;
using CalculatorService;  // Your service contract interface

// Get broker URL from command line or environment
var brokerUrl = args.Length > 0 ? args[0] : "http://localhost:5050";
Console.WriteLine($"Connecting to CloudSOA Broker: {brokerUrl}");

// === HPC Pack-Compatible Typed API ===
SessionStartInfo info = new SessionStartInfo(brokerUrl, "CalculatorService");

using (Session session = Session.CreateSession(info))
{
    Console.WriteLine($"Session created: {session.Id}");

    using (BrokerClient<ICalculator> client = new BrokerClient<ICalculator>(session))
    {
        // Send requests
        client.SendRequest<AddRequest>(new AddRequest(10, 5), "add-1");
        client.SendRequest<AddRequest>(new AddRequest(20, 7), "add-2");
        client.SendRequest<AddRequest>(new AddRequest(100, 50), "add-3");
        client.EndRequests();

        // Collect responses
        foreach (BrokerResponse<AddResponse> resp in client.GetResponses<AddResponse>())
        {
            if (resp.IsFault)
                Console.WriteLine($"  [{resp.UserData}] FAULT: {resp.FaultMessage}");
            else
                Console.WriteLine($"  [{resp.UserData}] Result = {resp.Result!.AddResult}");
        }
    }

    session.Close();
}

Console.WriteLine("Done.");
```

### Step 5: Build and Run

```bash
# Build
dotnet build

# Run (replace with your broker URL)
dotnet run -- http://localhost:5050

# Or with port-forward running in another terminal:
#   kubectl port-forward svc/broker-service 5050:80 -n cloudsoa
dotnet run
```

### Alternative: Simplified API (No Message Contracts)

For quick testing or new services where you don't need WCF-style message contracts:

```csharp
using CloudSOA.Client;

var brokerUrl = args.Length > 0 ? args[0] : "http://localhost:5050";

using var session = await CloudSession.CreateSessionAsync(
    new SessionStartInfo(brokerUrl, "CalculatorService"));

Console.WriteLine($"Session: {session.SessionId}");

using var client = new CloudBrokerClient(session);

// Send raw text/XML payload
client.SendRequest("Echo", "Hello World!");
client.SendRequest("Echo", "Testing 1-2-3");
await client.EndRequestsAsync();

// Get responses
var responses = await client.GetAllResponsesAsync(expectedCount: 2);
foreach (var resp in responses)
{
    Console.WriteLine($"  Response: {resp.GetPayloadString()}");
}

await session.CloseAsync();
```

---

## 9. Message Contracts Reference

### Naming Convention

| Service Method | Request Class | Response Class | Action Name |
|----------------|---------------|----------------|-------------|
| `double Add(double a, double b)` | `AddRequest` | `AddResponse` | `Add` |
| `double Subtract(double a, double b)` | `SubtractRequest` | `SubtractResponse` | `Subtract` |
| `string Echo(string message)` | `EchoRequest` | `EchoResponse` | `Echo` |

**Rules:**
- Request class name: `{OperationName}Request`
- Response class name: `{OperationName}Response`
- Response result property: `{OperationName}Result`
- `[DataMember]` names must match the method parameter names
- `[DataMember(Order = N)]` should match parameter order

### DataContract Namespace

Use `Namespace = "http://tempuri.org/"` (default WCF namespace) unless your service specifies a custom namespace in the `[ServiceContract]` attribute:

```csharp
[ServiceContract(Namespace = "http://mycompany.com/services")]
public interface IMyService { ... }
```

In that case, use the matching namespace in your message contracts:
```csharp
[DataContract(Namespace = "http://mycompany.com/services")]
public class MyRequest { ... }
```

---

## 10. Running and Testing

### Quick Test with Port-Forward

```bash
# Terminal 1: Port-forward Broker
kubectl port-forward svc/broker-service 5050:80 -n cloudsoa

# Terminal 2: Run client
cd samples/CalculatorClient
dotnet run -- http://localhost:5050
```

**Expected output:**
```
CloudSOA Calculator Client
Broker: http://localhost:5050
==================================================

[Example 1] HPC Pack-Compatible API
----------------------------------------
Session: abc123def456...
  Sent: Add(0, 0)
  Sent: Add(10, 3)
  Sent: Add(20, 6)
  Sent: Add(30, 9)
  Sent: Add(40, 12)

  Results:
  [add-0] Result = 0
  [add-1] Result = 13
  [add-2] Result = 26
  [add-3] Result = 39
  [add-4] Result = 52

[Example 2] Simplified API (raw)
----------------------------------------
Session: ...
  [null] Echo: Hello from CloudSOA! [processed at 2026-...]
  [null] Echo: Testing 1-2-3 [processed at 2026-...]

==================================================
Done.
```

### Verify Service Health

```bash
# Check Broker health
curl http://localhost:5050/healthz
# → Healthy

# List active sessions
curl http://localhost:5050/api/v1/sessions
# → [{ "id": "...", "serviceName": "CalculatorService", ... }]
```

---

## 11. Concurrency and Performance

### Running a Load Test

Use the built-in ConcurrencyTest tool:

```bash
cd samples/ConcurrencyTest
dotnet run -- http://localhost:5050 10 100
#                                   ^  ^
#                            workers  total calls
```

### Expected Performance

Test results from a real AKS deployment (1 Broker pod, 1 CalculatorService pod):

| Metric | Value |
|--------|-------|
| Total calls | 100 |
| Concurrent workers | 10 |
| Success rate | 100% |
| Throughput | 5.5 calls/sec |
| Total time | 18.3s |

Performance scales linearly with more ServiceHost pods. Configure auto-scaling in the service config:

```json
{
  "resources": {
    "minInstances": 2,
    "maxInstances": 20
  }
}
```

---

## 12. Troubleshooting

### "No connection could be made" Error

**Cause:** Cannot reach the Broker endpoint.

**Fix:**
- Verify port-forward is running: `kubectl port-forward svc/broker-service 5050:80 -n cloudsoa`
- Check Broker pod status: `kubectl get pods -n cloudsoa -l app=broker`
- Check Broker logs: `kubectl logs -l app=broker -n cloudsoa --tail=50`

### "Session not found" Error

**Cause:** Session expired or Broker restarted (sessions are in-memory Redis).

**Fix:** Create a new session. Sessions have a default idle timeout of 30 minutes.

### Responses Return `0` or Default Values

**Cause:** Service DLL parameter deserialization failed.

**Fix:**
- Ensure `[DataMember]` property names exactly match method parameter names
- Ensure `[DataMember(Order = N)]` matches parameter position
- Check ServiceHost logs: `kubectl logs -l app=svc-calculatorservice -n cloudsoa`

### "Unknown action" Error

**Cause:** Action name doesn't match any `[OperationContract]` method.

**Fix:**
- The action name is derived from request type name: `AddRequest` → `"Add"`
- Ensure the service DLL has a method matching the action name
- Check discovered operations in ServiceHost logs: `Discovered WCF service 'ICalculator' with 5 operations`

### Service Pod Stuck in Pending

**Cause:** No available nodes matching the pod's OS requirement.

**Fix:**
- WCF services need Windows nodes: `kubectl get nodes -l kubernetes.io/os=windows`
- Scale the Windows node pool: `az aks nodepool scale -g <rg> --cluster-name <cluster> -n win -c 1`

---

## 13. API Reference

### Session API (Broker)

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/api/v1/sessions` | Create a new session |
| `GET` | `/api/v1/sessions` | List all active sessions |
| `GET` | `/api/v1/sessions/{id}` | Get session details |
| `POST` | `/api/v1/sessions/{id}/attach` | Attach to existing session |
| `GET` | `/api/v1/sessions/{id}/status` | Get session status |
| `DELETE` | `/api/v1/sessions/{id}` | Close session |

### Request/Response API (Broker)

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/api/v1/sessions/{id}/requests` | Submit batch of requests |
| `POST` | `/api/v1/sessions/{id}/requests/flush` | Signal end of requests |
| `GET` | `/api/v1/sessions/{id}/responses?maxCount=100` | Fetch responses |

### Service Management API (ServiceManager)

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/api/v1/services/register` | Upload and register service DLL |
| `GET` | `/api/v1/services` | List registered services |
| `GET` | `/api/v1/services/{name}` | Get service details |
| `POST` | `/api/v1/services/{name}/deploy` | Deploy service to AKS |
| `POST` | `/api/v1/services/{name}/stop` | Stop all service instances |
| `DELETE` | `/api/v1/services/{name}` | Unregister service |

### Create Session Request Body

```json
{
  "serviceName": "CalculatorService",
  "sessionType": 0,
  "minimumUnits": 1,
  "maximumUnits": 1,
  "transportScheme": 0
}
```

| Field | Values |
|-------|--------|
| `sessionType` | `0` = Interactive, `1` = Durable |
| `transportScheme` | `0` = gRPC, `1` = HTTP, `2` = WebSocket |

### Submit Requests Body

```json
{
  "requests": [
    {
      "action": "Add",
      "payload": "PEFkZFJlcXVlc3Q+Li4uPC9BZZR...",
      "userData": "req-1"
    }
  ]
}
```

The `payload` field is a Base64-encoded XML string produced by `DataContractSerializer`.

---

## Quick Start Summary

```bash
# 1. Port-forward the Broker
kubectl port-forward svc/broker-service 5050:80 -n cloudsoa &

# 2. Build the sample service DLL
cd samples/CalculatorService && dotnet publish -c Release -o publish

# 3. Register and deploy (via ServiceManager)
kubectl port-forward svc/servicemanager-service 5060:80 -n cloudsoa &
curl -X POST http://localhost:5060/api/v1/services/register \
  -F "config=@publish/CalculatorService.cloudsoa.config" \
  -F "dll=@publish/CalculatorService.dll"
curl -X POST http://localhost:5060/api/v1/services/CalculatorService/deploy

# 4. Run the client
cd ../CalculatorClient && dotnet run -- http://localhost:5050
```
