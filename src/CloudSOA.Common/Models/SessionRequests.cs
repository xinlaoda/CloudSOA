using CloudSOA.Common.Enums;

namespace CloudSOA.Common.Models;

public class CreateSessionRequest
{
    public string ServiceName { get; set; } = string.Empty;
    public SessionType SessionType { get; set; } = SessionType.Interactive;
    public int MinimumUnits { get; set; } = 1;
    public int MaximumUnits { get; set; } = 1;
    public TransportScheme TransportScheme { get; set; } = TransportScheme.Grpc;
    public TimeSpan? SessionIdleTimeout { get; set; }
    public TimeSpan? ClientIdleTimeout { get; set; }
    public Dictionary<string, string>? Properties { get; set; }
}

public class SessionStatusResponse
{
    public string SessionId { get; set; } = string.Empty;
    public SessionState State { get; set; }
    public int CurrentUnits { get; set; }
    public long PendingRequests { get; set; }
    public long ProcessedRequests { get; set; }
    public long FailedRequests { get; set; }
    public DateTime? LastAccessedAt { get; set; }
}
