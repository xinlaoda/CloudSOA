using CloudSOA.Common.Models;

namespace CloudSOA.Common.Interfaces;

/// <summary>
/// Session 元数据持久化存储接口
/// </summary>
public interface ISessionStore
{
    Task<SessionInfo?> GetAsync(string sessionId, CancellationToken ct = default);
    Task SaveAsync(SessionInfo session, CancellationToken ct = default);
    Task DeleteAsync(string sessionId, CancellationToken ct = default);
    Task<IReadOnlyList<SessionInfo>> ListAsync(CancellationToken ct = default);
    Task RefreshAccessTimeAsync(string sessionId, CancellationToken ct = default);
}
