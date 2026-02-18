using System.Text.Json;
using CloudSOA.Common.Enums;
using CloudSOA.Common.Interfaces;
using CloudSOA.Common.Models;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace CloudSOA.Broker.Storage;

public class RedisSessionStore : ISessionStore
{
    private const string KeyPrefix = "cloudsoa:session:";
    private const string IndexKey = "cloudsoa:sessions";
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisSessionStore> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public RedisSessionStore(IConnectionMultiplexer redis, ILogger<RedisSessionStore> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    private IDatabase Db => _redis.GetDatabase();

    public async Task<SessionInfo?> GetAsync(string sessionId, CancellationToken ct = default)
    {
        var data = await Db.StringGetAsync(KeyPrefix + sessionId);
        if (data.IsNullOrEmpty) return null;
        return JsonSerializer.Deserialize<SessionInfo>(data!, JsonOptions);
    }

    public async Task SaveAsync(SessionInfo session, CancellationToken ct = default)
    {
        var key = KeyPrefix + session.SessionId;
        var json = JsonSerializer.Serialize(session, JsonOptions);
        var ttl = session.SessionIdleTimeout + TimeSpan.FromMinutes(5); // buffer

        var tx = Db.CreateTransaction();
        _ = tx.StringSetAsync(key, json, ttl);
        _ = tx.SetAddAsync(IndexKey, session.SessionId);
        await tx.ExecuteAsync();

        _logger.LogDebug("Saved session {SessionId}, TTL={TTL}", session.SessionId, ttl);
    }

    public async Task DeleteAsync(string sessionId, CancellationToken ct = default)
    {
        var tx = Db.CreateTransaction();
        _ = tx.KeyDeleteAsync(KeyPrefix + sessionId);
        _ = tx.SetRemoveAsync(IndexKey, sessionId);
        await tx.ExecuteAsync();

        _logger.LogDebug("Deleted session {SessionId}", sessionId);
    }

    public async Task<IReadOnlyList<SessionInfo>> ListAsync(CancellationToken ct = default)
    {
        var ids = await Db.SetMembersAsync(IndexKey);
        var sessions = new List<SessionInfo>();

        foreach (var id in ids)
        {
            var session = await GetAsync(id!, ct);
            if (session != null)
                sessions.Add(session);
            else
                await Db.SetRemoveAsync(IndexKey, id); // cleanup stale entry
        }

        return sessions;
    }

    public async Task RefreshAccessTimeAsync(string sessionId, CancellationToken ct = default)
    {
        var session = await GetAsync(sessionId, ct);
        if (session == null) return;

        session.LastAccessedAt = DateTime.UtcNow;
        await SaveAsync(session, ct);
    }
}
