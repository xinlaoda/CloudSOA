using System.Reflection;
using Microsoft.Extensions.Logging;

namespace CloudSOA.ServiceHost.Loader;

/// <summary>
/// 动态加载用户服务 DLL 并发现 ISOAService 实现
/// </summary>
public class ServiceDllLoader
{
    private readonly ILogger<ServiceDllLoader> _logger;

    public ServiceDllLoader(ILogger<ServiceDllLoader> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 从指定路径加载 DLL，查找实现 ISOAService 的类型
    /// </summary>
    public ISOAService? LoadFromPath(string dllPath)
    {
        if (!File.Exists(dllPath))
        {
            _logger.LogError("Service DLL not found: {Path}", dllPath);
            return null;
        }

        try
        {
            var assembly = Assembly.LoadFrom(dllPath);
            var serviceType = assembly.GetTypes()
                .FirstOrDefault(t => typeof(ISOAService).IsAssignableFrom(t) && !t.IsAbstract);

            if (serviceType == null)
            {
                _logger.LogError("No ISOAService implementation found in {Path}", dllPath);
                return null;
            }

            var service = (ISOAService?)Activator.CreateInstance(serviceType);
            _logger.LogInformation("Loaded service '{ServiceName}' from {Path}",
                service?.ServiceName, dllPath);
            return service;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load service DLL: {Path}", dllPath);
            return null;
        }
    }

    /// <summary>
    /// 扫描目录下所有 DLL
    /// </summary>
    public IReadOnlyList<ISOAService> LoadFromDirectory(string directory)
    {
        var services = new List<ISOAService>();
        if (!Directory.Exists(directory)) return services;

        foreach (var dll in Directory.GetFiles(directory, "*.dll"))
        {
            var svc = LoadFromPath(dll);
            if (svc != null) services.Add(svc);
        }

        return services;
    }
}
