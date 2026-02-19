using CloudSOA.Common.Enums;
using CloudSOA.Common.Exceptions;
using CloudSOA.Common.Interfaces;
using CloudSOA.Common.Models;
using Microsoft.Extensions.Logging;

namespace CloudSOA.Broker.Services;

public class SessionManagerService : ISessionManager
{
    private readonly ISessionStore _store;
    private readonly ILogger<SessionManagerService> _logger;

    private static readonly TimeSpan DefaultSessionIdleTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan DefaultClientIdleTimeout = TimeSpan.FromMinutes(5);

    public SessionManagerService(ISessionStore store, ILogger<SessionManagerService> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task<SessionInfo> CreateSessionAsync(CreateSessionRequest request, CancellationToken ct = default)
    {
        var session = new SessionInfo
        {
            SessionId = Guid.NewGuid().ToString("N"),
            ServiceName = request.ServiceName,
            State = SessionState.Active,
            SessionType = request.SessionType,
            MinimumUnits = request.MinimumUnits,
            MaximumUnits = request.MaximumUnits,
            TransportScheme = request.TransportScheme,
            SessionIdleTimeout = request.SessionIdleTimeout ?? DefaultSessionIdleTimeout,
            ClientIdleTimeout = request.ClientIdleTimeout ?? DefaultClientIdleTimeout,
            CreatedAt = DateTime.UtcNow,
            LastAccessedAt = DateTime.UtcNow,
            Properties = request.Properties ?? new()
        };

        await _store.SaveAsync(session, ct);
        _logger.LogInformation("Session created: {SessionId} for service {ServiceName}",
            session.SessionId, session.ServiceName);

        Metrics.BrokerMetrics.SessionsCreated.WithLabels(session.ServiceName).Inc();
        Metrics.BrokerMetrics.ActiveSessions.Inc();

        return session;
    }

    public async Task<SessionInfo> GetSessionAsync(string sessionId, CancellationToken ct = default)
    {
        var session = await _store.GetAsync(sessionId, ct)
            ?? throw new SessionNotFoundException(sessionId);

        await _store.RefreshAccessTimeAsync(sessionId, ct);
        return session;
    }

    public async Task<SessionInfo> AttachSessionAsync(string sessionId, string? clientId = null, CancellationToken ct = default)
    {
        var session = await _store.GetAsync(sessionId, ct)
            ?? throw new SessionNotFoundException(sessionId);

        if (session.State != SessionState.Active)
            throw new SessionStateException(sessionId,
                $"Cannot attach to session in state '{session.State}'.");

        session.ClientId = clientId;
        session.LastAccessedAt = DateTime.UtcNow;
        await _store.SaveAsync(session, ct);

        _logger.LogInformation("Client {ClientId} attached to session {SessionId}",
            clientId, sessionId);

        return session;
    }

    public async Task CloseSessionAsync(string sessionId, CancellationToken ct = default)
    {
        var session = await _store.GetAsync(sessionId, ct)
            ?? throw new SessionNotFoundException(sessionId);

        if (session.State == SessionState.Closed)
            return;

        session.State = SessionState.Closing;
        await _store.SaveAsync(session, ct);

        // TODO: Phase 2 - drain pending requests before final close

        session.State = SessionState.Closed;
        await _store.SaveAsync(session, ct);

        Metrics.BrokerMetrics.SessionsClosed.Inc();
        Metrics.BrokerMetrics.ActiveSessions.Dec();

        _logger.LogInformation("Session closed: {SessionId}", sessionId);
    }

    public async Task<SessionStatusResponse> GetSessionStatusAsync(string sessionId, CancellationToken ct = default)
    {
        var session = await _store.GetAsync(sessionId, ct)
            ?? throw new SessionNotFoundException(sessionId);

        return new SessionStatusResponse
        {
            SessionId = session.SessionId,
            State = session.State,
            CurrentUnits = 0, // TODO: Phase 3 - count running Service Host Pods
            PendingRequests = 0, // TODO: Phase 2 - query queue depth
            ProcessedRequests = 0,
            FailedRequests = 0,
            LastAccessedAt = session.LastAccessedAt
        };
    }
}
