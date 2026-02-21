using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.ServiceModel;
using Newtonsoft.Json;

namespace CloudSOA.NetFxBridge
{
    /// <summary>
    /// .NET Framework 4.8 bridge process.
    /// Loads a WCF service DLL and executes methods via stdin/stdout JSON protocol.
    /// 
    /// Protocol:
    ///   → {"type":"discover"}
    ///   ← {"type":"discovered","service":"ICalculator","actions":["Add"]}
    ///   → {"type":"invoke","action":"Add","payload":"base64..."}
    ///   ← {"type":"result","payload":"base64..."}
    ///   ← {"type":"error","message":"..."}
    /// </summary>
    class Program
    {
        static Dictionary<string, MethodInfo> _methods;
        static Dictionary<string, Type[]> _paramTypes;
        static object _instance;
        static string _serviceName;

        static void Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.Error.WriteLine("Usage: NetFxBridge.exe <dll-path>");
                Environment.Exit(1);
            }

            var dllPath = args[0];
            if (!File.Exists(dllPath))
            {
                WriteError("DLL not found: " + dllPath);
                Environment.Exit(1);
            }

            try
            {
                LoadService(dllPath);
            }
            catch (Exception ex)
            {
                WriteError("Failed to load DLL: " + ex.Message);
                Environment.Exit(1);
            }

            // Signal ready
            WriteJson(new { type = "ready", service = _serviceName, actions = _methods.Keys.ToArray() });

            // Process commands from stdin
            string line;
            while ((line = Console.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    var cmd = JsonConvert.DeserializeObject<Dictionary<string, string>>(line);
                    var cmdType = cmd.ContainsKey("type") ? cmd["type"] : "";

                    switch (cmdType)
                    {
                        case "discover":
                            WriteJson(new { type = "discovered", service = _serviceName, actions = _methods.Keys.ToArray() });
                            break;

                        case "invoke":
                            var action = cmd.ContainsKey("action") ? cmd["action"] : "";
                            var payloadB64 = cmd.ContainsKey("payload") ? cmd["payload"] : "";
                            var payload = string.IsNullOrEmpty(payloadB64)
                                ? new byte[0]
                                : Convert.FromBase64String(payloadB64);

                            var result = Invoke(action, payload);
                            WriteJson(new { type = "result", payload = Convert.ToBase64String(result) });
                            break;

                        default:
                            WriteError("Unknown command: " + cmdType);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    WriteError(ex.InnerException != null ? ex.InnerException.Message : ex.Message);
                }
            }
        }

        static void LoadService(string dllPath)
        {
            var assembly = Assembly.LoadFrom(dllPath);

            // Find [ServiceContract] interface
            var contractType = assembly.GetTypes()
                .FirstOrDefault(t => t.IsInterface &&
                    t.GetCustomAttribute(typeof(ServiceContractAttribute), false) != null);

            if (contractType == null)
                throw new Exception("No [ServiceContract] interface found");

            // Find implementation
            var implType = assembly.GetTypes()
                .FirstOrDefault(t => !t.IsAbstract && contractType.IsAssignableFrom(t));

            if (implType == null)
                throw new Exception("No implementation found for " + contractType.Name);

            _instance = Activator.CreateInstance(implType);
            _serviceName = contractType.Name;
            _methods = new Dictionary<string, MethodInfo>(StringComparer.OrdinalIgnoreCase);
            _paramTypes = new Dictionary<string, Type[]>(StringComparer.OrdinalIgnoreCase);

            foreach (var method in contractType.GetMethods())
            {
                var opAttr = method.GetCustomAttribute(typeof(OperationContractAttribute), false)
                    as OperationContractAttribute;
                if (opAttr == null) continue;

                var name = string.IsNullOrEmpty(opAttr.Name) ? method.Name : opAttr.Name;
                var implMethod = implType.GetMethod(method.Name,
                    method.GetParameters().Select(p => p.ParameterType).ToArray());

                _methods[name] = implMethod ?? method;
                _paramTypes[name] = method.GetParameters().Select(p => p.ParameterType).ToArray();
            }

            Console.Error.WriteLine("[NetFxBridge] Loaded " + contractType.Name +
                " with " + _methods.Count + " operations from " + implType.Name);
        }

        static byte[] Invoke(string action, byte[] payload)
        {
            if (!_methods.ContainsKey(action))
                throw new Exception("Unknown action: " + action);

            var method = _methods[action];
            var types = _paramTypes[action];

            // Parse request XML to extract namespace and parameters
            string requestNamespace = "";
            object[] parameters;
            if (payload.Length > 0)
            {
                using (var ms = new MemoryStream(payload))
                using (var reader = System.Xml.XmlReader.Create(ms))
                {
                    reader.MoveToContent();
                    requestNamespace = reader.NamespaceURI ?? "";
                }
                parameters = DeserializeParams(payload, types);
            }
            else
            {
                parameters = types.Select(t => t.IsValueType ? Activator.CreateInstance(t) : null).ToArray();
            }

            // Invoke
            var result = method.Invoke(_instance, parameters);

            // Construct WCF-style response wrapper:
            // <{Action}Response xmlns="{ns}"><{Action}Result>{value}</{Action}Result></{Action}Response>
            var settings = new System.Xml.XmlWriterSettings { OmitXmlDeclaration = true };
            using (var ms = new MemoryStream())
            {
                using (var writer = System.Xml.XmlWriter.Create(ms, settings))
                {
                    writer.WriteStartElement(action + "Response", requestNamespace);
                    if (result != null)
                    {
                        writer.WriteStartElement(action + "Result", requestNamespace);
                        writer.WriteValue(Convert.ToString(result, System.Globalization.CultureInfo.InvariantCulture));
                        writer.WriteEndElement();
                    }
                    writer.WriteEndElement();
                }
                return ms.ToArray();
            }
        }

        static object[] DeserializeParams(byte[] payload, Type[] types)
        {
            if (types.Length == 0) return new object[0];
            if (payload.Length == 0)
                return types.Select(t => t.IsValueType ? Activator.CreateInstance(t) : null).ToArray();

            using (var ms = new MemoryStream(payload))
            using (var reader = System.Xml.XmlReader.Create(ms))
            {
                reader.MoveToContent();
                var values = new List<object>();
                if (!reader.Read()) return types.Select(t => t.IsValueType ? Activator.CreateInstance(t) : null).ToArray();

                int i = 0;
                while (i < types.Length)
                {
                    while (reader.NodeType != System.Xml.XmlNodeType.Element)
                    {
                        if (reader.NodeType == System.Xml.XmlNodeType.EndElement || !reader.Read()) break;
                    }
                    if (reader.NodeType != System.Xml.XmlNodeType.Element) break;

                    var text = reader.ReadElementContentAsString();
                    values.Add(Convert.ChangeType(text, types[i],
                        System.Globalization.CultureInfo.InvariantCulture));
                    i++;
                }

                if (values.Count == types.Length) return values.ToArray();
            }

            return types.Select(t => t.IsValueType ? Activator.CreateInstance(t) : null).ToArray();
        }

        static void WriteJson(object obj)
        {
            Console.WriteLine(JsonConvert.SerializeObject(obj));
        }

        static void WriteError(string message)
        {
            Console.WriteLine(JsonConvert.SerializeObject(new { type = "error", message = message }));
        }
    }
}
