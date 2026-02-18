namespace CloudSOA.Common.Interfaces;

/// <summary>
/// 调度引擎接口
/// </summary>
public interface IDispatcherEngine
{
    /// <summary>启动指定 Session 的调度循环</summary>
    Task StartDispatchingAsync(string sessionId, CancellationToken ct = default);

    /// <summary>停止指定 Session 的调度</summary>
    Task StopDispatchingAsync(string sessionId);

    /// <summary>检查是否正在调度</summary>
    bool IsDispatching(string sessionId);
}
