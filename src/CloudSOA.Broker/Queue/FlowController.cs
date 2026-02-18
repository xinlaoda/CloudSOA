using CloudSOA.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace CloudSOA.Broker.Queue;

/// <summary>
/// 基于队列深度的流控 — back-pressure 机制
/// </summary>
public class FlowController
{
    private readonly IRequestQueue _queue;
    private readonly ILogger<FlowController> _logger;

    public int MaxQueueDepth { get; set; } = 10000;
    public int ThrottleThreshold { get; set; } = 8000;

    public FlowController(IRequestQueue queue, ILogger<FlowController> logger)
    {
        _queue = queue;
        _logger = logger;
    }

    public enum FlowStatus { Accept, Throttle, Reject }

    public async Task<FlowStatus> CheckAsync(string sessionId, CancellationToken ct = default)
    {
        var depth = await _queue.GetQueueDepthAsync(sessionId, ct);

        if (depth >= MaxQueueDepth)
        {
            _logger.LogWarning("Session {SessionId} queue full ({Depth}), rejecting", sessionId, depth);
            return FlowStatus.Reject;
        }

        if (depth >= ThrottleThreshold)
        {
            _logger.LogInformation("Session {SessionId} queue high ({Depth}), throttling", sessionId, depth);
            return FlowStatus.Throttle;
        }

        return FlowStatus.Accept;
    }
}
