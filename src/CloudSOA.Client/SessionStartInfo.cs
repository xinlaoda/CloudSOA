using CloudSOA.Common.Enums;

namespace CloudSOA.Client;

/// <summary>
/// Session 启动配置 — 兼容 HPC Pack SessionStartInfo 语义
/// </summary>
public class SessionStartInfo
{
    public string BrokerEndpoint { get; }
    public string ServiceName { get; }
    public SessionType SessionType { get; set; } = SessionType.Interactive;
    public int MinimumUnits { get; set; } = 1;
    public int MaximumUnits { get; set; } = 1;
    public TransportScheme TransportScheme { get; set; } = TransportScheme.Grpc;
    public TimeSpan? SessionIdleTimeout { get; set; }
    public TimeSpan? ClientIdleTimeout { get; set; }

    public SessionStartInfo(string brokerEndpoint, string serviceName)
    {
        BrokerEndpoint = brokerEndpoint;
        ServiceName = serviceName;
    }
}
