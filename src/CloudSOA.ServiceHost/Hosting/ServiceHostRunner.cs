using CloudSOA.ServiceHost.Hosting;
using CloudSOA.ServiceHost.Loader;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CloudSOA.ServiceHost.Hosting;

/// <summary>
/// Service Host 启动器 — 加载 DLL 并启动 gRPC server
/// </summary>
public class ServiceHostRunner
{
    /// <summary>
    /// 启动 Service Host：加载用户 DLL，启动 gRPC 端口
    /// </summary>
    public static WebApplication Build(string[] args, string? dllPath = null)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Configure Kestrel for gRPC (HTTP/2)
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.ListenAnyIP(5010, o =>
                o.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2);
        });

        builder.Services.AddGrpc();
        builder.Services.AddHealthChecks();

        // 加载用户服务 DLL
        var servicePath = dllPath
            ?? Environment.GetEnvironmentVariable("SERVICE_DLL_PATH")
            ?? "/app/services/service.dll";

        builder.Services.AddSingleton<ServiceDllLoader>();
        builder.Services.AddSingleton<ISOAService>(sp =>
        {
            var loader = sp.GetRequiredService<ServiceDllLoader>();
            var service = loader.LoadFromPath(servicePath);
            if (service != null) return service;

            // Fallback: echo service for testing
            sp.GetRequiredService<ILogger<ServiceHostRunner>>()
                .LogWarning("No service DLL found at {Path}, using echo service", servicePath);
            return new EchoService();
        });

        var app = builder.Build();
        app.MapGrpcService<ComputeGrpcService>();
        app.MapHealthChecks("/healthz");
        app.MapGet("/", () => "CloudSOA Service Host");

        return app;
    }
}

/// <summary>内置 Echo 服务，用于测试</summary>
public class EchoService : ISOAService
{
    public string ServiceName => "EchoService";
    public IReadOnlyList<string> SupportedActions => new[] { "Echo", "Reverse" };

    public Task<byte[]> ExecuteAsync(string action, byte[] payload, CancellationToken ct = default)
    {
        return action switch
        {
            "Reverse" => Task.FromResult(payload.Reverse().ToArray()),
            _ => Task.FromResult(payload) // Echo
        };
    }
}
