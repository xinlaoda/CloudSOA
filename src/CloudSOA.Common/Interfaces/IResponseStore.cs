using CloudSOA.Common.Models;

namespace CloudSOA.Common.Interfaces;

/// <summary>
/// 响应存储接口 — 缓存 Service Host 返回的结果
/// </summary>
public interface IResponseStore
{
    /// <summary>存储响应</summary>
    Task AddAsync(BrokerResponse response, CancellationToken ct = default);

    /// <summary>获取指定 Session 的所有响应（拉取后删除）</summary>
    Task<IReadOnlyList<BrokerResponse>> FetchAsync(string sessionId, int maxCount = 100, CancellationToken ct = default);

    /// <summary>获取响应数量</summary>
    Task<long> GetCountAsync(string sessionId, CancellationToken ct = default);
}
