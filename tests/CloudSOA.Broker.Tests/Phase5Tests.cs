using CloudSOA.Broker.Metrics;
using CloudSOA.Broker.Queue;
using CloudSOA.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;

namespace CloudSOA.Broker.Tests;

public class Phase5Tests
{
    [Fact]
    public void BrokerMetrics_Counters_AreNotNull()
    {
        Assert.NotNull(BrokerMetrics.ActiveSessions);
        Assert.NotNull(BrokerMetrics.SessionsCreated);
        Assert.NotNull(BrokerMetrics.RequestsEnqueued);
        Assert.NotNull(BrokerMetrics.RequestsProcessed);
        Assert.NotNull(BrokerMetrics.RequestsFailed);
        Assert.NotNull(BrokerMetrics.ResponsesDelivered);
        Assert.NotNull(BrokerMetrics.QueueDepth);
        Assert.NotNull(BrokerMetrics.RequestLatency);
        Assert.NotNull(BrokerMetrics.ActiveDispatchers);
    }

    [Fact]
    public void BrokerMetrics_Increment_Works()
    {
        var before = BrokerMetrics.SessionsClosed.Value;
        BrokerMetrics.SessionsClosed.Inc();
        Assert.Equal(before + 1, BrokerMetrics.SessionsClosed.Value);
    }

    [Fact]
    public async Task FlowController_CustomThresholds()
    {
        var queueMock = new Mock<IRequestQueue>();
        queueMock.Setup(q => q.GetQueueDepthAsync("s1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(50);

        var fc = new FlowController(queueMock.Object, Mock.Of<ILogger<FlowController>>())
        {
            MaxQueueDepth = 100,
            ThrottleThreshold = 40
        };

        var status = await fc.CheckAsync("s1");
        Assert.Equal(FlowController.FlowStatus.Throttle, status);
    }

    [Trait("Category", "Integration")]
    [Fact]
    public async Task MetricsEndpoint_Returns200()
    {
        using var http = new HttpClient();
        var resp = await http.GetAsync("http://localhost:5000/metrics");
        Assert.True(resp.IsSuccessStatusCode);

        var content = await resp.Content.ReadAsStringAsync();
        // Prometheus-net exposes HTTP metrics automatically
        Assert.Contains("http_request_duration_seconds", content);
    }

    [Trait("Category", "Integration")]
    [Fact]
    public async Task ApiKey_NotRequired_InDevMode()
    {
        // With no API key configured, all endpoints should be accessible
        using var http = new HttpClient();
        var resp = await http.GetAsync("http://localhost:5000/healthz");
        Assert.True(resp.IsSuccessStatusCode);
    }
}
