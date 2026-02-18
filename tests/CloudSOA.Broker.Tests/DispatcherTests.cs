using CloudSOA.Broker.Dispatch;
using CloudSOA.Broker.Queue;
using CloudSOA.Common.Interfaces;
using CloudSOA.Common.Models;
using Microsoft.Extensions.Logging;
using Moq;

namespace CloudSOA.Broker.Tests;

public class DispatcherTests
{
    [Fact]
    public async Task Enqueue_And_Dequeue_RoundTrip()
    {
        var queueMock = new Mock<IRequestQueue>();
        var request = new BrokerRequest
        {
            SessionId = "sess1",
            Action = "Calculate",
            Payload = new byte[] { 1, 2, 3 },
            UserData = "item-1"
        };

        queueMock.Setup(q => q.EnqueueAsync(It.IsAny<BrokerRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        queueMock.Setup(q => q.DequeueAsync("sess1", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);

        await queueMock.Object.EnqueueAsync(request);
        var result = await queueMock.Object.DequeueAsync("sess1", TimeSpan.FromSeconds(5));

        Assert.NotNull(result);
        Assert.Equal("sess1", result!.SessionId);
        Assert.Equal("Calculate", result.Action);
        Assert.Equal("item-1", result.UserData);
    }

    [Fact]
    public async Task FlowController_Accept_WhenQueueLow()
    {
        var queueMock = new Mock<IRequestQueue>();
        queueMock.Setup(q => q.GetQueueDepthAsync("sess1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(100);

        var fc = new FlowController(queueMock.Object, Mock.Of<ILogger<FlowController>>());
        var status = await fc.CheckAsync("sess1");

        Assert.Equal(FlowController.FlowStatus.Accept, status);
    }

    [Fact]
    public async Task FlowController_Throttle_WhenQueueHigh()
    {
        var queueMock = new Mock<IRequestQueue>();
        queueMock.Setup(q => q.GetQueueDepthAsync("sess1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(9000);

        var fc = new FlowController(queueMock.Object, Mock.Of<ILogger<FlowController>>());
        var status = await fc.CheckAsync("sess1");

        Assert.Equal(FlowController.FlowStatus.Throttle, status);
    }

    [Fact]
    public async Task FlowController_Reject_WhenQueueFull()
    {
        var queueMock = new Mock<IRequestQueue>();
        queueMock.Setup(q => q.GetQueueDepthAsync("sess1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(10000);

        var fc = new FlowController(queueMock.Object, Mock.Of<ILogger<FlowController>>());
        var status = await fc.CheckAsync("sess1");

        Assert.Equal(FlowController.FlowStatus.Reject, status);
    }

    [Fact]
    public async Task Dispatcher_StartStop_Tracking()
    {
        var queueMock = new Mock<IRequestQueue>();
        var responseMock = new Mock<IResponseStore>();
        queueMock.Setup(q => q.DequeueAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BrokerRequest?)null); // empty queue

        var dispatcher = new DispatcherEngine(
            queueMock.Object, responseMock.Object, Mock.Of<ILogger<DispatcherEngine>>());

        Assert.False(dispatcher.IsDispatching("sess1"));
        await dispatcher.StartDispatchingAsync("sess1");
        Assert.True(dispatcher.IsDispatching("sess1"));
        await dispatcher.StopDispatchingAsync("sess1");

        // Give task time to cancel
        await Task.Delay(100);
        Assert.False(dispatcher.IsDispatching("sess1"));

        dispatcher.Dispose();
    }

    [Fact]
    public async Task ResponseStore_AddAndFetch()
    {
        var responseMock = new Mock<IResponseStore>();
        var response = new BrokerResponse
        {
            SessionId = "sess1",
            RequestMessageId = "req1",
            Payload = new byte[] { 4, 5, 6 },
            UserData = "result-1"
        };

        responseMock.Setup(r => r.AddAsync(It.IsAny<BrokerResponse>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        responseMock.Setup(r => r.FetchAsync("sess1", 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BrokerResponse> { response });

        await responseMock.Object.AddAsync(response);
        var results = await responseMock.Object.FetchAsync("sess1");

        Assert.Single(results);
        Assert.Equal("result-1", results[0].UserData);
    }
}
