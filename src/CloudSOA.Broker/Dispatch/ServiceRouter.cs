using System.Collections.Concurrent;
using CloudSOA.Common.Interfaces;
using CloudSOA.Common.Models;
using CloudSOA.ServiceHost.Protos;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;

namespace CloudSOA.Broker.Dispatch;

/// <summary>
/// Routes requests to the correct ServiceHost pod based on session's service name.
/// Uses K8s DNS: http://svc-{servicename}:5010
/// </summary>
public class ServiceRouter : IDisposable
{
    private readonly ISessionStore _sessionStore;
    private readonly ILogger<ServiceRouter> _logger;
    private readonly ConcurrentDictionary<string, (GrpcChannel Channel, ComputeService.ComputeServiceClient Client)> _clients = new();

    public ServiceRouter(ISessionStore sessionStore, ILogger<ServiceRouter> logger)
    {
        _sessionStore = sessionStore;
        _logger = logger;
    }

    public async Task<BrokerResponse> ExecuteAsync(BrokerRequest request, CancellationToken ct = default)
    {
        var session = await _sessionStore.GetAsync(request.SessionId, ct);
        var serviceName = session?.ServiceName;

        if (string.IsNullOrEmpty(serviceName))
        {
            _logger.LogWarning("Session {SessionId} has no service name, using echo fallback", request.SessionId);
            return EchoResponse(request);
        }

        var client = GetOrCreateClient(serviceName);

        var grpcRequest = new ExecuteRequest
        {
            MessageId = request.MessageId,
            SessionId = request.SessionId,
            Action = request.Action,
            Payload = Google.Protobuf.ByteString.CopyFrom(request.Payload),
            UserData = request.UserData ?? string.Empty
        };

        try
        {
            var grpcResponse = await client.ExecuteAsync(grpcRequest, cancellationToken: ct);
            return new BrokerResponse
            {
                MessageId = grpcResponse.MessageId,
                SessionId = request.SessionId,
                RequestMessageId = grpcResponse.RequestMessageId,
                Payload = grpcResponse.Payload.ToByteArray(),
                IsFault = grpcResponse.IsFault,
                FaultMessage = grpcResponse.FaultMessage
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to route request to service {ServiceName}", serviceName);
            throw;
        }
    }

    private ComputeService.ComputeServiceClient GetOrCreateClient(string serviceName)
    {
        var key = serviceName.ToLowerInvariant();
        var entry = _clients.GetOrAdd(key, name =>
        {
            var endpoint = $"http://svc-{name}:5010";
            _logger.LogInformation("Creating gRPC channel for service {ServiceName} at {Endpoint}", serviceName, endpoint);
            var channel = GrpcChannel.ForAddress(endpoint);
            var client = new ComputeService.ComputeServiceClient(channel);
            return (channel, client);
        });
        return entry.Client;
    }

    private static BrokerResponse EchoResponse(BrokerRequest request) => new()
    {
        MessageId = Guid.NewGuid().ToString("N"),
        SessionId = request.SessionId,
        RequestMessageId = request.MessageId,
        Payload = request.Payload,
        UserData = request.UserData,
        IsFault = false
    };

    public void Dispose()
    {
        foreach (var entry in _clients.Values)
            entry.Channel.Dispose();
        _clients.Clear();
    }
}
