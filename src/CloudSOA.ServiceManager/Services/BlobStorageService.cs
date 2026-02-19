using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace CloudSOA.ServiceManager.Services;

public class BlobStorageService
{
    private const string ContainerName = "service-packages";
    private readonly BlobContainerClient _container;
    private readonly ILogger<BlobStorageService> _logger;

    public BlobStorageService(BlobServiceClient blobServiceClient, ILogger<BlobStorageService> logger)
    {
        _container = blobServiceClient.GetBlobContainerClient(ContainerName);
        _logger = logger;
    }

    /// <summary>
    /// Ensure the blob container exists. Called once at startup.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
        _logger.LogInformation("Blob container '{Container}' is ready", ContainerName);
    }

    /// <summary>
    /// Upload a service DLL and optional config file to blob storage.
    /// Returns the base blob path "{serviceName}/{version}".
    /// </summary>
    public async Task<string> UploadServicePackageAsync(
        string serviceName,
        string version,
        string dllFileName,
        Stream dllStream,
        string? configFileName = null,
        Stream? configStream = null,
        CancellationToken ct = default)
    {
        var basePath = $"{serviceName}/{version}";

        // Upload DLL
        var dllBlobPath = $"{basePath}/{dllFileName}";
        var dllBlob = _container.GetBlobClient(dllBlobPath);
        await dllBlob.UploadAsync(dllStream, overwrite: true, cancellationToken: ct);
        _logger.LogInformation("Uploaded DLL to {BlobPath}", dllBlobPath);

        // Upload config if provided
        if (configStream is not null && configFileName is not null)
        {
            var configBlobPath = $"{basePath}/{configFileName}";
            var configBlob = _container.GetBlobClient(configBlobPath);
            await configBlob.UploadAsync(configStream, overwrite: true, cancellationToken: ct);
            _logger.LogInformation("Uploaded config to {BlobPath}", configBlobPath);
        }

        return basePath;
    }

    /// <summary>
    /// Download a service DLL (or any blob) by its full blob path.
    /// </summary>
    public async Task<Stream> DownloadServiceDllAsync(string blobPath, CancellationToken ct = default)
    {
        var blob = _container.GetBlobClient(blobPath);
        var response = await blob.DownloadStreamingAsync(cancellationToken: ct);
        return response.Value.Content;
    }

    /// <summary>
    /// Delete all blobs under the given base path (e.g. "{serviceName}/{version}").
    /// </summary>
    public async Task DeleteServicePackageAsync(string blobPath, CancellationToken ct = default)
    {
        await foreach (var item in _container.GetBlobsAsync(BlobTraits.None, BlobStates.None, blobPath, ct))
        {
            await _container.DeleteBlobIfExistsAsync(item.Name, cancellationToken: ct);
            _logger.LogInformation("Deleted blob {BlobName}", item.Name);
        }
    }

    /// <summary>
    /// List all uploaded versions for a given service name by inspecting blob prefixes.
    /// </summary>
    public async Task<List<string>> ListVersionsAsync(string serviceName, CancellationToken ct = default)
    {
        var versions = new HashSet<string>();
        var prefix = $"{serviceName}/";

        await foreach (var item in _container.GetBlobsByHierarchyAsync(
            BlobTraits.None, BlobStates.None, "/", prefix, ct))
        {
            if (item.IsPrefix && item.Prefix is not null)
            {
                // Prefix looks like "serviceName/version/" — extract version
                var version = item.Prefix.TrimEnd('/').Split('/').Last();
                versions.Add(version);
            }
        }

        return versions.OrderDescending().ToList();
    }
}
