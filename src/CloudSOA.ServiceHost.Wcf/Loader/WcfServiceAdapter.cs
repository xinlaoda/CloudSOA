using System.Runtime.Serialization;
using Microsoft.Extensions.Logging;

namespace CloudSOA.ServiceHost.Wcf.Loader;

/// <summary>
/// Adapts a legacy WCF service to the ISOAService interface.
/// Deserializes payloads via DataContractSerializer and invokes WCF methods via reflection.
/// </summary>
public class WcfServiceAdapter : ISOAService
{
    private readonly WcfServiceInfo _info;
    private readonly object _instance;
    private readonly ILogger<WcfServiceAdapter> _logger;
    private readonly Dictionary<string, WcfMethodInfo> _methodMap;

    public WcfServiceAdapter(WcfServiceInfo info, ILogger<WcfServiceAdapter> logger)
    {
        _info = info;
        _logger = logger;
        _instance = Activator.CreateInstance(info.ImplementationType)
            ?? throw new InvalidOperationException(
                $"Failed to create instance of {info.ImplementationType.Name}");

        _methodMap = info.Methods.ToDictionary(m => m.ActionName, StringComparer.OrdinalIgnoreCase);
    }

    public string ServiceName => _info.ServiceName;

    public IReadOnlyList<string> SupportedActions =>
        _info.Methods.Select(m => m.ActionName).ToList();

    public async Task<byte[]> ExecuteAsync(string action, byte[] payload, CancellationToken ct = default)
    {
        if (!_methodMap.TryGetValue(action, out var methodInfo))
        {
            throw new InvalidOperationException($"Unknown action: {action}");
        }

        // Deserialize parameters
        var parameters = DeserializeParameters(payload, methodInfo.ParameterTypes);

        // Invoke the WCF method
        _logger.LogDebug("Invoking {Action} on {Service}", action, ServiceName);
        var result = methodInfo.Method.Invoke(_instance, parameters);

        // Handle async methods (Task<T>)
        if (result is Task task)
        {
            await task.ConfigureAwait(false);
            var resultType = methodInfo.Method.ReturnType;
            if (resultType.IsGenericType && resultType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var resultProperty = resultType.GetProperty("Result")!;
                result = resultProperty.GetValue(task);
            }
            else
            {
                // Task with no return value
                return Array.Empty<byte>();
            }
        }

        // Serialize result
        if (result == null) return Array.Empty<byte>();
        return SerializeResult(result);
    }

    private object?[] DeserializeParameters(byte[] payload, Type[] parameterTypes)
    {
        if (parameterTypes.Length == 0)
            return Array.Empty<object?>();

        if (payload.Length == 0)
            return parameterTypes.Select(t => t.IsValueType ? Activator.CreateInstance(t) : null).ToArray();

        try
        {
            using var ms = new MemoryStream(payload);
            using var reader = System.Xml.XmlReader.Create(ms);

            reader.MoveToContent();
            if (reader.NodeType != System.Xml.XmlNodeType.Element)
                throw new InvalidOperationException("Expected root element");

            var values = new List<object?>();
            if (!reader.Read()) throw new InvalidOperationException("Empty element");

            var paramIndex = 0;
            while (paramIndex < parameterTypes.Length)
            {
                while (reader.NodeType != System.Xml.XmlNodeType.Element)
                {
                    if (reader.NodeType == System.Xml.XmlNodeType.EndElement || !reader.Read())
                        break;
                }
                if (reader.NodeType != System.Xml.XmlNodeType.Element)
                    break;

                var text = reader.ReadElementContentAsString();
                values.Add(Convert.ChangeType(text, parameterTypes[paramIndex],
                    System.Globalization.CultureInfo.InvariantCulture));
                paramIndex++;
            }

            if (values.Count == parameterTypes.Length)
                return values.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "XML deserialization failed, falling through");
        }

        // Fallback: single parameter
        if (parameterTypes.Length == 1)
        {
            // Try DataContract first, then plain UTF-8 for string types
            try { return new[] { Deserialize(payload, parameterTypes[0]) }; }
            catch when (parameterTypes[0] == typeof(string))
            {
                return new object?[] { System.Text.Encoding.UTF8.GetString(payload) };
            }
        }

        return parameterTypes.Select(t => t.IsValueType ? Activator.CreateInstance(t) : null).ToArray();
    }

    private static object? Deserialize(byte[] data, Type type)
    {
        if (data.Length == 0) return type.IsValueType ? Activator.CreateInstance(type) : null;

        var serializer = new DataContractSerializer(type);
        using var ms = new MemoryStream(data);
        return serializer.ReadObject(ms);
    }

    private static byte[] SerializeResult(object result)
    {
        var serializer = new DataContractSerializer(result.GetType());
        using var ms = new MemoryStream();
        serializer.WriteObject(ms, result);
        return ms.ToArray();
    }
}
