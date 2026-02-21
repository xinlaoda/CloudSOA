using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using CloudSOA.Common.Enums;
using CloudSOA.Common.Models;

namespace CloudSOA.Client;

/// <summary>
/// 云原生 SOA Session — 替代 HPC Pack Session
/// 用法：var session = await CloudSession.CreateSessionAsync(startInfo);
/// </summary>
public class CloudSession : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public string SessionId { get; private set; } = string.Empty;
    public string ServiceName { get; private set; } = string.Empty;
    public SessionState State { get; private set; }
    public string BrokerEndpoint { get; }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private CloudSession(string brokerEndpoint, SessionStartInfo? info = null)
    {
        BrokerEndpoint = brokerEndpoint;
        _baseUrl = brokerEndpoint.TrimEnd('/');
        _http = CreateHttpClient(_baseUrl, info);
    }

    internal static HttpClient CreateHttpClient(string baseUrl, SessionStartInfo? info)
    {
        // Username/Password auth is not supported — fail fast
        if (info != null && !string.IsNullOrEmpty(info.Username))
            throw new NotSupportedException(
                "Username/Password authentication is not supported in CloudSOA. " +
                "Use BearerToken (JWT/Azure AD) or ApiKey instead. " +
                "Remove the Username/Password assignment from your SessionStartInfo.");

        var handler = new HttpClientHandler();

        if (info != null)
        {
            // Accept self-signed certs in dev mode
            if (info.AcceptUntrustedCertificates)
            {
                handler.ServerCertificateCustomValidationCallback =
                    (message, cert, chain, errors) => true;
            }

            // Mutual TLS: client certificate
            if (info.ClientCertificate != null)
            {
                handler.ClientCertificates.Add(info.ClientCertificate);
            }
        }

        var client = new HttpClient(handler) { BaseAddress = new Uri(baseUrl) };

        if (info != null)
        {
            // Apply auth headers
            if (!string.IsNullOrEmpty(info.BearerToken))
                client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {info.BearerToken}");

            if (!string.IsNullOrEmpty(info.ApiKey))
                client.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Key", info.ApiKey);

            // Propagate custom headers
            foreach (var kv in info.Properties)
                client.DefaultRequestHeaders.TryAddWithoutValidation(kv.Key, kv.Value);
        }

        return client;
    }

    /// <summary>创建新 Session</summary>
    public static async Task<CloudSession> CreateSessionAsync(
        SessionStartInfo info, CancellationToken ct = default)
    {
        var session = new CloudSession(info.BrokerEndpoint, info);

        var body = new
        {
            serviceName = info.ServiceName,
            sessionType = (int)info.SessionType,
            minimumUnits = info.MinimumUnits,
            maximumUnits = info.MaximumUnits,
            transportScheme = (int)info.TransportScheme
        };

        var resp = await session._http.PostAsJsonAsync("/api/v1/sessions", body, JsonOpts, ct);
        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<SessionInfo>(JsonOpts, ct);
        session.SessionId = result!.SessionId;
        session.ServiceName = result.ServiceName;
        session.State = result.State;

        return session;
    }

    /// <summary>附加到已有 Session</summary>
    public static async Task<CloudSession> AttachSessionAsync(
        string brokerEndpoint, string sessionId, string? clientId = null, CancellationToken ct = default)
    {
        var session = new CloudSession(brokerEndpoint);
        var url = $"/api/v1/sessions/{sessionId}/attach" +
            (clientId != null ? $"?clientId={clientId}" : "");

        var resp = await session._http.PostAsync(url, null, ct);
        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<SessionInfo>(JsonOpts, ct);
        session.SessionId = result!.SessionId;
        session.ServiceName = result.ServiceName;
        session.State = result.State;

        return session;
    }

    /// <summary>获取 Session 状态</summary>
    public async Task<SessionStatusResponse> GetStatusAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetFromJsonAsync<SessionStatusResponse>(
            $"/api/v1/sessions/{SessionId}/status", JsonOpts, ct);
        return resp!;
    }

    /// <summary>关闭 Session</summary>
    public async Task CloseAsync(CancellationToken ct = default)
    {
        await _http.DeleteAsync($"/api/v1/sessions/{SessionId}", ct);
        State = SessionState.Closed;
    }

    public void Dispose() => _http.Dispose();
}
