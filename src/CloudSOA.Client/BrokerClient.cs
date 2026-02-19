using System.Reflection;
using System.Runtime.Serialization;
using System.ServiceModel;

namespace CloudSOA.Client;

/// <summary>
/// HPC Pack-compatible BrokerClient&lt;T&gt;.
/// T is the WCF service contract interface (e.g., ICalculator).
/// Supports SendRequest with WCF message contracts and DataContractSerializer payloads.
/// </summary>
public class BrokerClient<T> : IDisposable where T : class
{
    private readonly CloudBrokerClient _innerClient;
    private int _sentCount;

    public BrokerClient(Session session)
    {
        _innerClient = new CloudBrokerClient(session.InnerSession);
    }

    /// <summary>
    /// Send a typed request. TMessage should be a WCF MessageContract (e.g., AddRequest).
    /// The action name is derived from the type name by stripping the "Request" suffix.
    /// </summary>
    public void SendRequest<TMessage>(TMessage request, string? userData = null) where TMessage : class
    {
        var typeName = typeof(TMessage).Name;
        var action = typeName.EndsWith("Request", StringComparison.Ordinal)
            ? typeName.Substring(0, typeName.Length - "Request".Length)
            : typeName;

        var serializer = new DataContractSerializer(typeof(TMessage));
        using var ms = new MemoryStream();
        serializer.WriteObject(ms, request);
        var payload = ms.ToArray();

        _innerClient.SendRequest(action, payload, userData);
        _sentCount++;
    }

    /// <summary>
    /// Send a request with a named action and raw parameters.
    /// </summary>
    public void SendRequest(string action, params object[] parameters)
    {
        var knownTypes = parameters.Select(p => p.GetType()).Distinct().ToArray();
        var serializer = new DataContractSerializer(typeof(object[]), knownTypes);
        using var ms = new MemoryStream();
        serializer.WriteObject(ms, parameters);
        var payload = ms.ToArray();

        _innerClient.SendRequest(action, payload);
        _sentCount++;
    }

    /// <summary>Mark that all requests have been sent.</summary>
    public void EndRequests()
    {
        _innerClient.EndRequestsAsync().GetAwaiter().GetResult();
    }

    /// <summary>Mark that all requests have been sent (async).</summary>
    public Task EndRequestsAsync(CancellationToken ct = default)
    {
        return _innerClient.EndRequestsAsync(ct);
    }

    /// <summary>
    /// Get typed responses. TMessage should be a WCF MessageContract (e.g., AddResponse).
    /// Compatible with HPC Pack foreach-style enumeration.
    /// </summary>
    public IEnumerable<BrokerResponse<TMessage>> GetResponses<TMessage>() where TMessage : class, new()
    {
        var responses = _innerClient.GetAllResponsesAsync(_sentCount).GetAwaiter().GetResult();
        return DeserializeResponses<TMessage>(responses);
    }

    /// <summary>Get responses asynchronously.</summary>
    public async Task<IReadOnlyList<BrokerResponse<TMessage>>> GetResponsesAsync<TMessage>(
        CancellationToken ct = default) where TMessage : class, new()
    {
        var responses = await _innerClient.GetAllResponsesAsync(_sentCount, ct: ct);
        return DeserializeResponses<TMessage>(responses).ToList();
    }

    private static IEnumerable<BrokerResponse<TMessage>> DeserializeResponses<TMessage>(
        IReadOnlyList<BrokerResponseDto> responses) where TMessage : class, new()
    {
        var serializer = new DataContractSerializer(typeof(TMessage));

        foreach (var resp in responses)
        {
            var typedResponse = new BrokerResponse<TMessage>
            {
                UserData = resp.UserData,
                IsFault = resp.IsFault,
                FaultMessage = resp.FaultMessage
            };

            if (!resp.IsFault && resp.Payload != null)
            {
                try
                {
                    var bytes = Convert.FromBase64String(resp.Payload);
                    using var ms = new MemoryStream(bytes);
                    typedResponse.Result = (TMessage)serializer.ReadObject(ms)!;
                }
                catch (Exception ex)
                {
                    typedResponse.IsFault = true;
                    typedResponse.FaultMessage = $"Deserialization failed: {ex.Message}";
                }
            }

            yield return typedResponse;
        }
    }

    public void Dispose() => _innerClient.Dispose();
}
