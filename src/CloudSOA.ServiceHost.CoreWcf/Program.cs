using Azure.Storage.Blobs;
using CloudSOA.ServiceHost.CoreWcf.Hosting;

// Download service DLL from blob storage (same pattern as other service hosts)
var blobConn = Environment.GetEnvironmentVariable("BLOB_CONNECTION");
var blobPath = Environment.GetEnvironmentVariable("BLOB_PATH");
var servicesDir = "/app/services";

if (!string.IsNullOrEmpty(blobConn) && !string.IsNullOrEmpty(blobPath))
{
    Console.WriteLine($"Downloading service DLL from blob: {blobPath}");
    Directory.CreateDirectory(servicesDir);

    var blobClient = new BlobServiceClient(blobConn);
    var container = blobClient.GetBlobContainerClient("service-packages");

    await foreach (var item in container.GetBlobsAsync(Azure.Storage.Blobs.Models.BlobTraits.None, Azure.Storage.Blobs.Models.BlobStates.None, blobPath, default))
    {
        var fileName = Path.GetFileName(item.Name);
        var localPath = Path.Combine(servicesDir, fileName);

        var blob = container.GetBlobClient(item.Name);
        using var fs = File.Create(localPath);
        await blob.DownloadToAsync(fs);
        Console.WriteLine($"  Downloaded: {fileName} ({item.Properties.ContentLength} bytes)");
    }

    var dllPath = Path.Combine(servicesDir,
        Environment.GetEnvironmentVariable("SERVICE_NAME") + ".dll");
    if (File.Exists(dllPath))
        Console.WriteLine($"Service DLL ready at: {dllPath}");
}

CoreWcfHostRunner.Build(args).Run();
