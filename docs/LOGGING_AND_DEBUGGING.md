# CloudSOA Logging & Debugging Guide

## Current Logging Architecture

```
┌─────────────┐   stdout/stderr   ┌──────────────┐   ┌──────────────────┐
│  All Pods    │ ───────────────── │  AKS Node    │──→│ Azure Monitor    │
│  (console)   │   container logs  │  ama-logs    │   │ Log Analytics    │
└─────────────┘                   └──────────────┘   └──────────────────┘
                                                              │
                                                     KQL queries / Azure Portal
```

All components use **Microsoft.Extensions.Logging** with console output only. Container logs are collected by **Azure Monitor Container Insights** (ama-logs DaemonSet) and shipped to **Log Analytics Workspace**.

---

## 1. Kubernetes Log Commands (Quick Debugging)

### Broker

```bash
# Live logs (follow)
kubectl logs -f deployment/broker -n cloudsoa

# Last 100 lines
kubectl logs deployment/broker -n cloudsoa --tail=100

# Logs from specific pod (when multiple replicas)
kubectl get pods -n cloudsoa -l app=broker
kubectl logs broker-xxxx-yyyy -n cloudsoa

# All broker pods simultaneously
kubectl logs -l app=broker -n cloudsoa --tail=50

# Logs from previous crashed container
kubectl logs broker-xxxx-yyyy -n cloudsoa --previous

# Filter for errors
kubectl logs deployment/broker -n cloudsoa --tail=500 | Select-String "error|fail|exception" -CaseSensitive:$false
```

### Service Pods (WCF Windows Containers)

```bash
# CalculatorService
kubectl logs deployment/svc-calculatorservice -n cloudsoa --tail=100

# HeavyComputeService
kubectl logs deployment/svc-heavycomputeservice -n cloudsoa --tail=100

# Check gRPC call logs
kubectl logs deployment/svc-heavycomputeservice -n cloudsoa | Select-String "Execute|error"

# Watch for errors in real-time
kubectl logs -f deployment/svc-heavycomputeservice -n cloudsoa | Select-String "fail|error|exception" -CaseSensitive:$false
```

### Service Manager

```bash
kubectl logs deployment/servicemanager -n cloudsoa --tail=100

# Filter out health checks (very noisy)
kubectl logs deployment/servicemanager -n cloudsoa --tail=200 | Select-String -NotMatch "healthz"
```

### Portal

```bash
kubectl logs deployment/portal -n cloudsoa --tail=100
```

---

## 2. Azure Monitor (Log Analytics) — Persistent Logs

All pod logs are stored in Azure Log Analytics. Access via **Azure Portal > AKS cluster > Monitoring > Logs**.

### Useful KQL Queries

```kusto
// All errors across all pods in last 1 hour
ContainerLogV2
| where TimeGenerated > ago(1h)
| where PodNamespace == "cloudsoa"
| where LogMessage has_any ("error", "fail", "exception")
| project TimeGenerated, PodName, LogMessage
| order by TimeGenerated desc

// Broker session lifecycle
ContainerLogV2
| where PodNamespace == "cloudsoa"
| where PodName startswith "broker-"
| where LogMessage has_any ("Session created", "Session closed", "dispatching")
| project TimeGenerated, LogMessage
| order by TimeGenerated desc

// WCF service execution errors
ContainerLogV2
| where PodNamespace == "cloudsoa"
| where PodName startswith "svc-"
| where LogMessage has_any ("error", "fault", "exception")
| project TimeGenerated, PodName, LogMessage

// gRPC call latencies on service pods
ContainerLogV2
| where PodNamespace == "cloudsoa"
| where PodName startswith "svc-"
| where LogMessage has "Request finished HTTP/2 POST"
| parse LogMessage with * "Execute - 200 - application/grpc " duration "ms"
| project TimeGenerated, PodName, toreal(duration)
| summarize avg(toreal_duration), percentile(toreal_duration, 95) by bin(TimeGenerated, 1m)

// Pod restart events
KubeEvents
| where Namespace == "cloudsoa"
| where Reason in ("BackOff", "Failed", "Killing", "Unhealthy")
| project TimeGenerated, Name, Reason, Message
| order by TimeGenerated desc
```

### Resource Metrics (KQL)

```kusto
// CPU usage by pod over time
Perf
| where ObjectName == "K8SContainer"
| where InstanceName contains "cloudsoa"
| where CounterName == "cpuUsageNanoCores"
| summarize avg(CounterValue)/1000000 by bin(TimeGenerated, 1m), InstanceName
| render timechart

// Memory usage
Perf
| where ObjectName == "K8SContainer"
| where InstanceName contains "cloudsoa"
| where CounterName == "memoryRssBytes"
| summarize avg(CounterValue)/1048576 by bin(TimeGenerated, 1m), InstanceName
| render timechart
```

---

## 3. Prometheus Metrics (Real-Time Monitoring)

Broker exposes custom metrics at `/metrics`:

```bash
# Port-forward to broker
kubectl port-forward -n cloudsoa svc/broker-service 5050:80

# View all CloudSOA metrics
(Invoke-WebRequest -Uri http://localhost:5050/metrics).Content -split "`n" | Select-String "cloudsoa_"
```

### Key Metrics

| Metric | Type | Description |
|--------|------|-------------|
| `cloudsoa_sessions_active` | Gauge | Current active sessions |
| `cloudsoa_sessions_created_total` | Counter | Total sessions created (by service) |
| `cloudsoa_requests_processed_total` | Counter | Requests processed |
| `cloudsoa_requests_failed_total` | Counter | Requests that threw exceptions |
| `cloudsoa_request_duration_seconds` | Histogram | Processing latency (broker-side) |
| `cloudsoa_dispatchers_active` | Gauge | Active dispatcher loops |
| `cloudsoa_responses_delivered_total` | Counter | Responses fetched by clients |

---

## 4. Debugging Common Issues

### Issue: Client gets timeout / no response

```bash
# 1. Check session exists
kubectl port-forward -n cloudsoa svc/broker-service 5050:80
Invoke-RestMethod http://localhost:5050/api/v1/sessions

# 2. Check dispatchers are running (should match session count)
(Invoke-WebRequest http://localhost:5050/metrics).Content | Select-String "dispatchers_active"

# 3. Check queue depth (requests stuck?)
(Invoke-WebRequest http://localhost:5050/metrics).Content | Select-String "queue_depth"

# 4. Check broker can reach service pod
kubectl logs deployment/broker -n cloudsoa --tail=50 | Select-String "gRPC|route|Failed"

# 5. Check service pod is running and healthy
kubectl get pods -n cloudsoa | Select-String "svc-"
kubectl describe pod svc-xxx -n cloudsoa | Select-String "Ready|Restart|Warning|Error"
```

### Issue: Service returns IsFault=true

```bash
# 1. Check service pod logs for deserialization or execution errors
kubectl logs deployment/svc-heavycomputeservice -n cloudsoa --tail=50 | Select-String "error|fault"

# 2. Common causes:
#    - Payload format wrong (need XML for multi-param WCF methods)
#    - DLL version mismatch
#    - Missing dependencies in service DLL

# 3. Verify service DLL is loaded correctly
kubectl logs deployment/svc-heavycomputeservice -n cloudsoa | Select-String "Loaded|assembly|contract"
```

### Issue: Pod stuck in Pending

```bash
# Check why
kubectl describe pod <pod-name> -n cloudsoa | Select-String "Events:" -Context 0,10

# Common causes:
# - Insufficient CPU → need more nodes or reduce resource requests
# - Node affinity mismatch → Windows service on Linux node or vice versa
# - Image pull failed → check ACR credentials

# Quick fix for CPU:
kubectl top nodes
kubectl get hpa -n cloudsoa
```

### Issue: Service not scaling

```bash
# 1. Check HPA status
kubectl get hpa -n cloudsoa
kubectl describe hpa <hpa-name> -n cloudsoa

# 2. Check KEDA ScaledObject
kubectl get scaledobject -n cloudsoa
kubectl describe scaledobject <name> -n cloudsoa

# 3. Verify metrics-server works
kubectl top pods -n cloudsoa

# 4. Check if resource requests are set (HPA needs them)
kubectl get deployment svc-xxx -n cloudsoa -o jsonpath='{.spec.template.spec.containers[0].resources}'
```

### Issue: DLL upload / deployment fails

```bash
# 1. Check ServiceManager logs
kubectl logs deployment/servicemanager -n cloudsoa --tail=50 | Select-String -NotMatch "healthz"

# 2. Verify blob storage
$connStr = "<your-blob-connection-string>"
az storage blob list --container-name service-packages --prefix "ServiceName/" --connection-string $connStr -o table

# 3. Check service registration
kubectl port-forward -n cloudsoa svc/servicemanager-service 5060:80
Invoke-RestMethod http://localhost:5060/api/v1/services
```

---

## 5. Client-Side Debugging

### .NET 8 Client (`CloudSOA.Client`)

The CloudSOA Client SDK does not emit logs. Debug client issues using HTTP-level tracing:

```csharp
// Option 1: Enable .NET HttpClient logging
var handler = new HttpClientHandler();
var loggingHandler = new LoggingDelegatingHandler(handler);

// Option 2: Use environment variable
// Set DOTNET_SYSTEM_NET_HTTP_SOCKETSHTTPHANDLER_LOGGING=true

// Option 3: Manual debug in your code
using var session = await CloudSession.CreateSessionAsync(
    new SessionStartInfo(brokerUrl, "ServiceName"));
Console.WriteLine($"Session: {session.SessionId}, State: {session.State}");

using var client = new CloudBrokerClient(session);
client.SendRequest("Action", payload);
var sent = await client.EndRequestsAsync();
Console.WriteLine($"Sent: {sent} requests");

var responses = await client.GetAllResponsesAsync(sent, TimeSpan.FromSeconds(60));
foreach (var r in responses)
{
    Console.WriteLine($"  MsgId={r.MessageId}, IsFault={r.IsFault}");
    if (r.IsFault)
        Console.WriteLine($"  FaultMessage: {r.FaultMessage}");
}
```

### .NET Framework 4.8 Client (`CloudSOA.Client.NetFx`)

For migrated HPC Pack clients using `CloudSOA.Client.NetFx`:

```csharp
using CloudSOA.Client;

// Debug: print session info
SessionStartInfo info = new SessionStartInfo("http://broker-ip", "CalculatorService");
using (Session session = Session.CreateSession(info))
{
    Console.WriteLine($"Session: {session.Id}");

    using (BrokerClient<ICalculator> client = new BrokerClient<ICalculator>(session))
    {
        client.SendRequest<AddRequest>(new AddRequest(1, 2));
        client.EndRequests();

        foreach (BrokerResponse<AddResponse> resp in client.GetResponses<AddResponse>())
        {
            Console.WriteLine($"  IsFault={resp.IsFault}");
            if (resp.IsFault)
                Console.WriteLine($"  FaultMessage: {resp.FaultMessage}");
            else
                Console.WriteLine($"  Result = {resp.Result.AddResult}");
        }
        client.Close();
    }
    session.Close();
}
```

> **Note:** `BrokerResponse.Result` throws `InvalidOperationException` when `IsFault=true` — this matches HPC Pack behavior, so explicit fault checking is optional.

### Debugging NetFxBridge (windows-netfx48 runtime)

For services running on the `windows-netfx48` runtime, the ServiceHost.Wcf uses a dual-process architecture: .NET 8 gRPC host + NetFxBridge (.NET Framework 4.8). Debug both processes:

```bash
# 1. Check main ServiceHost.Wcf logs (.NET 8 host)
kubectl logs deployment/svc-calculatorservice -n cloudsoa --tail=100

# 2. Key log messages to look for:
#    "Direct assembly load failed... falling back to NetFxBridge"  — bridge activated
#    "NetFxBridge started, waiting for ready..."                    — bridge launching
#    "NetFxBridge ready: loaded CalculatorService.dll with 5 operations" — success
#    "NetFxBridge process exited unexpectedly"                      — bridge crashed

# 3. If bridge fails to load DLL:
#    Check that the DLL was built for .NET Framework 4.0–4.8
#    Check that all dependency DLLs were uploaded
kubectl logs deployment/svc-calculatorservice -n cloudsoa | Select-String "bridge|error|fault"

# 4. Exec into the pod to test bridge manually
kubectl exec -it deployment/svc-calculatorservice -n cloudsoa -- cmd
# Inside the pod:
dir C:\app\bridge\               # NetFxBridge.exe location
dir C:\app\services\             # Downloaded DLLs
C:\app\bridge\NetFxBridge.exe C:\app\services\CalculatorService.dll   # Manual test
```

### Capture HTTP Traffic

```powershell
# Use Fiddler or mitmproxy to see raw HTTP between client and broker
# Or use PowerShell to manually inspect:

# Create session
$s = Invoke-RestMethod -Uri http://<broker>/api/v1/sessions -Method POST `
    -ContentType "application/json" -Body '{"serviceName":"YourService"}' -Verbose

# Send request
$payload = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("<Parameters><x>1</x></Parameters>"))
Invoke-RestMethod -Uri "http://<broker>/api/v1/sessions/$($s.sessionId)/requests" `
    -Method POST -ContentType "application/json" `
    -Body (@{requests=@(@{action="YourAction";payload=$payload})} | ConvertTo-Json -Depth 3) -Verbose

# Flush
Invoke-RestMethod -Uri "http://<broker>/api/v1/sessions/$($s.sessionId)/requests/flush" -Method POST

# Poll responses
Invoke-RestMethod -Uri "http://<broker>/api/v1/sessions/$($s.sessionId)/responses"
```

---

## 6. Enable Debug-Level Logging

To temporarily increase log verbosity for a component, set environment variable:

```bash
# Broker — enable debug logging
kubectl set env deployment/broker -n cloudsoa Logging__LogLevel__Default=Debug

# Revert to normal
kubectl set env deployment/broker -n cloudsoa Logging__LogLevel__Default=Information

# Service pods — debug gRPC calls
kubectl set env deployment/svc-heavycomputeservice -n cloudsoa Logging__LogLevel__Default=Debug
```

Or edit `appsettings.json` and redeploy:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Information",
      "Grpc": "Debug",
      "CloudSOA": "Debug"
    }
  }
}
```

---

## 7. Quick Health Check Commands

```bash
# All-in-one cluster health check
kubectl get pods -n cloudsoa -o wide
kubectl get hpa -n cloudsoa
kubectl top nodes
kubectl top pods -n cloudsoa

# Check all services responding
kubectl port-forward -n cloudsoa svc/broker-service 5050:80
Invoke-RestMethod http://localhost:5050/healthz          # Broker health
Invoke-RestMethod http://localhost:5050/api/v1/metrics   # Cluster metrics

kubectl port-forward -n cloudsoa svc/servicemanager-service 5060:80
Invoke-RestMethod http://localhost:5060/healthz          # ServiceManager health
Invoke-RestMethod http://localhost:5060/api/v1/services  # Registered services

# Portal (already has LoadBalancer)
Invoke-WebRequest http://48.200.52.5/                    # Portal dashboard
```

---

## 8. Log Locations Summary

| Component | Log Method | Access |
|-----------|-----------|--------|
| **Broker** | Console (ILogger) | `kubectl logs deployment/broker -n cloudsoa` |
| **ServiceManager** | Console (ILogger) | `kubectl logs deployment/servicemanager -n cloudsoa` |
| **Portal** | Console (ILogger) | `kubectl logs deployment/portal -n cloudsoa` |
| **Service Pods** | Console (ILogger) | `kubectl logs deployment/svc-<name> -n cloudsoa` |
| **NetFxBridge** | Stdout (via host) | Same pod logs as Service Pods (bridge output forwarded to host) |
| **All Pods** | Azure Monitor | Azure Portal > AKS > Monitoring > Logs (KQL) |
| **Client SDK (.NET 8)** | None (user code) | Add your own logging |
| **Client SDK (.NET Fx)** | None (user code) | Add your own logging |
| **Prometheus** | `/metrics` endpoint | Port-forward to broker:5050/metrics |
| **HPA/Scaling** | K8s events | `kubectl describe hpa <name> -n cloudsoa` |
| **Pod Events** | K8s events | `kubectl describe pod <name> -n cloudsoa` |
