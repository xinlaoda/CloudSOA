using CloudSOA.ServiceHost.Wcf.Loader;
using CloudSOA.ServiceHost.Wcf.Protos;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace CloudSOA.ServiceHost.Wcf.Hosting;

/// <summary>
/// gRPC service implementation — receives requests from Broker, delegates to WCF adapter.
/// </summary>
public class ComputeGrpcService : ComputeService.ComputeServiceBase
{
    private readonly ISOAService _service;
    private readonly ILogger<ComputeGrpcService> _logger;
    private int _activeRequests;

    public ComputeGrpcService(ISOAService service, ILogger<ComputeGrpcService> logger)
    {
        _service = service;
        _logger = logger;
    }

    public override async Task<ExecuteResponse> Execute(ExecuteRequest request, ServerCallContext context)
    {
        Interlocked.Increment(ref _activeRequests);
        try
        {
            var result = await _service.ExecuteAsync(
                request.Action,
                request.Payload.ToByteArray(),
                context.CancellationToken);

            return new ExecuteResponse
            {
                MessageId = Guid.NewGuid().ToString("N"),
                RequestMessageId = request.MessageId,
                Payload = Google.Protobuf.ByteString.CopyFrom(result),
                IsFault = false
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing action '{Action}'", request.Action);
            return new ExecuteResponse
            {
                MessageId = Guid.NewGuid().ToString("N"),
                RequestMessageId = request.MessageId,
                IsFault = true,
                FaultMessage = ex.Message
            };
        }
        finally
        {
            Interlocked.Decrement(ref _activeRequests);
        }
    }

    public override Task<HealthCheckResponse> HealthCheck(
        HealthCheckRequest request, ServerCallContext context)
    {
        return Task.FromResult(new HealthCheckResponse
        {
            Healthy = true,
            ServiceName = _service.ServiceName,
            ActiveRequests = _activeRequests
        });
    }

    public override Task<ServiceInfoResponse> GetServiceInfo(
        ServiceInfoRequest request, ServerCallContext context)
    {
        var resp = new ServiceInfoResponse
        {
            ServiceName = _service.ServiceName,
            Version = "1.0.0"
        };
        resp.Actions.AddRange(_service.SupportedActions);
        return Task.FromResult(resp);
    }
}
