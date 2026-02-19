using System.Text.Json.Serialization;

namespace CloudSOA.Portal.Services;

/// <summary>
/// Wraps HttpClient calls to the CloudSOA Broker API.
/// </summary>
public class BrokerApiClient
{
    private readonly HttpClient _httpClient;

    public BrokerApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<List<SessionSummary>> GetSessionsAsync()
    {
        var response = await _httpClient.GetAsync("/api/v1/sessions");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<SessionSummary>>() ?? new();
    }

    public async Task<SessionSummary> GetSessionAsync(string sessionId)
    {
        var response = await _httpClient.GetAsync($"/api/v1/sessions/{sessionId}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SessionSummary>()
            ?? throw new InvalidOperationException("Session not found.");
    }

    public async Task CloseSessionAsync(string sessionId)
    {
        var response = await _httpClient.DeleteAsync($"/api/v1/sessions/{sessionId}");
        response.EnsureSuccessStatusCode();
    }

    public async Task<ClusterMetrics> GetMetricsAsync()
    {
        var response = await _httpClient.GetAsync("/api/v1/metrics");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ClusterMetrics>() ?? new();
    }
}

public class SessionSummary
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("serviceName")]
    public string ServiceName { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("lastAccessedAt")]
    public DateTime? LastAccessedAt { get; set; }

    [JsonPropertyName("clientId")]
    public string? ClientId { get; set; }

    [JsonPropertyName("requestCount")]
    public long RequestCount { get; set; }

    [JsonPropertyName("responseCount")]
    public long ResponseCount { get; set; }
}

public class ClusterMetrics
{
    [JsonPropertyName("runningServices")]
    public int RunningServices { get; set; }

    [JsonPropertyName("totalPods")]
    public int TotalPods { get; set; }

    [JsonPropertyName("requestThroughput")]
    public int RequestThroughput { get; set; }

    [JsonPropertyName("brokerHealthy")]
    public bool BrokerHealthy { get; set; }

    [JsonPropertyName("serviceManagerHealthy")]
    public bool ServiceManagerHealthy { get; set; }

    [JsonPropertyName("redisHealthy")]
    public bool RedisHealthy { get; set; }

    [JsonPropertyName("kubernetesHealthy")]
    public bool KubernetesHealthy { get; set; }

    [JsonPropertyName("serviceHostPods")]
    public List<PodStatus> ServiceHostPods { get; set; } = new();

    [JsonPropertyName("brokerPods")]
    public List<PodStatus> BrokerPods { get; set; } = new();

    [JsonPropertyName("queueDepths")]
    public List<QueueDepth> QueueDepths { get; set; } = new();

    [JsonPropertyName("clusterInfo")]
    public Dictionary<string, string> ClusterInfo { get; set; } = new();
}

public class PodStatus
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("node")]
    public string Node { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("cpuUsage")]
    public string CpuUsage { get; set; } = string.Empty;

    [JsonPropertyName("memoryUsage")]
    public string MemoryUsage { get; set; } = string.Empty;

    [JsonPropertyName("restarts")]
    public int Restarts { get; set; }
}

public class QueueDepth
{
    [JsonPropertyName("queueName")]
    public string QueueName { get; set; } = string.Empty;

    [JsonPropertyName("pending")]
    public int Pending { get; set; }

    [JsonPropertyName("processing")]
    public int Processing { get; set; }
}
