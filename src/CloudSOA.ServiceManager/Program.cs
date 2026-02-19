using Azure.Storage.Blobs;
using CloudSOA.ServiceManager.Services;
using Microsoft.Azure.Cosmos;

var builder = WebApplication.CreateBuilder(args);

// Azure Blob Storage
var blobConn = builder.Configuration.GetConnectionString("AzureBlobStorage")
    ?? "UseDevelopmentStorage=true";
builder.Services.AddSingleton(new BlobServiceClient(blobConn));
builder.Services.AddSingleton<BlobStorageService>();

// Azure CosmosDB
var cosmosConn = builder.Configuration.GetConnectionString("CosmosDB")
    ?? "AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";
builder.Services.AddSingleton(new CosmosClient(cosmosConn, new CosmosClientOptions
{
    SerializerOptions = new CosmosSerializationOptions
    {
        PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
    }
}));
builder.Services.AddSingleton<ServiceRegistrationStore>();

// Deployment service
builder.Services.AddSingleton<ServiceDeploymentService>();

// REST controllers + Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "CloudSOA Service Manager",
        Version = "v1",
        Description = "Manages SOA service registrations, blob uploads, and Kubernetes deployments."
    });
});

// CORS (allow all origins in development; restrict in production via config)
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

// Health checks
builder.Services.AddHealthChecks();

var app = builder.Build();

// Initialize storage backends
using (var scope = app.Services.CreateScope())
{
    var blob = scope.ServiceProvider.GetRequiredService<BlobStorageService>();
    await blob.InitializeAsync();
}

// Swagger (all environments for now; gate behind IsDevelopment in production)
app.UseSwagger();
app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "CloudSOA Service Manager v1"));

app.UseCors();
app.UseRouting();

app.MapControllers();
app.MapHealthChecks("/healthz");
app.MapGet("/", () => "CloudSOA Service Manager");

app.Run();
