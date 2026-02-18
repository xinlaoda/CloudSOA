namespace CloudSOA.ServiceHost.Loader;

/// <summary>
/// 用户服务接口 — 用户实现此接口并打包为 DLL
/// </summary>
public interface ISOAService
{
    string ServiceName { get; }
    IReadOnlyList<string> SupportedActions { get; }
    Task<byte[]> ExecuteAsync(string action, byte[] payload, CancellationToken ct = default);
}
