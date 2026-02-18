using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace CloudSOA.Broker.HA;

/// <summary>
/// 基于 Redis 的简单 Leader Election
/// 在 K8s 中建议使用 K8s Lease API，此实现用于开发/测试
/// </summary>
public class LeaderElection : IHostedService, IDisposable
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<LeaderElection> _logger;
    private readonly string _instanceId;
    private readonly TimeSpan _leaseDuration = TimeSpan.FromSeconds(15);
    private Timer? _timer;

    private const string LeaderKey = "cloudsoa:leader";

    public bool IsLeader { get; private set; }
    public string InstanceId => _instanceId;

    public event Action<bool>? LeadershipChanged;

    public LeaderElection(IConnectionMultiplexer redis, ILogger<LeaderElection> logger)
    {
        _redis = redis;
        _logger = logger;
        _instanceId = $"{Environment.MachineName}-{Guid.NewGuid():N}"[..16];
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _timer = new Timer(TryAcquireLease, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        _logger.LogInformation("Leader election started for instance {InstanceId}", _instanceId);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Change(Timeout.Infinite, 0);
        ReleaseLease();
        return Task.CompletedTask;
    }

    private void TryAcquireLease(object? state)
    {
        try
        {
            var db = _redis.GetDatabase();
            var acquired = db.StringSet(LeaderKey, _instanceId, _leaseDuration, When.NotExists);

            if (acquired)
            {
                if (!IsLeader)
                {
                    IsLeader = true;
                    _logger.LogInformation("Instance {InstanceId} became LEADER", _instanceId);
                    LeadershipChanged?.Invoke(true);
                }
            }
            else
            {
                var currentLeader = db.StringGet(LeaderKey);
                if (currentLeader == _instanceId)
                {
                    // Renew lease
                    db.KeyExpire(LeaderKey, _leaseDuration);
                    IsLeader = true;
                }
                else
                {
                    if (IsLeader)
                    {
                        IsLeader = false;
                        _logger.LogInformation("Instance {InstanceId} lost leadership to {Leader}",
                            _instanceId, (string?)currentLeader);
                        LeadershipChanged?.Invoke(false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in leader election");
        }
    }

    private void ReleaseLease()
    {
        try
        {
            var db = _redis.GetDatabase();
            var current = db.StringGet(LeaderKey);
            if (current == _instanceId)
            {
                db.KeyDelete(LeaderKey);
                IsLeader = false;
                _logger.LogInformation("Instance {InstanceId} released leadership", _instanceId);
            }
        }
        catch { /* best effort */ }
    }

    public void Dispose() => _timer?.Dispose();
}
