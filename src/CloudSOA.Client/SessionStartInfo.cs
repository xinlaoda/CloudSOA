using CloudSOA.Common.Enums;
using System.Security.Cryptography.X509Certificates;

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

    /// <summary>Accept self-signed or untrusted server certificates (development only).</summary>
    public bool AcceptUntrustedCertificates { get; set; }

    /// <summary>Client certificate for mutual TLS authentication.</summary>
    public X509Certificate2? ClientCertificate { get; set; }

    /// <summary>Custom properties (e.g., Authorization header).</summary>
    public Dictionary<string, string> Properties { get; } = new();

    /// <summary>Bearer token for JWT authentication. Sets Authorization header automatically.</summary>
    public string? BearerToken { get; set; }

    /// <summary>API Key for X-Api-Key header authentication.</summary>
    public string? ApiKey { get; set; }

    // HPC Pack compatibility
    public string? Username { get; set; }
    public string? Password { get; set; }
    public bool Secure { get; set; } = true;

    public SessionStartInfo(string brokerEndpoint, string serviceName)
    {
        BrokerEndpoint = brokerEndpoint;
        ServiceName = serviceName;
    }
}
