using System.Text.Json;
using CloudSOA.Common.Interfaces;
using CloudSOA.Common.Models;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace CloudSOA.Broker.Queue;

/// <summary>
/// Redis Streams 实现的请求队列（适用于 Interactive Session）
/// </summary>
public class RedisRequestQueue : IRequestQueue
{
    private const string StreamPrefix = "cloudsoa:queue:";
    private const string DeadLetterPrefix = "cloudsoa:dlq:";
    private const string ConsumerGroup = "dispatchers";
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisRequestQueue> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public RedisRequestQueue(IConnectionMultiplexer redis, ILogger<RedisRequestQueue> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    private IDatabase Db => _redis.GetDatabase();
    private static string StreamKey(string sessionId) => StreamPrefix + sessionId;

    public async Task EnqueueAsync(BrokerRequest request, CancellationToken ct = default)
    {
        var key = StreamKey(request.SessionId);
        await EnsureConsumerGroupAsync(key);

        var json = JsonSerializer.Serialize(request, JsonOpts);
        await Db.StreamAddAsync(key, new NameValueEntry[]
        {
            new("messageId", request.MessageId),
            new("data", json)
        }, maxLength: 100000);

        _logger.LogDebug("Enqueued request {MessageId} to session {SessionId}",
            request.MessageId, request.SessionId);
    }

    public async Task EnqueueBatchAsync(IEnumerable<BrokerRequest> requests, CancellationToken ct = default)
    {
        foreach (var req in requests)
        {
            await EnqueueAsync(req, ct);
        }
    }

    public async Task<BrokerRequest?> DequeueAsync(string sessionId, TimeSpan timeout, CancellationToken ct = default)
    {
        var key = StreamKey(sessionId);
        await EnsureConsumerGroupAsync(key);

        var consumerId = $"consumer-{Environment.MachineName}";
        var entries = await Db.StreamReadGroupAsync(key, ConsumerGroup, consumerId,
            ">", count: 1);

        if (entries == null || entries.Length == 0)
            return null;

        var entry = entries[0];
        var dataField = entry.Values.FirstOrDefault(v => v.Name == "data");
        if (dataField.Value.IsNullOrEmpty)
            return null;

        var request = JsonSerializer.Deserialize<BrokerRequest>(dataField.Value!, JsonOpts);
        return request;
    }

    public async Task AcknowledgeAsync(string sessionId, string messageId, CancellationToken ct = default)
    {
        var key = StreamKey(sessionId);
        var consumerId = $"consumer-{Environment.MachineName}";
        var pending = await Db.StreamPendingMessagesAsync(key, ConsumerGroup, 100, consumerId);

        foreach (var p in pending)
        {
            await Db.StreamAcknowledgeAsync(key, ConsumerGroup, p.MessageId);
            break; // ACK first pending
        }
    }

    public async Task<long> GetQueueDepthAsync(string sessionId, CancellationToken ct = default)
    {
        var key = StreamKey(sessionId);
        return await Db.StreamLengthAsync(key);
    }

    public async Task DeadLetterAsync(BrokerRequest request, string reason, CancellationToken ct = default)
    {
        var dlqKey = DeadLetterPrefix + request.SessionId;
        var json = JsonSerializer.Serialize(new { request, reason, timestamp = DateTime.UtcNow }, JsonOpts);
        await Db.StreamAddAsync(dlqKey, new NameValueEntry[]
        {
            new("data", json)
        }, maxLength: 10000);

        _logger.LogWarning("Dead-lettered request {MessageId}: {Reason}", request.MessageId, reason);
    }

    private async Task EnsureConsumerGroupAsync(string key)
    {
        try
        {
            await Db.StreamCreateConsumerGroupAsync(key, ConsumerGroup, "0-0", createStream: true);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
        {
            // Group already exists
        }
    }
}
