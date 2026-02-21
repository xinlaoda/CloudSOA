using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CloudSOA.Client;

/// <summary>
/// Broker 客户端 — 发送请求和获取响应
/// 兼容 HPC Pack BrokerClient 语义
/// 用法：
///   using var client = new CloudBrokerClient(session);
///   client.SendRequest("Add", payload, "item-1");
///   client.EndRequests();
///   foreach (var resp in await client.GetResponsesAsync()) { ... }
/// </summary>
public class CloudBrokerClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _sessionId;
    private readonly List<RequestItem> _pendingRequests = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public CloudBrokerClient(CloudSession session)
    {
        _sessionId = session.SessionId;
        // Reuse TLS-configured HttpClient factory from CloudSession
        _http = CloudSession.CreateHttpClient(session.BrokerEndpoint.TrimEnd('/'), null);
    }

    internal CloudBrokerClient(CloudSession session, SessionStartInfo? info)
    {
        _sessionId = session.SessionId;
        _http = CloudSession.CreateHttpClient(session.BrokerEndpoint.TrimEnd('/'), info);
    }

    /// <summary>发送单个请求（缓存在本地，调用 EndRequests 后批量提交）</summary>
    public void SendRequest(string action, byte[] payload, string? userData = null)
    {
        _pendingRequests.Add(new RequestItem
        {
            Action = action,
            Payload = Convert.ToBase64String(payload),
            UserData = userData
        });
    }

    /// <summary>发送请求（字符串负载）</summary>
    public void SendRequest(string action, string payload, string? userData = null)
    {
        SendRequest(action, Encoding.UTF8.GetBytes(payload), userData);
    }

    /// <summary>结束请求提交，将所有缓存请求批量发送到 Broker</summary>
    public async Task<int> EndRequestsAsync(CancellationToken ct = default)
    {
        if (_pendingRequests.Count == 0) return 0;

        var body = new { requests = _pendingRequests };
        var resp = await _http.PostAsJsonAsync(
            $"/api/v1/sessions/{_sessionId}/requests", body, JsonOpts, ct);
        resp.EnsureSuccessStatusCode();

        var count = _pendingRequests.Count;
        _pendingRequests.Clear();

        // Signal flush
        await _http.PostAsync($"/api/v1/sessions/{_sessionId}/requests/flush", null, ct);

        return count;
    }

    /// <summary>拉取所有可用响应</summary>
    public async Task<IReadOnlyList<BrokerResponseDto>> GetResponsesAsync(
        int maxCount = 1000, CancellationToken ct = default)
    {
        var resp = await _http.GetFromJsonAsync<GetResponsesResult>(
            $"/api/v1/sessions/{_sessionId}/responses?maxCount={maxCount}", JsonOpts, ct);
        return resp?.Responses ?? new List<BrokerResponseDto>();
    }

    /// <summary>轮询等待所有响应返回</summary>
    public async Task<IReadOnlyList<BrokerResponseDto>> GetAllResponsesAsync(
        int expectedCount, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromMinutes(5));
        var allResponses = new List<BrokerResponseDto>();

        while (allResponses.Count < expectedCount && DateTime.UtcNow < deadline)
        {
            var batch = await GetResponsesAsync(expectedCount - allResponses.Count, ct);
            allResponses.AddRange(batch);

            if (allResponses.Count < expectedCount)
                await Task.Delay(200, ct);
        }

        return allResponses;
    }

    public void Dispose() => _http.Dispose();

    private class RequestItem
    {
        public string? Action { get; set; }
        public string? Payload { get; set; }
        public string? UserData { get; set; }
    }
}

public class BrokerResponseDto
{
    public string MessageId { get; set; } = string.Empty;
    public string RequestMessageId { get; set; } = string.Empty;
    public string? Payload { get; set; }
    public string? UserData { get; set; }
    public bool IsFault { get; set; }
    public string? FaultMessage { get; set; }

    /// <summary>获取 Base64 解码后的 payload 字节</summary>
    public byte[] GetPayloadBytes() =>
        Payload != null ? Convert.FromBase64String(Payload) : Array.Empty<byte>();

    /// <summary>获取 payload 字符串</summary>
    public string GetPayloadString() =>
        Encoding.UTF8.GetString(GetPayloadBytes());
}

internal class GetResponsesResult
{
    public List<BrokerResponseDto> Responses { get; set; } = new();
    public int Count { get; set; }
    public long Remaining { get; set; }
}
