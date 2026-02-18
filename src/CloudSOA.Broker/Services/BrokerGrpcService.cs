using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using CloudSOA.Broker.Protos;
using CloudSOA.Common.Exceptions;
using CloudSOA.Common.Interfaces;

namespace CloudSOA.Broker.Services;

public class BrokerGrpcService : Protos.BrokerService.BrokerServiceBase
{
    private readonly ISessionManager _sessionManager;
    private readonly ILogger<BrokerGrpcService> _logger;

    public BrokerGrpcService(ISessionManager sessionManager, ILogger<BrokerGrpcService> logger)
    {
        _sessionManager = sessionManager;
        _logger = logger;
    }

    public override async Task<SessionInfoReply> CreateSession(
        Protos.CreateSessionRequest request, ServerCallContext context)
    {
        var result = await _sessionManager.CreateSessionAsync(new Common.Models.CreateSessionRequest
        {
            ServiceName = request.ServiceName,
            SessionType = (Common.Enums.SessionType)(int)request.SessionType,
            MinimumUnits = request.MinimumUnits,
            MaximumUnits = request.MaximumUnits,
            TransportScheme = (Common.Enums.TransportScheme)(int)request.TransportScheme,
            SessionIdleTimeout = request.SessionIdleTimeout?.ToTimeSpan(),
            ClientIdleTimeout = request.ClientIdleTimeout?.ToTimeSpan()
        }, context.CancellationToken);

        return ToReply(result);
    }

    public override async Task<SessionInfoReply> GetSession(
        GetSessionRequest request, ServerCallContext context)
    {
        try
        {
            var session = await _sessionManager.GetSessionAsync(request.SessionId, context.CancellationToken);
            return ToReply(session);
        }
        catch (SessionNotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
    }

    public override async Task<SessionInfoReply> AttachSession(
        AttachSessionRequest request, ServerCallContext context)
    {
        try
        {
            var session = await _sessionManager.AttachSessionAsync(
                request.SessionId, request.ClientId, context.CancellationToken);
            return ToReply(session);
        }
        catch (SessionNotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
        catch (SessionStateException ex)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, ex.Message));
        }
    }

    public override async Task<Empty> CloseSession(
        CloseSessionRequest request, ServerCallContext context)
    {
        try
        {
            await _sessionManager.CloseSessionAsync(request.SessionId, context.CancellationToken);
            return new Empty();
        }
        catch (SessionNotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
    }

    public override async Task<SessionStatusReply> GetSessionStatus(
        GetSessionRequest request, ServerCallContext context)
    {
        try
        {
            var status = await _sessionManager.GetSessionStatusAsync(
                request.SessionId, context.CancellationToken);
            return new SessionStatusReply
            {
                SessionId = status.SessionId,
                State = (SessionStateProto)(int)status.State,
                CurrentUnits = status.CurrentUnits,
                PendingRequests = status.PendingRequests,
                ProcessedRequests = status.ProcessedRequests,
                FailedRequests = status.FailedRequests,
                LastAccessedAt = status.LastAccessedAt.HasValue
                    ? Timestamp.FromDateTime(status.LastAccessedAt.Value.ToUniversalTime())
                    : null
            };
        }
        catch (SessionNotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
    }

    private static SessionInfoReply ToReply(Common.Models.SessionInfo s) => new()
    {
        SessionId = s.SessionId,
        ServiceName = s.ServiceName,
        State = (SessionStateProto)(int)s.State,
        SessionType = (SessionTypeProto)(int)s.SessionType,
        MinimumUnits = s.MinimumUnits,
        MaximumUnits = s.MaximumUnits,
        ClientId = s.ClientId ?? string.Empty,
        CreatedAt = Timestamp.FromDateTime(s.CreatedAt.ToUniversalTime()),
        BrokerEndpoint = s.BrokerEndpoint ?? string.Empty,
        TransportScheme = (TransportSchemeProto)(int)s.TransportScheme
    };
}
