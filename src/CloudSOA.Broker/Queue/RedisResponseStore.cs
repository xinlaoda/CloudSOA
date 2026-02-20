using System.Text.Json;
using CloudSOA.Common.Interfaces;
using CloudSOA.Common.Models;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace CloudSOA.Broker.Queue;

/// <summary>
/// Redis 实现的响应缓存，客户端拉取后删除
/// </summary>
public class RedisResponseStore : IResponseStore
{
    private const string ListPrefix = "cloudsoa:responses:";
    private const string CounterPrefix = "cloudsoa:resp-count:";
    private const string TtlPrefix = "cloudsoa:resp-ttl:";
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisResponseStore> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public RedisResponseStore(IConnectionMultiplexer redis, ILogger<RedisResponseStore> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    private IDatabase Db => _redis.GetDatabase();

    /// <summary>Set response TTL for a session (call once after session creation).</summary>
    public async Task SetSessionResponseTtlAsync(string sessionId, TimeSpan ttl)
    {
        await Db.StringSetAsync(TtlPrefix + sessionId, ttl.TotalSeconds.ToString(), ttl + TimeSpan.FromMinutes(5));
    }

    private async Task<TimeSpan> GetResponseTtlAsync(string sessionId)
    {
        var val = await Db.StringGetAsync(TtlPrefix + sessionId);
        if (val.HasValue && double.TryParse(val!, out var seconds))
            return TimeSpan.FromSeconds(seconds);
        return TimeSpan.FromHours(1); // default for interactive
    }

    public async Task AddAsync(BrokerResponse response, CancellationToken ct = default)
    {
        var key = ListPrefix + response.SessionId;
        var json = JsonSerializer.Serialize(response, JsonOpts);
        await Db.ListRightPushAsync(key, json);
        var ttl = await GetResponseTtlAsync(response.SessionId);
        await Db.KeyExpireAsync(key, ttl);
        await Db.StringIncrementAsync(CounterPrefix + response.SessionId);

        _logger.LogDebug("Stored response {MessageId} for session {SessionId}",
            response.MessageId, response.SessionId);
    }

    public async Task<IReadOnlyList<BrokerResponse>> FetchAsync(
        string sessionId, int maxCount = 100, CancellationToken ct = default)
    {
        var key = ListPrefix + sessionId;
        var results = new List<BrokerResponse>();

        for (int i = 0; i < maxCount; i++)
        {
            var json = await Db.ListLeftPopAsync(key);
            if (json.IsNullOrEmpty)
                break;

            var resp = JsonSerializer.Deserialize<BrokerResponse>(json!, JsonOpts);
            if (resp != null)
                results.Add(resp);
        }

        return results;
    }

    public async Task<long> GetCountAsync(string sessionId, CancellationToken ct = default)
    {
        return await Db.ListLengthAsync(ListPrefix + sessionId);
    }
}
