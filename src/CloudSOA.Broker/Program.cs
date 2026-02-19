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

// Phase 5: API Key auth middleware (skipped if no key configured)
app.UseMiddleware<ApiKeyMiddleware>();

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
