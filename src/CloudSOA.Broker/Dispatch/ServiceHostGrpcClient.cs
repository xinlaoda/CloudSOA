using CloudSOA.Common.Models;
using CloudSOA.ServiceHost.Protos;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;

namespace CloudSOA.Broker.Dispatch;

/// <summary>
/// 通过 gRPC 调用 ServiceHost Pod 处理请求
/// </summary>
public class ServiceHostGrpcClient : IDisposable
{
    private readonly ILogger<ServiceHostGrpcClient> _logger;
    private readonly List<GrpcChannel> _channels = new();
    private readonly List<ComputeService.ComputeServiceClient> _clients = new();
    private int _roundRobinIndex;
    private readonly object _lock = new();

    public ServiceHostGrpcClient(ILogger<ServiceHostGrpcClient> logger)
    {
        _logger = logger;
    }

    /// <summary>添加 ServiceHost endpoint</summary>
    public void AddEndpoint(string address)
    {
        var channel = GrpcChannel.ForAddress(address);
        var client = new ComputeService.ComputeServiceClient(channel);
        lock (_lock)
        {
            _channels.Add(channel);
            _clients.Add(client);
        }
        _logger.LogInformation("Added ServiceHost endpoint: {Address}", address);
    }

    /// <summary>Round-robin 分发请求到 ServiceHost</summary>
    public async Task<BrokerResponse> ExecuteAsync(BrokerRequest request, CancellationToken ct = default)
    {
        ComputeService.ComputeServiceClient client;
        lock (_lock)
        {
            if (_clients.Count == 0)
                throw new InvalidOperationException("No ServiceHost endpoints available.");
            client = _clients[_roundRobinIndex % _clients.Count];
            _roundRobinIndex++;
        }

        var grpcRequest = new ExecuteRequest
        {
            MessageId = request.MessageId,
            SessionId = request.SessionId,
            Action = request.Action,
            Payload = Google.Protobuf.ByteString.CopyFrom(request.Payload),
            UserData = request.UserData ?? string.Empty
        };

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

    public void Dispose()
    {
        foreach (var ch in _channels) ch.Dispose();
    }
}
