using CloudSOA.Common.Enums;

namespace CloudSOA.Common.Models;

public class SessionInfo
{
    public string SessionId { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public SessionState State { get; set; } = SessionState.Creating;
    public SessionType SessionType { get; set; } = SessionType.Interactive;
    public int MinimumUnits { get; set; } = 1;
    public int MaximumUnits { get; set; } = 1;
    public string? ClientId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastAccessedAt { get; set; }
    public TimeSpan SessionIdleTimeout { get; set; } = TimeSpan.FromMinutes(30);
    public TimeSpan ClientIdleTimeout { get; set; } = TimeSpan.FromMinutes(5);
    public TransportScheme TransportScheme { get; set; } = TransportScheme.Grpc;
    public string? BrokerEndpoint { get; set; }
    public Dictionary<string, string> Properties { get; set; } = new();
}
