using System.Text.Json.Serialization;

namespace CloudSOA.Portal.Services;

/// <summary>
/// Wraps HttpClient calls to the CloudSOA Service Manager API.
/// </summary>
public class ServiceManagerApiClient
{
    private readonly HttpClient _httpClient;

    public ServiceManagerApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<List<ServiceInfo>> GetServicesAsync()
    {
        var response = await _httpClient.GetAsync("/api/v1/services");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<ServiceInfo>>() ?? new();
    }

    public async Task<ServiceInfo> GetServiceAsync(string serviceName)
    {
        var response = await _httpClient.GetAsync($"/api/v1/services/{serviceName}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ServiceInfo>()
            ?? throw new InvalidOperationException("Service not found.");
    }

    public async Task RegisterServiceAsync(MultipartFormDataContent content)
    {
        var response = await _httpClient.PostAsync("/api/v1/services", content);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeployAsync(string serviceName)
    {
        var response = await _httpClient.PostAsync($"/api/v1/services/{serviceName}/deploy", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task StopAsync(string serviceName)
    {
        var response = await _httpClient.PostAsync($"/api/v1/services/{serviceName}/stop", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task ScaleAsync(string serviceName, int replicas)
    {
        var response = await _httpClient.PostAsJsonAsync($"/api/v1/services/{serviceName}/scale", new { replicas });
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteAsync(string serviceName)
    {
        var response = await _httpClient.DeleteAsync($"/api/v1/services/{serviceName}");
        response.EnsureSuccessStatusCode();
    }
}

/// <summary>
/// Matches the JSON shape returned by GET /api/v1/services
/// </summary>
public class ServiceInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("serviceName")]
    public string ServiceName { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("runtime")]
    public string Runtime { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("assemblyName")]
    public string AssemblyName { get; set; } = string.Empty;

    [JsonPropertyName("serviceContractType")]
    public string? ServiceContractType { get; set; }

    [JsonPropertyName("resources")]
    public ServiceResourcesInfo Resources { get; set; } = new();

    /// <summary>Friendly runtime label for display</summary>
    public string RuntimeLabel => Runtime switch
    {
        "wcf-netfx" => "WCF (.NET Fx)",
        "native-net8" => ".NET 8",
        _ => Runtime
    };
}

public class ServiceResourcesInfo
{
    [JsonPropertyName("minInstances")]
    public int MinInstances { get; set; }

    [JsonPropertyName("maxInstances")]
    public int MaxInstances { get; set; }

    [JsonPropertyName("cpuPerInstance")]
    public string CpuPerInstance { get; set; } = string.Empty;

    [JsonPropertyName("memoryPerInstance")]
    public string MemoryPerInstance { get; set; } = string.Empty;
}
