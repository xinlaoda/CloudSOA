using System.Runtime.Serialization;
using Microsoft.Extensions.Logging;

namespace CloudSOA.ServiceHost.CoreWcf.Loader;

/// <summary>
/// Adapts a CoreWCF/.NET 8 service DLL to the ISOAService interface.
/// Same approach as the WcfServiceAdapter but runs natively on .NET 8.
/// </summary>
public class CoreWcfServiceAdapter : ISOAService
{
    private readonly CoreWcfServiceInfo _info;
    private readonly object _instance;
    private readonly ILogger _logger;
    private readonly Dictionary<string, CoreWcfMethodInfo> _methodMap;

    public CoreWcfServiceAdapter(CoreWcfServiceInfo info, ILogger logger)
    {
        _info = info;
        _logger = logger;
        _instance = Activator.CreateInstance(info.ImplementationType)
            ?? throw new InvalidOperationException($"Failed to create {info.ImplementationType.Name}");
        _methodMap = info.Methods.ToDictionary(m => m.ActionName, StringComparer.OrdinalIgnoreCase);
    }

    public string ServiceName => _info.ServiceName;
    public IReadOnlyList<string> SupportedActions => _info.Methods.Select(m => m.ActionName).ToList();

    public async Task<byte[]> ExecuteAsync(string action, byte[] payload, CancellationToken ct = default)
    {
        if (!_methodMap.TryGetValue(action, out var methodInfo))
            throw new InvalidOperationException($"Unknown action: {action}");

        var parameters = DeserializeParameters(payload, methodInfo.ParameterTypes);

        _logger.LogDebug("Invoking {Action} on {Service}", action, ServiceName);
        var result = methodInfo.Method.Invoke(_instance, parameters);

        // Handle async
        if (result is Task task)
        {
            await task.ConfigureAwait(false);
            var rt = methodInfo.Method.ReturnType;
            if (rt.IsGenericType && rt.GetGenericTypeDefinition() == typeof(Task<>))
                result = rt.GetProperty("Result")!.GetValue(task);
            else
                return Array.Empty<byte>();
        }

        if (result == null) return Array.Empty<byte>();

        // Serialize: if result is a known DataContract type, use DCS; otherwise wrap as response
        return SerializeResult(result, action, payload);
    }

    private static object?[] DeserializeParameters(byte[] payload, Type[] paramTypes)
    {
        if (paramTypes.Length == 0) return Array.Empty<object?>();
        if (payload.Length == 0)
            return paramTypes.Select(t => t.IsValueType ? Activator.CreateInstance(t) : null).ToArray();

        // If single complex parameter, try DataContractSerializer
        if (paramTypes.Length == 1 && !paramTypes[0].IsPrimitive && paramTypes[0] != typeof(string))
        {
            try
            {
                var dcs = new DataContractSerializer(paramTypes[0]);
                using var ms = new MemoryStream(payload);
                return new[] { dcs.ReadObject(ms) };
            }
            catch { /* fall through to XML parsing */ }
        }

        // Parse XML elements for individual parameters
        try
        {
            using var ms = new MemoryStream(payload);
            using var reader = System.Xml.XmlReader.Create(ms);
            reader.MoveToContent();
            if (reader.NodeType != System.Xml.XmlNodeType.Element) return FallbackParams(paramTypes);
            if (!reader.Read()) return FallbackParams(paramTypes);

            var values = new List<object?>();
            int i = 0;
            while (i < paramTypes.Length)
            {
                while (reader.NodeType != System.Xml.XmlNodeType.Element)
                    if (reader.NodeType == System.Xml.XmlNodeType.EndElement || !reader.Read()) break;
                if (reader.NodeType != System.Xml.XmlNodeType.Element) break;

                var text = reader.ReadElementContentAsString();
                values.Add(Convert.ChangeType(text, paramTypes[i],
                    System.Globalization.CultureInfo.InvariantCulture));
                i++;
            }
            if (values.Count == paramTypes.Length) return values.ToArray();
        }
        catch { }

        return FallbackParams(paramTypes);
    }

    private static object?[] FallbackParams(Type[] types) =>
        types.Select(t => t.IsValueType ? Activator.CreateInstance(t) : null).ToArray();

    private static byte[] SerializeResult(object result, string action, byte[] requestPayload)
    {
        var resultType = result.GetType();

        // If result is a complex type (likely a DataContract response), serialize directly
        if (!resultType.IsPrimitive && resultType != typeof(string) && resultType != typeof(decimal))
        {
            var dcs = new DataContractSerializer(resultType);
            using var ms = new MemoryStream();
            dcs.WriteObject(ms, result);
            return ms.ToArray();
        }

        // Primitive result — wrap in {Action}Response/{Action}Result XML
        // Extract namespace from request payload for consistency
        string ns = "";
        if (requestPayload.Length > 0)
        {
            try
            {
                using var rms = new MemoryStream(requestPayload);
                using var reader = System.Xml.XmlReader.Create(rms);
                reader.MoveToContent();
                ns = reader.NamespaceURI ?? "";
            }
            catch { }
        }

        var settings = new System.Xml.XmlWriterSettings { OmitXmlDeclaration = true };
        using var oms = new MemoryStream();
        using (var writer = System.Xml.XmlWriter.Create(oms, settings))
        {
            writer.WriteStartElement(action + "Response", ns);
            writer.WriteStartElement(action + "Result", ns);
            writer.WriteValue(Convert.ToString(result, System.Globalization.CultureInfo.InvariantCulture));
            writer.WriteEndElement();
            writer.WriteEndElement();
        }
        return oms.ToArray();
    }
}
