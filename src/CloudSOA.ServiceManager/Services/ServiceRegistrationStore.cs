using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using CloudSOA.ServiceManager.Models;

namespace CloudSOA.ServiceManager.Services;

public class ServiceRegistrationStore
{
    private const string DatabaseId = "cloudsoa";
    private const string ContainerId = "service-registrations";
    private readonly Container _container;
    private readonly ILogger<ServiceRegistrationStore> _logger;

    public ServiceRegistrationStore(CosmosClient cosmosClient, ILogger<ServiceRegistrationStore> logger)
    {
        _container = cosmosClient.GetContainer(DatabaseId, ContainerId);
        _logger = logger;
    }

    /// <summary>
    /// Ensure the Cosmos DB database and container exist. Called once at startup.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var db = (await _container.Database.ReadAsync(cancellationToken: ct)).Database;
        await db.CreateContainerIfNotExistsAsync(
            new ContainerProperties(ContainerId, "/serviceName"),
            cancellationToken: ct);
        _logger.LogInformation("CosmosDB container '{Container}' is ready", ContainerId);
    }

    public async Task<ServiceRegistration> CreateAsync(ServiceRegistration registration, CancellationToken ct = default)
    {
        registration.Id = Guid.NewGuid().ToString();
        registration.CreatedAt = DateTime.UtcNow;
        registration.UpdatedAt = DateTime.UtcNow;

        var response = await _container.CreateItemAsync(
            registration,
            new PartitionKey(registration.ServiceName),
            cancellationToken: ct);

        _logger.LogInformation("Created registration {Id} for {ServiceName} v{Version}",
            response.Resource.Id, response.Resource.ServiceName, response.Resource.Version);
        return response.Resource;
    }

    public async Task<ServiceRegistration?> GetByIdAsync(string id, string serviceName, CancellationToken ct = default)
    {
        try
        {
            var response = await _container.ReadItemAsync<ServiceRegistration>(
                id, new PartitionKey(serviceName), cancellationToken: ct);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>
    /// Get the latest version of a service by name (most recently created).
    /// </summary>
    public async Task<ServiceRegistration?> GetByNameAsync(string serviceName, CancellationToken ct = default)
    {
        var query = _container.GetItemLinqQueryable<ServiceRegistration>()
            .Where(r => r.ServiceName == serviceName)
            .OrderByDescending(r => r.CreatedAt)
            .Take(1)
            .ToFeedIterator();

        while (query.HasMoreResults)
        {
            var response = await query.ReadNextAsync(ct);
            return response.FirstOrDefault();
        }

        return null;
    }

    /// <summary>
    /// List all registrations, optionally filtered by status.
    /// </summary>
    public async Task<List<ServiceRegistration>> ListAsync(string? statusFilter = null, int maxItems = 100, CancellationToken ct = default)
    {
        var queryable = _container.GetItemLinqQueryable<ServiceRegistration>().AsQueryable();

        if (!string.IsNullOrEmpty(statusFilter))
            queryable = queryable.Where(r => r.Status == statusFilter);

        queryable = queryable.OrderByDescending(r => r.CreatedAt).Take(maxItems);

        var iterator = queryable.ToFeedIterator();
        var results = new List<ServiceRegistration>();

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(ct);
            results.AddRange(response);
        }

        return results;
    }

    /// <summary>
    /// List all registrations for a specific service name (all versions).
    /// </summary>
    public async Task<List<ServiceRegistration>> ListByNameAsync(string serviceName, CancellationToken ct = default)
    {
        var iterator = _container.GetItemLinqQueryable<ServiceRegistration>()
            .Where(r => r.ServiceName == serviceName)
            .OrderByDescending(r => r.CreatedAt)
            .ToFeedIterator();

        var results = new List<ServiceRegistration>();
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(ct);
            results.AddRange(response);
        }

        return results;
    }

    public async Task<ServiceRegistration> UpdateAsync(ServiceRegistration registration, CancellationToken ct = default)
    {
        registration.UpdatedAt = DateTime.UtcNow;

        var response = await _container.ReplaceItemAsync(
            registration,
            registration.Id,
            new PartitionKey(registration.ServiceName),
            cancellationToken: ct);

        _logger.LogInformation("Updated registration {Id}", response.Resource.Id);
        return response.Resource;
    }

    public async Task<bool> DeleteAsync(string id, string serviceName, CancellationToken ct = default)
    {
        try
        {
            await _container.DeleteItemAsync<ServiceRegistration>(
                id, new PartitionKey(serviceName), cancellationToken: ct);
            _logger.LogInformation("Deleted registration {Id}", id);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }
}
