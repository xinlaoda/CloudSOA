using System.Reflection;
using System.ServiceModel;
using Microsoft.Extensions.Logging;

namespace CloudSOA.ServiceHost.Wcf.Loader;

/// <summary>
/// Loads a WCF service DLL and discovers [ServiceContract] / [OperationContract] types.
/// </summary>
public class WcfDllLoader
{
    private readonly ILogger<WcfDllLoader> _logger;

    public WcfDllLoader(ILogger<WcfDllLoader> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Load a DLL and discover WCF service contracts.
    /// </summary>
    public WcfServiceInfo? LoadFromPath(string dllPath)
    {
        if (!File.Exists(dllPath))
        {
            _logger.LogError("WCF service DLL not found: {Path}", dllPath);
            return null;
        }

        try
        {
            var assembly = Assembly.LoadFrom(dllPath);
            return DiscoverService(assembly);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load WCF service DLL: {Path}", dllPath);
            return null;
        }
    }

    private WcfServiceInfo? DiscoverService(Assembly assembly)
    {
        // Find interface with [ServiceContract]
        var contractType = assembly.GetTypes()
            .FirstOrDefault(t => t.IsInterface &&
                t.GetCustomAttribute<ServiceContractAttribute>() != null);

        if (contractType == null)
        {
            // Also check concrete classes for [ServiceContract]
            contractType = assembly.GetTypes()
                .FirstOrDefault(t => !t.IsAbstract &&
                    t.GetCustomAttribute<ServiceContractAttribute>() != null);
        }

        if (contractType == null)
        {
            _logger.LogError("No type with [ServiceContract] found in assembly {Assembly}",
                assembly.GetName().Name);
            return null;
        }

        // Find implementation type (if contract is an interface, find the implementing class)
        Type implType;
        if (contractType.IsInterface)
        {
            var impl = assembly.GetTypes()
                .FirstOrDefault(t => !t.IsAbstract && contractType.IsAssignableFrom(t));

            if (impl == null)
            {
                _logger.LogError("No implementation found for contract {Contract}", contractType.Name);
                return null;
            }
            implType = impl;
        }
        else
        {
            implType = contractType;
        }

        // Get service name from [ServiceContract(Name=...)]
        var scAttr = contractType.GetCustomAttribute<ServiceContractAttribute>();
        var serviceName = !string.IsNullOrEmpty(scAttr?.Name)
            ? scAttr.Name
            : contractType.Name;

        // Discover [OperationContract] methods
        var methods = new List<WcfMethodInfo>();
        var targetType = contractType.IsInterface ? contractType : implType;

        foreach (var method in targetType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            var opAttr = method.GetCustomAttribute<OperationContractAttribute>();
            if (opAttr == null) continue;

            var actionName = !string.IsNullOrEmpty(opAttr.Name)
                ? opAttr.Name
                : method.Name;

            methods.Add(new WcfMethodInfo
            {
                ActionName = actionName,
                Method = implType.GetMethod(method.Name, method.GetParameters().Select(p => p.ParameterType).ToArray()) ?? method,
                ParameterTypes = method.GetParameters().Select(p => p.ParameterType).ToArray(),
                ReturnType = method.ReturnType
            });
        }

        if (methods.Count == 0)
        {
            _logger.LogError("No [OperationContract] methods found on {Type}", contractType.Name);
            return null;
        }

        _logger.LogInformation(
            "Discovered WCF service '{ServiceName}' with {Count} operations from {Type}",
            serviceName, methods.Count, implType.Name);

        return new WcfServiceInfo
        {
            ServiceName = serviceName,
            ImplementationType = implType,
            ContractType = contractType,
            Methods = methods
        };
    }
}
