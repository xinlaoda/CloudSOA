using CloudSOA.Broker.Services;
using CloudSOA.Common.Enums;
using CloudSOA.Common.Exceptions;
using CloudSOA.Common.Interfaces;
using CloudSOA.Common.Models;
using Microsoft.Extensions.Logging;
using Moq;

namespace CloudSOA.Broker.Tests;

public class SessionManagerTests
{
    private readonly Mock<ISessionStore> _storeMock;
    private readonly SessionManagerService _sut;

    public SessionManagerTests()
    {
        _storeMock = new Mock<ISessionStore>();
        var logger = Mock.Of<ILogger<SessionManagerService>>();
        _sut = new SessionManagerService(_storeMock.Object, logger);
    }

    [Fact]
    public async Task CreateSession_ReturnsActiveSession()
    {
        _storeMock.Setup(s => s.SaveAsync(It.IsAny<SessionInfo>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new CreateSessionRequest
        {
            ServiceName = "TestService",
            MinimumUnits = 2,
            MaximumUnits = 10,
            SessionType = SessionType.Interactive
        };

        var result = await _sut.CreateSessionAsync(request);

        Assert.NotNull(result);
        Assert.Equal("TestService", result.ServiceName);
        Assert.Equal(SessionState.Active, result.State);
        Assert.Equal(2, result.MinimumUnits);
        Assert.Equal(10, result.MaximumUnits);
        Assert.NotEmpty(result.SessionId);

        _storeMock.Verify(s => s.SaveAsync(It.IsAny<SessionInfo>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetSession_ExistingSession_ReturnsSession()
    {
        var session = CreateTestSession();
        _storeMock.Setup(s => s.GetAsync(session.SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var result = await _sut.GetSessionAsync(session.SessionId);

        Assert.Equal(session.SessionId, result.SessionId);
        _storeMock.Verify(s => s.RefreshAccessTimeAsync(session.SessionId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetSession_NonExistent_ThrowsNotFound()
    {
        _storeMock.Setup(s => s.GetAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SessionInfo?)null);

        await Assert.ThrowsAsync<SessionNotFoundException>(() =>
            _sut.GetSessionAsync("missing"));
    }

    [Fact]
    public async Task AttachSession_ActiveSession_SetsClientId()
    {
        var session = CreateTestSession();
        _storeMock.Setup(s => s.GetAsync(session.SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var result = await _sut.AttachSessionAsync(session.SessionId, "client-1");

        Assert.Equal("client-1", result.ClientId);
        _storeMock.Verify(s => s.SaveAsync(It.Is<SessionInfo>(si => si.ClientId == "client-1"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AttachSession_ClosedSession_Throws()
    {
        var session = CreateTestSession();
        session.State = SessionState.Closed;
        _storeMock.Setup(s => s.GetAsync(session.SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        await Assert.ThrowsAsync<SessionStateException>(() =>
            _sut.AttachSessionAsync(session.SessionId));
    }

    [Fact]
    public async Task CloseSession_ActiveSession_TransitionsToClosed()
    {
        var session = CreateTestSession();
        _storeMock.Setup(s => s.GetAsync(session.SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        await _sut.CloseSessionAsync(session.SessionId);

        _storeMock.Verify(s => s.SaveAsync(It.Is<SessionInfo>(si => si.State == SessionState.Closed),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task CloseSession_AlreadyClosed_NoOp()
    {
        var session = CreateTestSession();
        session.State = SessionState.Closed;
        _storeMock.Setup(s => s.GetAsync(session.SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        await _sut.CloseSessionAsync(session.SessionId);

        _storeMock.Verify(s => s.SaveAsync(It.IsAny<SessionInfo>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetSessionStatus_ReturnsStatus()
    {
        var session = CreateTestSession();
        _storeMock.Setup(s => s.GetAsync(session.SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var status = await _sut.GetSessionStatusAsync(session.SessionId);

        Assert.Equal(session.SessionId, status.SessionId);
        Assert.Equal(SessionState.Active, status.State);
    }

    private static SessionInfo CreateTestSession() => new()
    {
        SessionId = Guid.NewGuid().ToString("N"),
        ServiceName = "TestService",
        State = SessionState.Active,
        SessionType = SessionType.Interactive,
        MinimumUnits = 1,
        MaximumUnits = 10,
        CreatedAt = DateTime.UtcNow,
        LastAccessedAt = DateTime.UtcNow
    };
}
