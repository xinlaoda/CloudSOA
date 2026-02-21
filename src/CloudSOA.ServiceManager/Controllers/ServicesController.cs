using System.Xml.Serialization;
using CloudSOA.ServiceManager.Models;
using CloudSOA.ServiceManager.Services;
using Microsoft.AspNetCore.Mvc;

namespace CloudSOA.ServiceManager.Controllers;

[ApiController]
[Route("api/v1/services")]
public class ServicesController : ControllerBase
{
    private readonly BlobStorageService _blob;
    private readonly ServiceRegistrationStore _store;
    private readonly ServiceDeploymentService _deployer;
    private readonly ILogger<ServicesController> _logger;

    public ServicesController(
        BlobStorageService blob,
        ServiceRegistrationStore store,
        ServiceDeploymentService deployer,
        ILogger<ServicesController> logger)
    {
        _blob = blob;
        _store = store;
        _deployer = deployer;
        _logger = logger;
    }

    /// <summary>
    /// Register a new service. Expects a multipart form with "dll" and optional "config" (XML) files.
    /// </summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> RegisterService(
        IFormFile dll,
        IFormFile? config = null,
        List<IFormFile>? dependencies = null,
        CancellationToken ct = default)
    {
        if (dll is null || dll.Length == 0)
            return BadRequest(new { message = "A DLL file is required." });

        ServiceRegistration registration;

        // If an XML config is provided, parse it for metadata
        if (config is not null && config.Length > 0)
        {
            var serializer = new XmlSerializer(typeof(ServiceConfigXml));
            await using var configStream = config.OpenReadStream();
            var xmlConfig = serializer.Deserialize(configStream) as ServiceConfigXml;
            if (xmlConfig is null)
                return BadRequest(new { message = "Invalid XML configuration file." });

            registration = xmlConfig.ToRegistration();
        }
        else
        {
            // Derive minimal registration from the DLL file name
            registration = new ServiceRegistration
            {
                AssemblyName = dll.FileName,
                ServiceName = Path.GetFileNameWithoutExtension(dll.FileName),
                Version = "1.0.0"
            };
        }

        // Upload to Blob Storage
        await using var dllStream = dll.OpenReadStream();
        Stream? cfgStream = config is not null ? config.OpenReadStream() : null;
        try
        {
            var blobPath = await _blob.UploadServicePackageAsync(
                registration.ServiceName,
                registration.Version,
                dll.FileName,
                dllStream,
                config?.FileName,
                cfgStream,
                ct);

            // Upload dependency DLLs
            if (dependencies is not null)
            {
                foreach (var dep in dependencies.Where(d => d.Length > 0))
                {
                    await using var depStream = dep.OpenReadStream();
                    await _blob.UploadDependencyAsync(blobPath, dep.FileName, depStream, ct);
                    registration.Dependencies.Add(dep.FileName);
                }
            }

            registration.BlobPath = blobPath;
        }
        finally
        {
            if (cfgStream is not null)
                await cfgStream.DisposeAsync();
        }

        // Persist metadata
        var created = await _store.CreateAsync(registration, ct);

        _logger.LogInformation("Registered service {ServiceName} v{Version}",
            created.ServiceName, created.Version);

        return CreatedAtAction(nameof(GetService), new { name = created.ServiceName }, created);
    }

    /// <summary>List all registered services.</summary>
    [HttpGet]
    public async Task<IActionResult> ListServices(
        [FromQuery] string? status = null,
        [FromQuery] int maxItems = 100,
        CancellationToken ct = default)
    {
        var services = await _store.ListAsync(status, maxItems, ct);
        return Ok(services);
    }

    /// <summary>Get the latest registration for a service by name.</summary>
    [HttpGet("{name}")]
    public async Task<IActionResult> GetService(string name, CancellationToken ct = default)
    {
        var registration = await _store.GetByNameAsync(name, ct);
        if (registration is null)
            return NotFound(new { message = $"Service '{name}' not found." });
        return Ok(registration);
    }

    /// <summary>Update service metadata (not the DLL — re-register for that).</summary>
    [HttpPut("{name}")]
    public async Task<IActionResult> UpdateService(
        string name,
        [FromBody] ServiceRegistrationUpdateDto dto,
        CancellationToken ct = default)
    {
        var registration = await _store.GetByNameAsync(name, ct);
        if (registration is null)
            return NotFound(new { message = $"Service '{name}' not found." });

        if (dto.Resources is not null)
            registration.Resources = dto.Resources;
        if (dto.Environment is not null)
            registration.Environment = dto.Environment;
        if (dto.Runtime is not null)
            registration.Runtime = dto.Runtime;

        var updated = await _store.UpdateAsync(registration, ct);
        return Ok(updated);
    }

    /// <summary>Deregister a service and delete its blobs.</summary>
    [HttpDelete("{name}")]
    public async Task<IActionResult> DeleteService(string name, CancellationToken ct = default)
    {
        var registration = await _store.GetByNameAsync(name, ct);
        if (registration is null)
            return NotFound(new { message = $"Service '{name}' not found." });

        await _blob.DeleteServicePackageAsync(registration.BlobPath, ct);
        await _store.DeleteAsync(registration.Id, registration.ServiceName, ct);

        return NoContent();
    }

    /// <summary>Deploy / start a registered service.</summary>
    [HttpPost("{name}/deploy")]
    public async Task<IActionResult> DeployService(string name, CancellationToken ct = default)
    {
        var registration = await _store.GetByNameAsync(name, ct);
        if (registration is null)
            return NotFound(new { message = $"Service '{name}' not found." });

        var deployed = await _deployer.DeployServiceAsync(registration.Id, registration.ServiceName, ct);
        return Ok(deployed);
    }

    /// <summary>Stop a deployed service (scale to 0).</summary>
    [HttpPost("{name}/stop")]
    public async Task<IActionResult> StopService(string name, CancellationToken ct = default)
    {
        var registration = await _store.GetByNameAsync(name, ct);
        if (registration is null)
            return NotFound(new { message = $"Service '{name}' not found." });

        var stopped = await _deployer.StopServiceAsync(registration.Id, registration.ServiceName, ct);
        return Ok(stopped);
    }

    /// <summary>Scale a deployed service to the requested number of replicas.</summary>
    [HttpPost("{name}/scale")]
    public async Task<IActionResult> ScaleService(
        string name,
        [FromBody] ScaleRequestDto dto,
        CancellationToken ct = default)
    {
        var registration = await _store.GetByNameAsync(name, ct);
        if (registration is null)
            return NotFound(new { message = $"Service '{name}' not found." });

        var scaled = await _deployer.ScaleServiceAsync(registration.Id, registration.ServiceName, dto.Replicas, ct);
        return Ok(scaled);
    }

    /// <summary>List all versions uploaded for a service.</summary>
    [HttpGet("{name}/versions")]
    public async Task<IActionResult> ListVersions(string name, CancellationToken ct = default)
    {
        var blobVersions = await _blob.ListVersionsAsync(name, ct);
        var registrations = await _store.ListByNameAsync(name, ct);

        return Ok(new
        {
            serviceName = name,
            versions = registrations.Select(r => new
            {
                r.Version,
                r.Status,
                r.CreatedAt,
                r.Id
            }),
            blobVersions
        });
    }

    /// <summary>Get the current deployment status of a service.</summary>
    [HttpGet("{name}/status")]
    public async Task<IActionResult> GetStatus(string name, CancellationToken ct = default)
    {
        var registration = await _store.GetByNameAsync(name, ct);
        if (registration is null)
            return NotFound(new { message = $"Service '{name}' not found." });

        var status = await _deployer.GetDeploymentStatusAsync(registration.Id, registration.ServiceName, ct);
        return Ok(status);
    }
}

public class ServiceRegistrationUpdateDto
{
    public ServiceResources? Resources { get; set; }
    public Dictionary<string, string>? Environment { get; set; }
    public string? Runtime { get; set; }
}

public class ScaleRequestDto
{
    public int Replicas { get; set; }
}
