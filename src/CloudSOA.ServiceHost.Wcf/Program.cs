using Azure.Storage.Blobs;
using CloudSOA.ServiceHost.Wcf.Hosting;

// Download service DLL from Blob Storage before starting the host
var blobConn = Environment.GetEnvironmentVariable("BLOB_CONNECTION");
var blobPath = Environment.GetEnvironmentVariable("BLOB_PATH");
var dllPath = Environment.GetEnvironmentVariable("SERVICE_DLL_PATH");

if (!string.IsNullOrEmpty(blobConn) && !string.IsNullOrEmpty(blobPath) && !string.IsNullOrEmpty(dllPath))
{
    var dir = Path.GetDirectoryName(dllPath)!;
    Directory.CreateDirectory(dir);

    Console.WriteLine($"Downloading service DLL from blob: {blobPath}");
    var blobService = new BlobServiceClient(blobConn);
    var container = blobService.GetBlobContainerClient("service-packages");

    // Download all blobs under the path (DLL + dependencies)
    await foreach (var item in container.GetBlobsAsync(Azure.Storage.Blobs.Models.BlobTraits.None, Azure.Storage.Blobs.Models.BlobStates.None, blobPath + "/", default))
    {
        var fileName = Path.GetFileName(item.Name);
        var localPath = Path.Combine(dir, fileName);
        var blob = container.GetBlobClient(item.Name);
        using var stream = File.Create(localPath);
        await blob.DownloadToAsync(stream);
        Console.WriteLine($"  Downloaded: {fileName} ({item.Properties.ContentLength} bytes)");
    }
    Console.WriteLine($"Service DLL ready at: {dllPath}");
}

WcfHostRunner.Build(args).Run();
