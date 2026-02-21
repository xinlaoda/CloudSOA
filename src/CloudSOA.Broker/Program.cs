using CloudSOA.Broker.Dispatch;
using CloudSOA.Broker.HA;
using CloudSOA.Broker.Middleware;
using CloudSOA.Broker.Queue;
using CloudSOA.Broker.Services;
using CloudSOA.Broker.Storage;
using CloudSOA.Common.Interfaces;
using Prometheus;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// TLS mode: "direct" (Kestrel TLS), "ingress" (TLS at ingress), "none" (dev only)
var tlsMode = builder.Configuration["Tls:Mode"] ?? "none";
var certPath = builder.Configuration["Tls:CertPath"];
var certPassword = builder.Configuration["Tls:CertPassword"];
var certKeyPath = builder.Configuration["Tls:KeyPath"];

if (string.Equals(tlsMode, "direct", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(certPath))
{
    builder.WebHost.ConfigureKestrel(kestrel =>
    {
        // HTTPS endpoint for REST clients
        kestrel.ListenAnyIP(5000, o => o.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1AndHttp2);
        kestrel.ListenAnyIP(5443, o =>
        {
            o.UseHttps(certPath, certPassword);
            o.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1AndHttp2;
        });
        // gRPC over TLS
        kestrel.ListenAnyIP(5001, o => o.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2);
        kestrel.ListenAnyIP(5444, o =>
        {
            o.UseHttps(certPath, certPassword);
            o.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2;
        });
    });
    Console.WriteLine($"[TLS] Direct mode: HTTPS on :5443, gRPC+TLS on :5444 (cert: {certPath})");
}
else if (string.Equals(tlsMode, "ingress", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("[TLS] Ingress mode: TLS terminated at ingress controller, internal traffic is plain HTTP");
}
else
{
    Console.WriteLine("[TLS] No TLS configured (development mode). Set Tls:Mode=direct or Tls:Mode=ingress for production.");
}

// Redis
var redisConn = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect(redisConn));

// Core services
builder.Services.AddSingleton<ISessionStore, RedisSessionStore>();
builder.Services.AddSingleton<ISessionManager, SessionManagerService>();

// Phase 2: Queue, Dispatcher, Response Cache
builder.Services.AddSingleton<IRequestQueue, RedisRequestQueue>();
builder.Services.AddSingleton<IResponseStore, RedisResponseStore>();
builder.Services.AddSingleton<ServiceRouter>();
builder.Services.AddSingleton<IDispatcherEngine>(sp =>
{
    var engine = new DispatcherEngine(
        sp.GetRequiredService<IRequestQueue>(),
        sp.GetRequiredService<IResponseStore>(),
        sp.GetRequiredService<ILogger<DispatcherEngine>>());
    var router = sp.GetRequiredService<ServiceRouter>();
    engine.RequestHandler = req => router.ExecuteAsync(req);
    return engine;
});
builder.Services.AddSingleton<FlowController>();

// Phase 5: HA + Metrics
builder.Services.AddSingleton<LeaderElection>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LeaderElection>());

// REST + gRPC
builder.Services.AddControllers();
builder.Services.AddGrpc();

// Health check
builder.Services.AddHealthChecks();

var app = builder.Build();

// HTTPS redirect in direct TLS mode
if (string.Equals(tlsMode, "direct", StringComparison.OrdinalIgnoreCase))
{
    app.Use(async (context, next) =>
    {
        // Redirect HTTP (5000) to HTTPS (5443) for non-health/metrics paths
        if (!context.Request.IsHttps)
        {
            var path = context.Request.Path.Value ?? "";
            if (!path.Equals("/healthz", StringComparison.OrdinalIgnoreCase) &&
                !path.Equals("/metrics", StringComparison.OrdinalIgnoreCase))
            {
                var host = context.Request.Host.Host;
                var redirectUrl = $"https://{host}:5443{context.Request.Path}{context.Request.QueryString}";
                context.Response.StatusCode = 308;
                context.Response.Headers["Location"] = redirectUrl;
                return;
            }
        }
        await next();
    });
}

// Authentication middleware (JWT / API Key / none)
app.UseMiddleware<AuthenticationMiddleware>();

// Authorization middleware (RBAC: Admin / User / Reader)
app.UseMiddleware<AuthorizationMiddleware>();

// Prometheus metrics endpoint
app.UseRouting();
app.UseHttpMetrics();
CloudSOA.Broker.Metrics.BrokerMetrics.Initialize();

app.MapControllers();
app.MapGrpcService<BrokerGrpcService>();
app.MapHealthChecks("/healthz");
app.MapMetrics("/metrics"); // Prometheus scrape endpoint
app.MapGet("/", () => "CloudSOA Broker Service");

app.Run();
