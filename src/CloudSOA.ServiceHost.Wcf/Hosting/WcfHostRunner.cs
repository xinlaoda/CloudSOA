using CloudSOA.ServiceHost.Wcf.Loader;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CloudSOA.ServiceHost.Wcf.Hosting;

/// <summary>
/// WCF Service Host runner — loads a legacy WCF DLL and starts the gRPC server.
/// </summary>
public static class WcfHostRunner
{
    public static WebApplication Build(string[] args, string? dllPath = null)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Force HTTP/2 for gRPC (Windows Kestrel defaults to HTTP/1.1)
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenAnyIP(5010, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
            });
        });

        builder.Services.AddGrpc();
        builder.Services.AddHealthChecks();

        var servicePath = dllPath
            ?? Environment.GetEnvironmentVariable("SERVICE_DLL_PATH")
            ?? "/app/services/service.dll";

        builder.Services.AddSingleton<WcfDllLoader>();
        builder.Services.AddSingleton<ISOAService>(sp =>
        {
            var loader = sp.GetRequiredService<WcfDllLoader>();
            var info = loader.LoadFromPath(servicePath);

            if (info != null)
            {
                var adapterLogger = sp.GetRequiredService<ILogger<WcfServiceAdapter>>();
                return new WcfServiceAdapter(info, adapterLogger);
            }

            // Fallback: built-in echo service for testing
            sp.GetRequiredService<ILoggerFactory>()
                .CreateLogger("CloudSOA.ServiceHost.Wcf.Hosting.WcfHostRunner")
                .LogWarning("No WCF service DLL found at {Path}, using echo service", servicePath);
            return new WcfEchoService();
        });

        var app = builder.Build();
        app.MapGrpcService<ComputeGrpcService>();
        app.MapHealthChecks("/healthz");
        app.MapGet("/", () => "CloudSOA WCF Service Host");

        return app;
    }
}

/// <summary>Built-in echo service for testing when no WCF DLL is provided.</summary>
internal class WcfEchoService : ISOAService
{
    public string ServiceName => "WcfEchoService";
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
