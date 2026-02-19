using CloudSOA.ServiceManager.Models;

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

    public ServiceDeploymentService(
        ServiceRegistrationStore store,
        IConfiguration configuration,
        ILogger<ServiceDeploymentService> logger)
    {
        _store = store;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Deploy (or redeploy) a service by creating/updating its Kubernetes Deployment.
    /// </summary>
    public async Task<ServiceRegistration> DeployServiceAsync(string registrationId, string serviceName, CancellationToken ct = default)
    {
        var registration = await _store.GetByIdAsync(registrationId, serviceName, ct)
            ?? throw new KeyNotFoundException($"Registration '{registrationId}' not found.");

        // TODO: Use the Kubernetes client library (KubernetesClient) to create/update
        // a Deployment for this service. The deployment should:
        //
        // 1. Use the CloudSOA.ServiceHost image as the pod container image.
        // 2. Set environment variables:
        //    - SERVICE_DLL_PATH = the assembly name to load
        //    - BLOB_CONNECTION = Azure Blob connection string from config
        //    - BLOB_PATH = registration.BlobPath
        //    - SERVICE_RUNTIME = registration.Runtime
        //    - Plus any registration.Environment entries
        // 3. Choose the node pool based on Runtime:
        //    - "wcf-netfx"   → Windows node pool (nodeSelector: kubernetes.io/os=windows)
        //    - "native-net8" → Linux node pool   (nodeSelector: kubernetes.io/os=linux)
        // 4. Set resource requests/limits from registration.Resources.
        // 5. Set replicas to registration.Resources.MinInstances (or 1 if 0).
        //
        // var k8sClient = new Kubernetes(KubernetesClientConfiguration.InClusterConfig());
        // var deployment = BuildDeploymentManifest(registration);
        // await k8sClient.CreateNamespacedDeploymentAsync(deployment, "cloudsoa");

        _logger.LogInformation(
            "Deployed service {ServiceName} v{Version} (runtime={Runtime})",
            registration.ServiceName, registration.Version, registration.Runtime);

        registration.Status = "deployed";
        return await _store.UpdateAsync(registration, ct);
    }

    /// <summary>
    /// Scale a deployed service to the specified number of replicas.
    /// </summary>
    public async Task<ServiceRegistration> ScaleServiceAsync(string registrationId, string serviceName, int replicas, CancellationToken ct = default)
    {
        var registration = await _store.GetByIdAsync(registrationId, serviceName, ct)
            ?? throw new KeyNotFoundException($"Registration '{registrationId}' not found.");

        if (registration.Status != "deployed")
            throw new InvalidOperationException($"Service must be deployed before scaling. Current status: {registration.Status}");

        // TODO: Use Kubernetes client to patch the Deployment replica count:
        //
        // var patch = new V1Patch($"{{\"spec\":{{\"replicas\":{replicas}}}}}",
        //     V1Patch.PatchType.MergePatch);
        // await k8sClient.PatchNamespacedDeploymentAsync(
        //     patch, DeploymentName(registration), "cloudsoa");

        _logger.LogInformation(
            "Scaled service {ServiceName} to {Replicas} replicas",
            registration.ServiceName, replicas);

        registration.Resources.MinInstances = replicas;
        return await _store.UpdateAsync(registration, ct);
    }

    /// <summary>
    /// Stop a deployed service by scaling to 0 replicas.
    /// </summary>
    public async Task<ServiceRegistration> StopServiceAsync(string registrationId, string serviceName, CancellationToken ct = default)
    {
        var registration = await _store.GetByIdAsync(registrationId, serviceName, ct)
            ?? throw new KeyNotFoundException($"Registration '{registrationId}' not found.");

        // TODO: Scale the Kubernetes Deployment to 0 replicas:
        //
        // var patch = new V1Patch("{\"spec\":{\"replicas\":0}}",
        //     V1Patch.PatchType.MergePatch);
        // await k8sClient.PatchNamespacedDeploymentAsync(
        //     patch, DeploymentName(registration), "cloudsoa");

        _logger.LogInformation("Stopped service {ServiceName}", registration.ServiceName);

        registration.Status = "stopped";
        return await _store.UpdateAsync(registration, ct);
    }

    /// <summary>
    /// Get the current deployment status from Kubernetes.
    /// </summary>
    public async Task<object> GetDeploymentStatusAsync(string registrationId, string serviceName, CancellationToken ct = default)
    {
        var registration = await _store.GetByIdAsync(registrationId, serviceName, ct)
            ?? throw new KeyNotFoundException($"Registration '{registrationId}' not found.");

        // TODO: Query the Kubernetes API for the Deployment status:
        //
        // var deployment = await k8sClient.ReadNamespacedDeploymentAsync(
        //     DeploymentName(registration), "cloudsoa");
        // return new {
        //     readyReplicas = deployment.Status.ReadyReplicas,
        //     availableReplicas = deployment.Status.AvailableReplicas,
        //     conditions = deployment.Status.Conditions
        // };

        return new
        {
            registrationId = registration.Id,
            serviceName = registration.ServiceName,
            status = registration.Status,
            runtime = registration.Runtime,
            replicas = registration.Resources.MinInstances,
            message = "Kubernetes status not yet implemented"
        };
    }

    private static string DeploymentName(ServiceRegistration reg) =>
        $"svc-{reg.ServiceName}-{reg.Version}".ToLowerInvariant().Replace('.', '-');
}
