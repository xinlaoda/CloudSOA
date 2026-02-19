using System.Text.Json.Serialization;

namespace CloudSOA.ServiceManager.Models;

public class ServiceRegistration
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("serviceName")]
    public string ServiceName { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>"wcf-netfx" | "native-net8"</summary>
    [JsonPropertyName("runtime")]
    public string Runtime { get; set; } = "native-net8";

    /// <summary>Primary assembly file name, e.g. "MyService.dll".</summary>
    [JsonPropertyName("assemblyName")]
    public string AssemblyName { get; set; } = string.Empty;

    /// <summary>For WCF services: the fully-qualified service contract type name.</summary>
    [JsonPropertyName("serviceContractType")]
    public string? ServiceContractType { get; set; }

    /// <summary>Path inside the "service-packages" blob container.</summary>
    [JsonPropertyName("blobPath")]
    public string BlobPath { get; set; } = string.Empty;

    [JsonPropertyName("resources")]
    public ServiceResources Resources { get; set; } = new();

    /// <summary>Additional DLLs required by the service.</summary>
    [JsonPropertyName("dependencies")]
    public List<string> Dependencies { get; set; } = new();

    /// <summary>Extra environment variables injected into the host pod.</summary>
    [JsonPropertyName("environment")]
    public Dictionary<string, string> Environment { get; set; } = new();

    /// <summary>"registered" | "deployed" | "stopped" | "error"</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "registered";

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class ServiceResources
{
    [JsonPropertyName("minInstances")]
    public int MinInstances { get; set; } = 0;

    [JsonPropertyName("maxInstances")]
    public int MaxInstances { get; set; } = 10;

    [JsonPropertyName("cpuPerInstance")]
    public string CpuPerInstance { get; set; } = "500m";

    [JsonPropertyName("memoryPerInstance")]
    public string MemoryPerInstance { get; set; } = "512Mi";
}
