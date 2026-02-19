using CloudSOA.Portal.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpClient<BrokerApiClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["ApiEndpoints:Broker"] ?? "http://localhost:5000");
});

builder.Services.AddHttpClient<ServiceManagerApiClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["ApiEndpoints:ServiceManager"] ?? "http://localhost:5030");
});

builder.Services.AddHealthChecks();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapHealthChecks("/healthz");
app.MapRazorComponents<CloudSOA.Portal.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
