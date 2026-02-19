using CloudSOA.Common.Enums;

namespace CloudSOA.Client;

/// <summary>
/// HPC Pack-compatible Session class.
/// Wraps CloudSession with synchronous API matching Microsoft.Hpc.Scheduler.Session.Session.
/// </summary>
public class Session : IDisposable
{
    internal CloudSession InnerSession { get; }

    internal Session(CloudSession innerSession)
    {
        InnerSession = innerSession;
    }

    public string Id => InnerSession.SessionId;
    public string ServiceName => InnerSession.ServiceName;
    public string EndpointReference => InnerSession.BrokerEndpoint;

    /// <summary>
    /// Create a new session synchronously. Compatible with HPC Pack Session.CreateSession().
    /// </summary>
    public static Session CreateSession(SessionStartInfo info)
    {
        var innerSession = CloudSession.CreateSessionAsync(info).GetAwaiter().GetResult();
        return new Session(innerSession);
    }

    /// <summary>
    /// Create a new session asynchronously.
    /// </summary>
    public static async Task<Session> CreateSessionAsync(SessionStartInfo info, CancellationToken ct = default)
    {
        var innerSession = await CloudSession.CreateSessionAsync(info, ct);
        return new Session(innerSession);
    }

    public void Close() => InnerSession.CloseAsync().GetAwaiter().GetResult();

    public Task CloseAsync(CancellationToken ct = default) => InnerSession.CloseAsync(ct);

    public void Dispose() => InnerSession.Dispose();
}

/// <summary>
/// HPC Pack-compatible DurableSession.
/// In CloudSOA, durability is handled by the Broker (session state in CosmosDB).
/// </summary>
public class DurableSession : Session
{
    private DurableSession(CloudSession inner) : base(inner) { }

    /// <summary>
    /// Create a durable session. Sets SessionType to Durable before creating.
    /// </summary>
    public new static DurableSession CreateSession(SessionStartInfo info)
    {
        info.SessionType = SessionType.Durable;
        var innerSession = CloudSession.CreateSessionAsync(info).GetAwaiter().GetResult();
        return new DurableSession(innerSession);
    }

    /// <summary>
    /// Create a durable session asynchronously.
    /// </summary>
    public new static async Task<DurableSession> CreateSessionAsync(
        SessionStartInfo info, CancellationToken ct = default)
    {
        info.SessionType = SessionType.Durable;
        var innerSession = await CloudSession.CreateSessionAsync(info, ct);
        return new DurableSession(innerSession);
    }
}
