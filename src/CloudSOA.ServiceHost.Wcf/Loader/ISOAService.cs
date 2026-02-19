namespace CloudSOA.ServiceHost.Wcf.Loader;

/// <summary>
/// Local copy of the SOA service interface (avoids circular dependency with ServiceHost)
/// </summary>
public interface ISOAService
{
    string ServiceName { get; }
    IReadOnlyList<string> SupportedActions { get; }
    Task<byte[]> ExecuteAsync(string action, byte[] payload, CancellationToken ct = default);
}
