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

    public async Task AddAsync(BrokerResponse response, CancellationToken ct = default)
    {
        var key = ListPrefix + response.SessionId;
        var json = JsonSerializer.Serialize(response, JsonOpts);
        await Db.ListRightPushAsync(key, json);
        await Db.KeyExpireAsync(key, TimeSpan.FromHours(1));
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
