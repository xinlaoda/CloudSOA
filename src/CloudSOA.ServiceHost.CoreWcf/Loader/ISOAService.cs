namespace CloudSOA.ServiceHost.CoreWcf.Loader;

/// <summary>
/// User service interface — same contract as the native ServiceHost.
/// </summary>
public interface ISOAService
{
    string ServiceName { get; }
    IReadOnlyList<string> SupportedActions { get; }
    Task<byte[]> ExecuteAsync(string action, byte[] payload, CancellationToken ct = default);
}
