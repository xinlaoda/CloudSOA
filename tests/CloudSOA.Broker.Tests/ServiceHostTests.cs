using CloudSOA.ServiceHost.Hosting;
using CloudSOA.ServiceHost.Loader;

namespace CloudSOA.Broker.Tests;

public class ServiceHostTests
{
    [Fact]
    public async Task EchoService_Echo_ReturnsSamePayload()
    {
        var service = new EchoService();
        var payload = new byte[] { 1, 2, 3, 4, 5 };

        var result = await service.ExecuteAsync("Echo", payload);

        Assert.Equal(payload, result);
    }

    [Fact]
    public async Task EchoService_Reverse_ReturnsReversedPayload()
    {
        var service = new EchoService();
        var payload = new byte[] { 1, 2, 3, 4, 5 };

        var result = await service.ExecuteAsync("Reverse", payload);

        Assert.Equal(new byte[] { 5, 4, 3, 2, 1 }, result);
    }

    [Fact]
    public void EchoService_Properties()
    {
        var service = new EchoService();
        Assert.Equal("EchoService", service.ServiceName);
        Assert.Contains("Echo", service.SupportedActions);
        Assert.Contains("Reverse", service.SupportedActions);
    }

    [Fact]
    public void ServiceDllLoader_NonExistentPath_ReturnsNull()
    {
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<ServiceDllLoader>();
        var loader = new ServiceDllLoader(logger);

        var result = loader.LoadFromPath("/nonexistent/service.dll");

        Assert.Null(result);
    }

    [Fact]
    public void ServiceDllLoader_NonExistentDirectory_ReturnsEmpty()
    {
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<ServiceDllLoader>();
        var loader = new ServiceDllLoader(logger);

        var result = loader.LoadFromDirectory("/nonexistent/dir");

        Assert.Empty(result);
    }
}
