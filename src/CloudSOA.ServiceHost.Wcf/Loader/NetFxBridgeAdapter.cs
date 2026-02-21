using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace CloudSOA.ServiceHost.Wcf.Loader;

/// <summary>
/// Adapts a .NET Framework WCF service DLL by communicating with the NetFxBridge process.
/// The bridge runs on .NET Framework 4.8 and can load legacy DLLs that .NET 8 cannot.
/// </summary>
public class NetFxBridgeAdapter : ISOAService, IDisposable
{
    private readonly Process _bridge;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string _serviceName = "Unknown";
    private List<string> _actions = new();

    public string ServiceName => _serviceName;
    public IReadOnlyList<string> SupportedActions => _actions;

    private NetFxBridgeAdapter(Process bridge, ILogger logger)
    {
        _bridge = bridge;
        _logger = logger;
    }

    /// <summary>
    /// Start the NetFxBridge process and wait for it to load the DLL.
    /// </summary>
    public static NetFxBridgeAdapter? Start(string bridgeExePath, string dllPath, ILogger logger)
    {
        if (!File.Exists(bridgeExePath))
        {
            logger.LogError("NetFxBridge not found at {Path}", bridgeExePath);
            return null;
        }

        var psi = new ProcessStartInfo
        {
            FileName = bridgeExePath,
            Arguments = $"\"{dllPath}\"",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        var process = Process.Start(psi);
        if (process == null)
        {
            logger.LogError("Failed to start NetFxBridge process");
            return null;
        }

        // Read stderr for diagnostic messages
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                logger.LogInformation("[NetFxBridge] {Message}", e.Data);
        };
        process.BeginErrorReadLine();

        var adapter = new NetFxBridgeAdapter(process, logger);

        // Wait for "ready" message
        try
        {
            var readyLine = process.StandardOutput.ReadLine();
            if (readyLine == null)
            {
                logger.LogError("NetFxBridge exited without ready message");
                process.Kill();
                return null;
            }

            using var doc = JsonDocument.Parse(readyLine);
            var root = doc.RootElement;

            if (root.GetProperty("type").GetString() == "error")
            {
                logger.LogError("NetFxBridge error: {Message}", root.GetProperty("message").GetString());
                process.Kill();
                return null;
            }

            adapter._serviceName = root.GetProperty("service").GetString() ?? "Unknown";
            adapter._actions = root.GetProperty("actions").EnumerateArray()
                .Select(e => e.GetString()!)
                .ToList();

            logger.LogInformation(
                "NetFxBridge loaded service '{Service}' with {Count} operations (PID={Pid})",
                adapter._serviceName, adapter._actions.Count, process.Id);

            return adapter;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize NetFxBridge");
            if (!process.HasExited) process.Kill();
            return null;
        }
    }

    public async Task<byte[]> ExecuteAsync(string action, byte[] payload, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var request = JsonSerializer.Serialize(new
            {
                type = "invoke",
                action = action,
                payload = Convert.ToBase64String(payload)
            });

            await _bridge.StandardInput.WriteLineAsync(request);
            await _bridge.StandardInput.FlushAsync();

            var responseLine = await _bridge.StandardOutput.ReadLineAsync(ct);
            if (responseLine == null)
                throw new InvalidOperationException("NetFxBridge process terminated unexpectedly");

            using var doc = JsonDocument.Parse(responseLine);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString();

            if (type == "error")
            {
                var msg = root.GetProperty("message").GetString() ?? "Unknown error";
                throw new InvalidOperationException($"Service error: {msg}");
            }

            var resultB64 = root.GetProperty("payload").GetString() ?? "";
            return string.IsNullOrEmpty(resultB64)
                ? Array.Empty<byte>()
                : Convert.FromBase64String(resultB64);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        try
        {
            if (!_bridge.HasExited)
            {
                _bridge.StandardInput.Close();
                _bridge.WaitForExit(3000);
                if (!_bridge.HasExited) _bridge.Kill();
            }
        }
        catch { }
        _bridge.Dispose();
    }
}
