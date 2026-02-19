using CloudSOA.Common.Interfaces;
using k8s;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;

namespace CloudSOA.Broker.Controllers;

[ApiController]
[Route("api/v1/metrics")]
public class ClusterMetricsController : ControllerBase
{
    private readonly ISessionStore _sessionStore;
    private readonly IRequestQueue _requestQueue;
    private readonly IResponseStore _responseStore;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<ClusterMetricsController> _logger;

    public ClusterMetricsController(
        ISessionStore sessionStore,
        IRequestQueue requestQueue,
        IResponseStore responseStore,
        IConnectionMultiplexer redis,
        ILogger<ClusterMetricsController> logger)
    {
        _sessionStore = sessionStore;
        _requestQueue = requestQueue;
        _responseStore = responseStore;
        _redis = redis;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetMetrics(CancellationToken ct)
    {
        var sessions = await _sessionStore.ListAsync(ct);
        var activeSessions = sessions.Where(s => s.State == Common.Enums.SessionState.Active).ToList();

        // Queue depths per active session
        var queueDepths = new List<object>();
        long totalPending = 0;
        long totalResponses = 0;
        foreach (var s in activeSessions)
        {
            var pending = await _requestQueue.GetQueueDepthAsync(s.SessionId, ct);
            var responses = await _responseStore.GetCountAsync(s.SessionId, ct);
            totalPending += pending;
            totalResponses += responses;
            if (pending > 0 || responses > 0)
            {
                queueDepths.Add(new
                {
                    queueName = $"{s.ServiceName}/{s.SessionId[..8]}",
                    pending = (int)pending,
                    processing = (int)responses
                });
            }
        }

        // Redis health
        bool redisHealthy;
        try
        {
            var pong = await _redis.GetDatabase().PingAsync();
            redisHealthy = pong.TotalMilliseconds < 5000;
        }
        catch { redisHealthy = false; }

        // K8s pod info + cluster endpoint info
        var brokerPods = new List<object>();
        var serviceHostPods = new List<object>();
        var servicePods = new List<object>();
        int totalPods = 0;
        int runningServicePods = 0;
        bool k8sHealthy = true;
        var clusterInfo = new Dictionary<string, string>();

        try
        {
            var config = KubernetesClientConfiguration.InClusterConfig();
            var k8s = new Kubernetes(config);
            var pods = await k8s.CoreV1.ListNamespacedPodAsync("cloudsoa", cancellationToken: ct);
            totalPods = pods.Items.Count;

            foreach (var pod in pods.Items)
            {
                var info = new
                {
                    name = pod.Metadata.Name,
                    node = pod.Spec.NodeName ?? "",
                    status = pod.Status.Phase,
                    cpuUsage = pod.Spec.Containers.FirstOrDefault()?.Resources?.Requests?.TryGetValue("cpu", out var cpu) == true ? cpu.ToString() : "-",
                    memoryUsage = pod.Spec.Containers.FirstOrDefault()?.Resources?.Requests?.TryGetValue("memory", out var mem) == true ? mem.ToString() : "-",
                    restarts = pod.Status.ContainerStatuses?.Sum(c => c.RestartCount) ?? 0
                };

                var appLabel = pod.Metadata.Labels != null && pod.Metadata.Labels.ContainsKey("app")
                    ? pod.Metadata.Labels["app"] : "";

                if (appLabel == "broker")
                {
                    brokerPods.Add(info);
                }
                else if (pod.Metadata.Name.StartsWith("svc-"))
                {
                    servicePods.Add(info);
                    if (pod.Status.Phase == "Running")
                        runningServicePods++;
                }
                else if (appLabel.StartsWith("servicehost") || appLabel == "portal" || appLabel == "servicemanager")
                {
                    serviceHostPods.Add(info);
                }
            }

            // Cluster service endpoints
            var services = await k8s.CoreV1.ListNamespacedServiceAsync("cloudsoa", cancellationToken: ct);
            foreach (var svc in services.Items)
            {
                var name = svc.Metadata.Name;
                var svcType = svc.Spec.Type;
                var port = svc.Spec.Ports?.FirstOrDefault()?.Port ?? 80;

                if (svcType == "LoadBalancer")
                {
                    var ingress = svc.Status?.LoadBalancer?.Ingress?.FirstOrDefault();
                    var ip = ingress?.Ip ?? ingress?.Hostname ?? "<pending>";
                    clusterInfo[name] = port == 80 ? $"http://{ip}" : $"http://{ip}:{port}";
                }
                else if (svcType == "ClusterIP")
                {
                    clusterInfo[name] = $"http://{name}.cloudsoa:{port}";
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query Kubernetes API");
            k8sHealthy = false;
        }

        return Ok(new
        {
            runningServices = runningServicePods,
            totalPods,
            requestThroughput = (int)(Metrics.BrokerMetrics.RequestsProcessed.Value),
            brokerHealthy = true,
            serviceManagerHealthy = true,
            redisHealthy,
            kubernetesHealthy = k8sHealthy,
            servicePods,
            serviceHostPods,
            brokerPods,
            queueDepths,
            clusterInfo
        });
    }
}
