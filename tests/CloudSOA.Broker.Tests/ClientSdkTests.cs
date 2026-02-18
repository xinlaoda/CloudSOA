using CloudSOA.Client;
using CloudSOA.Common.Enums;

namespace CloudSOA.Broker.Tests;

/// <summary>
/// Client SDK 集成测试 — 需要 Broker 服务运行中
/// 标记为 [Trait("Category", "Integration")] 以便独立运行
/// </summary>
public class ClientSdkTests
{
    private const string BrokerUrl = "http://localhost:5000";

    [Trait("Category", "Integration")]
    [Fact]
    public async Task FullLifecycle_CreateSendReceiveClose()
    {
        // 1. Create session
        var startInfo = new SessionStartInfo(BrokerUrl, "TestCalcService")
        {
            MinimumUnits = 1,
            MaximumUnits = 5,
            TransportScheme = TransportScheme.Http
        };

        using var session = await CloudSession.CreateSessionAsync(startInfo);
        Assert.NotEmpty(session.SessionId);
        Assert.Equal(SessionState.Active, session.State);

        // 2. Send requests
        using var client = new CloudBrokerClient(session);
        for (int i = 0; i < 10; i++)
        {
            client.SendRequest("Echo", $"data-{i}", $"item-{i}");
        }

        var sent = await client.EndRequestsAsync();
        Assert.Equal(10, sent);

        // 3. Wait and get all responses
        var responses = await client.GetAllResponsesAsync(10, TimeSpan.FromSeconds(15));
        Assert.Equal(10, responses.Count);
        Assert.All(responses, r => Assert.False(r.IsFault));

        // 4. Check status
        var status = await session.GetStatusAsync();
        Assert.Equal(session.SessionId, status.SessionId);

        // 5. Close
        await session.CloseAsync();
        Assert.Equal(SessionState.Closed, session.State);
    }

    [Trait("Category", "Integration")]
    [Fact]
    public async Task AttachSession_Works()
    {
        var startInfo = new SessionStartInfo(BrokerUrl, "AttachTestService")
        {
            MinimumUnits = 1,
            MaximumUnits = 2
        };

        using var session1 = await CloudSession.CreateSessionAsync(startInfo);

        // Attach from another "client"
        using var session2 = await CloudSession.AttachSessionAsync(
            BrokerUrl, session1.SessionId, "client-2");

        Assert.Equal(session1.SessionId, session2.SessionId);

        await session1.CloseAsync();
    }
}
