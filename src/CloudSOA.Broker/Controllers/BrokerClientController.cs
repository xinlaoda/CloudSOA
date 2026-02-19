using System.Text;
using CloudSOA.Broker.Queue;
using CloudSOA.Common.Exceptions;
using CloudSOA.Common.Interfaces;
using CloudSOA.Common.Models;
using Microsoft.AspNetCore.Mvc;

namespace CloudSOA.Broker.Controllers;

[ApiController]
[Route("api/v1/sessions/{sessionId}")]
public class BrokerClientController : ControllerBase
{
    private readonly ISessionManager _sessionManager;
    private readonly IRequestQueue _queue;
    private readonly IResponseStore _responseStore;
    private readonly IDispatcherEngine _dispatcher;
    private readonly FlowController _flowController;

    public BrokerClientController(
        ISessionManager sessionManager,
        IRequestQueue queue,
        IResponseStore responseStore,
        IDispatcherEngine dispatcher,
        FlowController flowController)
    {
        _sessionManager = sessionManager;
        _queue = queue;
        _responseStore = responseStore;
        _dispatcher = dispatcher;
        _flowController = flowController;
    }

    /// <summary>POST /api/v1/sessions/{sessionId}/requests — 发送请求（支持批量）</summary>
    [HttpPost("requests")]
    public async Task<IActionResult> SendRequests(string sessionId,
        [FromBody] SendRequestsBody body, CancellationToken ct)
    {
        try
        {
            await _sessionManager.GetSessionAsync(sessionId, ct);
        }
        catch (SessionNotFoundException)
        {
            return NotFound(new { message = $"Session '{sessionId}' not found." });
        }

        // 流控检查
        var flowStatus = await _flowController.CheckAsync(sessionId, ct);
        if (flowStatus == FlowController.FlowStatus.Reject)
            return StatusCode(429, new { message = "Queue is full. Try again later." });

        var requests = body.Requests.Select(r => new BrokerRequest
        {
            SessionId = sessionId,
            Action = r.Action ?? string.Empty,
            Payload = r.Payload != null ? Convert.FromBase64String(r.Payload) : Array.Empty<byte>(),
            UserData = r.UserData
        }).ToList();

        await _queue.EnqueueBatchAsync(requests, ct);

        foreach (var r in requests)
            Metrics.BrokerMetrics.RequestsEnqueued.WithLabels(sessionId, r.Action).Inc();

        // 确保 dispatcher 正在运行
        if (!_dispatcher.IsDispatching(sessionId))
            await _dispatcher.StartDispatchingAsync(sessionId, ct);

        return Accepted(new
        {
            enqueued = requests.Count,
            messageIds = requests.Select(r => r.MessageId).ToList()
        });
    }

    /// <summary>POST /api/v1/sessions/{sessionId}/requests/flush — 结束请求提交</summary>
    [HttpPost("requests/flush")]
    public async Task<IActionResult> EndRequests(string sessionId, CancellationToken ct)
    {
        // Signal end of requests — dispatcher will continue until queue is drained
        var depth = await _queue.GetQueueDepthAsync(sessionId, ct);
        return Ok(new { sessionId, pendingRequests = depth, message = "Requests flushed." });
    }

    /// <summary>GET /api/v1/sessions/{sessionId}/responses — 拉取响应</summary>
    [HttpGet("responses")]
    public async Task<IActionResult> GetResponses(string sessionId,
        [FromQuery] int maxCount = 100, CancellationToken ct = default)
    {
        var responses = await _responseStore.FetchAsync(sessionId, maxCount, ct);
        var remaining = await _responseStore.GetCountAsync(sessionId, ct);

        Metrics.BrokerMetrics.ResponsesDelivered.Inc(responses.Count);

        return Ok(new
        {
            responses = responses.Select(r => new
            {
                r.MessageId,
                r.RequestMessageId,
                payload = Convert.ToBase64String(r.Payload),
                r.UserData,
                r.IsFault,
                r.FaultMessage
            }),
            count = responses.Count,
            remaining
        });
    }
}

public class SendRequestsBody
{
    public List<RequestItem> Requests { get; set; } = new();
}

public class RequestItem
{
    public string? Action { get; set; }
    public string? Payload { get; set; } // Base64 encoded
    public string? UserData { get; set; }
}
