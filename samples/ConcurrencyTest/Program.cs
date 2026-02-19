using CloudSOA.Client;
using System.Diagnostics;

var brokerUrl = args.Length > 0 ? args[0] : "http://localhost:5000";
var concurrency = args.Length > 1 ? int.Parse(args[1]) : 10;
var totalCalls = args.Length > 2 ? int.Parse(args[2]) : 100;

Console.WriteLine($"CloudSOA Auto-Scaling Load Test");
Console.WriteLine($"Broker:      {brokerUrl}");
Console.WriteLine($"Workers:     {concurrency} (1 session each)");
Console.WriteLine($"Total calls: {totalCalls}");
Console.WriteLine($"Calls/worker: {totalCalls / concurrency}");
Console.WriteLine(new string('=', 60));

// Snapshot metrics before test
double? preProcessed = null;
try
{
    using var hc = new HttpClient();
    var metrics = await hc.GetStringAsync($"{brokerUrl}/metrics");
    foreach (var line in metrics.Split('\n'))
    {
        if (line.StartsWith("cloudsoa_requests_processed_total "))
            preProcessed = double.Parse(line.Split(' ')[1]);
    }
}
catch { /* metrics endpoint may not be accessible */ }

var sw = Stopwatch.StartNew();
var completed = 0;
var failed = 0;
var responseTimes = new List<double>();
var lockObj = new object();

var callsPerWorker = totalCalls / concurrency;
var remainder = totalCalls % concurrency;

var tasks = new List<Task>();
for (int w = 0; w < concurrency; w++)
{
    var workerCalls = callsPerWorker + (w < remainder ? 1 : 0);
    var workerId = w;
    tasks.Add(Task.Run(async () =>
    {
        try
        {
            // One session per worker — all requests go through this session
            using var session = await CloudSession.CreateSessionAsync(
                new SessionStartInfo(brokerUrl, "CalculatorService"));

            using var client = new CloudBrokerClient(session);

            for (int i = 0; i < workerCalls; i++)
            {
                var callSw = Stopwatch.StartNew();
                try
                {
                    var payload = $"Worker{workerId}-Call{i}: {DateTime.UtcNow:O}";
                    client.SendRequest("Echo", payload);
                    await client.EndRequestsAsync();

                    var responses = await client.GetAllResponsesAsync(1, TimeSpan.FromSeconds(30));

                    callSw.Stop();
                    lock (lockObj)
                    {
                        Interlocked.Increment(ref completed);
                        responseTimes.Add(callSw.Elapsed.TotalMilliseconds);
                    }

                    var c = Interlocked.CompareExchange(ref completed, 0, 0);
                    if (c % 50 == 0)
                        Console.Write($"\n  [{c}/{totalCalls}]");
                    else if (c % 10 == 0)
                        Console.Write(".");
                }
                catch (Exception ex)
                {
                    callSw.Stop();
                    Interlocked.Increment(ref failed);
                    Console.Error.WriteLine($"\n  [W{workerId}#{i}] Error: {ex.Message}");
                }
            }

            await session.CloseAsync();
        }
        catch (Exception ex)
        {
            // Session creation failed — count all calls as failed
            Interlocked.Add(ref failed, workerCalls);
            Console.Error.WriteLine($"\n  [W{workerId}] Session error: {ex.Message}");
        }
    }));
}

await Task.WhenAll(tasks);
sw.Stop();

// Snapshot metrics after test
double? postProcessed = null;
try
{
    using var hc = new HttpClient();
    var metrics = await hc.GetStringAsync($"{brokerUrl}/metrics");
    foreach (var line in metrics.Split('\n'))
    {
        if (line.StartsWith("cloudsoa_requests_processed_total "))
            postProcessed = double.Parse(line.Split(' ')[1]);
    }
}
catch { /* metrics endpoint may not be accessible */ }

Console.WriteLine();
Console.WriteLine(new string('=', 60));
Console.WriteLine($"RESULTS");
Console.WriteLine(new string('=', 60));
Console.WriteLine($"  Total requests:  {completed + failed}");
Console.WriteLine($"  Succeeded:       {completed}");
Console.WriteLine($"  Failed:          {failed}");
Console.WriteLine($"  Sessions:        {concurrency}");
Console.WriteLine($"  Duration:        {sw.Elapsed.TotalSeconds:F1}s");
Console.WriteLine($"  Throughput:      {(completed + failed) / sw.Elapsed.TotalSeconds:F1} req/sec");

if (responseTimes.Count > 0)
{
    responseTimes.Sort();
    Console.WriteLine();
    Console.WriteLine($"  LATENCY (per request, client-side)");
    Console.WriteLine($"    Min:    {responseTimes.First():F0}ms");
    Console.WriteLine($"    Avg:    {responseTimes.Average():F0}ms");
    Console.WriteLine($"    P50:    {responseTimes[(int)(responseTimes.Count * 0.50)]:F0}ms");
    Console.WriteLine($"    P90:    {responseTimes[(int)(responseTimes.Count * 0.90)]:F0}ms");
    Console.WriteLine($"    P95:    {responseTimes[(int)(responseTimes.Count * 0.95)]:F0}ms");
    Console.WriteLine($"    P99:    {responseTimes[(int)(responseTimes.Count * 0.99)]:F0}ms");
    Console.WriteLine($"    Max:    {responseTimes.Last():F0}ms");
}

if (preProcessed.HasValue && postProcessed.HasValue)
{
    Console.WriteLine();
    Console.WriteLine($"  SERVER METRICS (Prometheus)");
    Console.WriteLine($"    Requests processed: {postProcessed.Value - preProcessed.Value:F0}");
    Console.WriteLine($"    Server throughput:  {(postProcessed.Value - preProcessed.Value) / sw.Elapsed.TotalSeconds:F1} req/sec");
}

Console.WriteLine(new string('=', 60));
