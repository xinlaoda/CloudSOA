using CloudSOA.Common.Models;

namespace CloudSOA.Common.Interfaces;

/// <summary>
/// Session 管理器接口，定义 Session 生命周期操作
/// </summary>
public interface ISessionManager
{
    Task<SessionInfo> CreateSessionAsync(CreateSessionRequest request, CancellationToken ct = default);
    Task<SessionInfo> GetSessionAsync(string sessionId, CancellationToken ct = default);
    Task<SessionInfo> AttachSessionAsync(string sessionId, string? clientId = null, CancellationToken ct = default);
    Task CloseSessionAsync(string sessionId, CancellationToken ct = default);
    Task<SessionStatusResponse> GetSessionStatusAsync(string sessionId, CancellationToken ct = default);
}
