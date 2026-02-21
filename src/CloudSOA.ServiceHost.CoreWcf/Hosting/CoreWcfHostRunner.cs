using CloudSOA.ServiceHost.CoreWcf.Loader;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CloudSOA.ServiceHost.CoreWcf.Hosting;

public class CoreWcfHostRunner
{
    public static WebApplication Build(string[] args, string? dllPath = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddGrpc();
        builder.Services.AddHealthChecks();

        var servicePath = dllPath
            ?? Environment.GetEnvironmentVariable("SERVICE_DLL_PATH")
            ?? "/app/services/service.dll";

        builder.Services.AddSingleton<CoreWcfDllLoader>();
        builder.Services.AddSingleton<ISOAService>(sp =>
        {
            var loader = sp.GetRequiredService<CoreWcfDllLoader>();
            var info = loader.LoadFromPath(servicePath);

            if (info != null)
            {
                var logger = sp.GetRequiredService<ILogger<CoreWcfServiceAdapter>>();
                return new CoreWcfServiceAdapter(info, logger);
            }

            sp.GetRequiredService<ILogger<CoreWcfHostRunner>>()
                .LogWarning("No CoreWCF DLL at {Path}, using echo service", servicePath);
            return new EchoService();
        });

        var app = builder.Build();
        app.MapGrpcService<ComputeGrpcService>();
        app.MapHealthChecks("/healthz");
        app.MapGet("/", () => "CloudSOA CoreWCF Service Host");
        return app;
    }
}

public class EchoService : ISOAService
{
    public string ServiceName => "EchoService";
    public IReadOnlyList<string> SupportedActions => new[] { "Echo" };
    public Task<byte[]> ExecuteAsync(string action, byte[] payload, CancellationToken ct = default)
        => Task.FromResult(payload);
}
