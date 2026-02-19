using CloudSOA.ServiceManager.Models;
using k8s;
using k8s.Models;

namespace CloudSOA.ServiceManager.Services;

/// <summary>
/// Manages Kubernetes deployments for registered services.
/// Creates/updates Deployment manifests dynamically, choosing the correct
/// node pool (Linux vs Windows) based on the service Runtime.
/// </summary>
public class ServiceDeploymentService
{
    private readonly ServiceRegistrationStore _store;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ServiceDeploymentService> _logger;
    private const string Namespace = "cloudsoa";

    public ServiceDeploymentService(
        ServiceRegistrationStore store,
        IConfiguration configuration,
        ILogger<ServiceDeploymentService> logger)
    {
        _store = store;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<ServiceRegistration> DeployServiceAsync(string registrationId, string serviceName, CancellationToken ct = default)
    {
        var registration = await _store.GetByIdAsync(registrationId, serviceName, ct)
            ?? throw new KeyNotFoundException($"Registration '{registrationId}' not found.");

        var k8s = new Kubernetes(KubernetesClientConfiguration.InClusterConfig());
        var deployName = DeploymentName(registration);
        var replicas = Math.Max(registration.Resources.MinInstances, 1);

        var isWindows = registration.Runtime == "wcf-netfx";
        var image = isWindows
            ? _configuration["ServiceHost:WcfImage"] ?? "xxincloudsoaacr.azurecr.io/servicehost-wcf:v1.0.0"
            : _configuration["ServiceHost:Image"] ?? "xxincloudsoaacr.azurecr.io/servicehost-echo:v1.0.0";

        var blobConn = _configuration["ConnectionStrings:AzureBlobStorage"]
            ?? _configuration["AzureBlob:ConnectionString"] ?? "";

        var deployment = new V1Deployment
        {
            Metadata = new V1ObjectMeta
            {
                Name = deployName,
                NamespaceProperty = Namespace,
                Labels = new Dictionary<string, string>
                {
                    ["app"] = deployName,
                    ["cloudsoa/service"] = registration.ServiceName.ToLowerInvariant(),
                    ["cloudsoa/runtime"] = registration.Runtime
                }
            },
            Spec = new V1DeploymentSpec
            {
                Replicas = replicas,
                Selector = new V1LabelSelector
                {
                    MatchLabels = new Dictionary<string, string> { ["app"] = deployName }
                },
                Template = new V1PodTemplateSpec
                {
                    Metadata = new V1ObjectMeta
                    {
                        Labels = new Dictionary<string, string>
                        {
                            ["app"] = deployName,
                            ["cloudsoa/service"] = registration.ServiceName.ToLowerInvariant()
                        }
                    },
                    Spec = new V1PodSpec
                    {
                        NodeSelector = new Dictionary<string, string>
                        {
                            ["kubernetes.io/os"] = isWindows ? "windows" : "linux"
                        },
                        ImagePullSecrets = new List<V1LocalObjectReference>
                        {
                            new() { Name = "acr-secret" }
                        },
                        Containers = new List<V1Container>
                        {
                            new()
                            {
                                Name = "servicehost",
                                Image = image,
                                Ports = new List<V1ContainerPort>
                                {
                                    new() { ContainerPort = 5010 }
                                },
                                Env = BuildEnvVars(registration, blobConn, isWindows),
                                Resources = new V1ResourceRequirements
                                {
                                    Requests = new Dictionary<string, ResourceQuantity>
                                    {
                                        ["cpu"] = new(registration.Resources.CpuPerInstance),
                                        ["memory"] = new(registration.Resources.MemoryPerInstance)
                                    },
                                    Limits = new Dictionary<string, ResourceQuantity>
                                    {
                                        ["cpu"] = new("1"),
                                        ["memory"] = new("1Gi")
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        // Create or update
        try
        {
            var existing = await k8s.AppsV1.ReadNamespacedDeploymentAsync(deployName, Namespace, cancellationToken: ct);
            // Update existing
            existing.Spec = deployment.Spec;
            await k8s.AppsV1.ReplaceNamespacedDeploymentAsync(existing, deployName, Namespace, cancellationToken: ct);
            _logger.LogInformation("Updated deployment {Name} to {Replicas} replicas", deployName, replicas);
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            await k8s.AppsV1.CreateNamespacedDeploymentAsync(deployment, Namespace, cancellationToken: ct);
            _logger.LogInformation("Created deployment {Name} with {Replicas} replicas", deployName, replicas);
        }

        // Create Service if not exists
        await EnsureServiceAsync(k8s, deployName, ct);

        registration.Status = "deployed";
        registration.UpdatedAt = DateTime.UtcNow;
        return await _store.UpdateAsync(registration, ct);
    }

    public async Task<ServiceRegistration> ScaleServiceAsync(string registrationId, string serviceName, int replicas, CancellationToken ct = default)
    {
        var registration = await _store.GetByIdAsync(registrationId, serviceName, ct)
            ?? throw new KeyNotFoundException($"Registration '{registrationId}' not found.");

        if (registration.Status != "deployed")
            throw new InvalidOperationException($"Service must be deployed before scaling. Current status: {registration.Status}");

        var k8s = new Kubernetes(KubernetesClientConfiguration.InClusterConfig());
        var deployName = DeploymentName(registration);

        var patch = new V1Patch($"{{\"spec\":{{\"replicas\":{replicas}}}}}", V1Patch.PatchType.MergePatch);
        await k8s.AppsV1.PatchNamespacedDeploymentAsync(patch, deployName, Namespace, cancellationToken: ct);

        _logger.LogInformation("Scaled {Name} to {Replicas} replicas", deployName, replicas);

        registration.Resources.MinInstances = replicas;
        registration.UpdatedAt = DateTime.UtcNow;
        return await _store.UpdateAsync(registration, ct);
    }

    public async Task<ServiceRegistration> StopServiceAsync(string registrationId, string serviceName, CancellationToken ct = default)
    {
        var registration = await _store.GetByIdAsync(registrationId, serviceName, ct)
            ?? throw new KeyNotFoundException($"Registration '{registrationId}' not found.");

        var k8s = new Kubernetes(KubernetesClientConfiguration.InClusterConfig());
        var deployName = DeploymentName(registration);

        try
        {
            await k8s.AppsV1.DeleteNamespacedDeploymentAsync(deployName, Namespace, cancellationToken: ct);
            _logger.LogInformation("Deleted deployment {Name}", deployName);
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Deployment {Name} not found, marking as stopped", deployName);
        }

        registration.Status = "stopped";
        registration.UpdatedAt = DateTime.UtcNow;
        return await _store.UpdateAsync(registration, ct);
    }

    public async Task<object> GetDeploymentStatusAsync(string registrationId, string serviceName, CancellationToken ct = default)
    {
        var registration = await _store.GetByIdAsync(registrationId, serviceName, ct)
            ?? throw new KeyNotFoundException($"Registration '{registrationId}' not found.");

        var k8s = new Kubernetes(KubernetesClientConfiguration.InClusterConfig());
        var deployName = DeploymentName(registration);

        try
        {
            var deployment = await k8s.AppsV1.ReadNamespacedDeploymentAsync(deployName, Namespace, cancellationToken: ct);
            return new
            {
                serviceName = registration.ServiceName,
                status = registration.Status,
                desiredReplicas = deployment.Spec.Replicas,
                readyReplicas = deployment.Status?.ReadyReplicas ?? 0,
                availableReplicas = deployment.Status?.AvailableReplicas ?? 0,
                conditions = deployment.Status?.Conditions?.Select(c => new { c.Type, c.Status, c.Message })
            };
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new
            {
                serviceName = registration.ServiceName,
                status = registration.Status,
                desiredReplicas = 0,
                readyReplicas = 0,
                availableReplicas = 0,
                conditions = Array.Empty<object>()
            };
        }
    }

    private async Task EnsureServiceAsync(IKubernetes k8s, string deployName, CancellationToken ct)
    {
        try
        {
            await k8s.CoreV1.ReadNamespacedServiceAsync(deployName, Namespace, cancellationToken: ct);
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            var svc = new V1Service
            {
                Metadata = new V1ObjectMeta { Name = deployName, NamespaceProperty = Namespace },
                Spec = new V1ServiceSpec
                {
                    Selector = new Dictionary<string, string> { ["app"] = deployName },
                    Ports = new List<V1ServicePort>
                    {
                        new() { Name = "grpc", Port = 5010, TargetPort = 5010 }
                    }
                }
            };
            await k8s.CoreV1.CreateNamespacedServiceAsync(svc, Namespace, cancellationToken: ct);
            _logger.LogInformation("Created ClusterIP service {Name}", deployName);
        }
    }

    private static List<V1EnvVar> BuildEnvVars(ServiceRegistration reg, string blobConn, bool isWindows)
    {
        var servicesDir = isWindows ? @"C:\app\services" : "/app/services";
        var sep = isWindows ? @"\" : "/";
        var envVars = new List<V1EnvVar>
        {
            new() { Name = "SERVICE_DLL_PATH", Value = $"{servicesDir}{sep}{reg.AssemblyName}" },
            new() { Name = "BLOB_CONNECTION", Value = blobConn },
            new() { Name = "BLOB_PATH", Value = reg.BlobPath },
            new() { Name = "SERVICE_NAME", Value = reg.ServiceName },
            new() { Name = "SERVICE_RUNTIME", Value = reg.Runtime },
            new() { Name = "ASPNETCORE_URLS", Value = "http://+:5010" }
        };

        foreach (var kv in reg.Environment)
            envVars.Add(new V1EnvVar { Name = kv.Key, Value = kv.Value });

        return envVars;
    }

    private static string DeploymentName(ServiceRegistration reg) =>
        $"svc-{reg.ServiceName}".ToLowerInvariant().Replace('.', '-');
}
