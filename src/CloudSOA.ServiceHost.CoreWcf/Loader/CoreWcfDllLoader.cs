using System.Reflection;
using System.ServiceModel;
using Microsoft.Extensions.Logging;

namespace CloudSOA.ServiceHost.CoreWcf.Loader;

/// <summary>
/// Information about a discovered WCF/CoreWCF service.
/// </summary>
public record CoreWcfServiceInfo(
    Type ContractType,
    Type ImplementationType,
    string ServiceName,
    List<CoreWcfMethodInfo> Methods);

public record CoreWcfMethodInfo(
    string ActionName,
    MethodInfo Method,
    Type[] ParameterTypes);

/// <summary>
/// Loads a .NET 8 DLL compiled with CoreWCF or System.ServiceModel attributes.
/// Unlike the NetFxBridge, this loads assemblies directly since they target .NET 8.
/// </summary>
public class CoreWcfDllLoader
{
    private readonly ILogger<CoreWcfDllLoader> _logger;

    public CoreWcfDllLoader(ILogger<CoreWcfDllLoader> logger) => _logger = logger;

    public CoreWcfServiceInfo? LoadFromPath(string dllPath)
    {
        if (!File.Exists(dllPath))
        {
            _logger.LogError("Service DLL not found: {Path}", dllPath);
            return null;
        }

        try
        {
            var assembly = Assembly.LoadFrom(dllPath);
            return DiscoverService(assembly);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load CoreWCF service DLL: {Path}", dllPath);
            return null;
        }
    }

    private CoreWcfServiceInfo? DiscoverService(Assembly assembly)
    {
        // Find [ServiceContract] interface
        var contractType = assembly.GetTypes()
            .FirstOrDefault(t => t.IsInterface &&
                t.GetCustomAttribute<ServiceContractAttribute>() is not null);

        if (contractType is null)
        {
            _logger.LogWarning("No [ServiceContract] interface found in assembly");
            return null;
        }

        // Find implementation
        var implType = assembly.GetTypes()
            .FirstOrDefault(t => !t.IsAbstract && contractType.IsAssignableFrom(t));

        if (implType is null)
        {
            _logger.LogWarning("No implementation found for {Contract}", contractType.Name);
            return null;
        }

        var methods = new List<CoreWcfMethodInfo>();
        foreach (var method in contractType.GetMethods())
        {
            var opAttr = method.GetCustomAttribute<OperationContractAttribute>();
            if (opAttr is null) continue;

            var name = string.IsNullOrEmpty(opAttr.Name) ? method.Name : opAttr.Name;
            var paramTypes = method.GetParameters().Select(p => p.ParameterType).ToArray();
            var implMethod = implType.GetMethod(method.Name, paramTypes) ?? method;

            methods.Add(new CoreWcfMethodInfo(name, implMethod, paramTypes));
        }

        _logger.LogInformation("Loaded CoreWCF service {Contract} with {Count} operations from {Impl}",
            contractType.Name, methods.Count, implType.Name);

        return new CoreWcfServiceInfo(contractType, implType, contractType.Name, methods);
    }
}
