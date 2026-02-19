using System.Collections.Concurrent;
using CloudSOA.Common.Interfaces;
using CloudSOA.Common.Models;
using Microsoft.Extensions.Logging;

namespace CloudSOA.Broker.Dispatch;

/// <summary>
/// 调度引擎 — 从请求队列取出请求，分发到 Service Host Pod
/// Phase 2: 使用 echo handler；Phase 3 接入 ServiceHost gRPC
/// </summary>
public class DispatcherEngine : IDispatcherEngine, IDisposable
{
    private readonly IRequestQueue _queue;
    private readonly IResponseStore _responseStore;
    private readonly ILogger<DispatcherEngine> _logger;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _dispatchers = new();

    /// <summary>Phase 3 将替换为 ServiceHost gRPC 调用</summary>
    public Func<BrokerRequest, Task<BrokerResponse>>? RequestHandler { get; set; }

    public DispatcherEngine(
        IRequestQueue queue,
        IResponseStore responseStore,
        ILogger<DispatcherEngine> logger)
    {
        _queue = queue;
        _responseStore = responseStore;
        _logger = logger;
    }

    public Task StartDispatchingAsync(string sessionId, CancellationToken ct = default)
    {
        if (_dispatchers.ContainsKey(sessionId))
            return Task.CompletedTask;

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _dispatchers[sessionId] = cts;

        _ = Task.Run(() => DispatchLoopAsync(sessionId, cts.Token), cts.Token);
        _logger.LogInformation("Started dispatching for session {SessionId}", sessionId);
        Metrics.BrokerMetrics.ActiveDispatchers.Inc();
        return Task.CompletedTask;
    }

    public Task StopDispatchingAsync(string sessionId)
    {
        if (_dispatchers.TryRemove(sessionId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
            Metrics.BrokerMetrics.ActiveDispatchers.Dec();
            _logger.LogInformation("Stopped dispatching for session {SessionId}", sessionId);
        }
        return Task.CompletedTask;
    }

    public bool IsDispatching(string sessionId) => _dispatchers.ContainsKey(sessionId);

    private async Task DispatchLoopAsync(string sessionId, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var request = await _queue.DequeueAsync(sessionId, TimeSpan.FromSeconds(5), ct);
                if (request == null)
                    continue;

                var sw = System.Diagnostics.Stopwatch.StartNew();
                var response = await ProcessRequestAsync(request, ct);
                await _responseStore.AddAsync(response, ct);
                await _queue.AcknowledgeAsync(sessionId, request.MessageId, ct);
                sw.Stop();
                Metrics.BrokerMetrics.RequestsProcessed.Inc();
                Metrics.BrokerMetrics.RequestLatency.Observe(sw.Elapsed.TotalSeconds);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Metrics.BrokerMetrics.RequestsFailed.Inc();
                _logger.LogError(ex, "Error in dispatch loop for session {SessionId}", sessionId);
                await Task.Delay(1000, ct);
            }
        }
    }

    private async Task<BrokerResponse> ProcessRequestAsync(BrokerRequest request, CancellationToken ct)
    {
        try
        {
            if (RequestHandler != null)
                return await RequestHandler(request);

            // Default echo handler for Phase 2 testing
            return new BrokerResponse
            {
                MessageId = Guid.NewGuid().ToString("N"),
                SessionId = request.SessionId,
                RequestMessageId = request.MessageId,
                Payload = request.Payload,
                UserData = request.UserData,
                IsFault = false
            };
        }
        catch (Exception ex)
        {
            if (request.RetryCount < request.MaxRetries)
            {
                request.RetryCount++;
                await _queue.EnqueueAsync(request, ct);
            }
            else
            {
                await _queue.DeadLetterAsync(request, ex.Message, ct);
            }

            return new BrokerResponse
            {
                MessageId = Guid.NewGuid().ToString("N"),
                SessionId = request.SessionId,
                RequestMessageId = request.MessageId,
                IsFault = true,
                FaultMessage = ex.Message
            };
        }
    }

    public void Dispose()
    {
        foreach (var kvp in _dispatchers)
        {
            kvp.Value.Cancel();
            kvp.Value.Dispose();
        }
        _dispatchers.Clear();
    }
}
