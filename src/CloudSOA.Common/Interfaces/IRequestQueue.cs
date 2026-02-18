using CloudSOA.Common.Models;

namespace CloudSOA.Common.Interfaces;

/// <summary>
/// 请求队列接口 — 支持 Redis Streams / Service Bus 等后端
/// </summary>
public interface IRequestQueue
{
    /// <summary>入队请求</summary>
    Task EnqueueAsync(BrokerRequest request, CancellationToken ct = default);

    /// <summary>批量入队</summary>
    Task EnqueueBatchAsync(IEnumerable<BrokerRequest> requests, CancellationToken ct = default);

    /// <summary>出队一个请求（阻塞式，超时返回 null）</summary>
    Task<BrokerRequest?> DequeueAsync(string sessionId, TimeSpan timeout, CancellationToken ct = default);

    /// <summary>确认处理完成</summary>
    Task AcknowledgeAsync(string sessionId, string messageId, CancellationToken ct = default);

    /// <summary>获取指定 Session 队列深度</summary>
    Task<long> GetQueueDepthAsync(string sessionId, CancellationToken ct = default);

    /// <summary>将失败请求移入 Dead Letter</summary>
    Task DeadLetterAsync(BrokerRequest request, string reason, CancellationToken ct = default);
}
