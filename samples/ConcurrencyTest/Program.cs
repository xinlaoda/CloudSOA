using CloudSOA.Client;
using System.Diagnostics;

var brokerUrl = args.Length > 0 ? args[0] : "http://localhost:5000";
var concurrency = args.Length > 1 ? int.Parse(args[1]) : 10;
var totalCalls = args.Length > 2 ? int.Parse(args[2]) : 100;

Console.WriteLine($"CloudSOA Concurrency Test");
Console.WriteLine($"Broker:      {brokerUrl}");
Console.WriteLine($"Concurrency: {concurrency} parallel sessions");
Console.WriteLine($"Total calls: {totalCalls}");
Console.WriteLine(new string('=', 60));

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
        for (int i = 0; i < workerCalls; i++)
        {
            var callSw = Stopwatch.StartNew();
            try
            {
                using var session = await CloudSession.CreateSessionAsync(
                    new SessionStartInfo(brokerUrl, "CalculatorService"));

                using var client = new CloudBrokerClient(session);
                var payload = $"Worker{workerId}-Call{i}: {DateTime.UtcNow:O}";
                client.SendRequest("Echo", payload);
                await client.EndRequestsAsync();

                var responses = await client.GetAllResponsesAsync(1, TimeSpan.FromSeconds(30));
                await session.CloseAsync();

                callSw.Stop();
                lock (lockObj)
                {
                    Interlocked.Increment(ref completed);
                    responseTimes.Add(callSw.Elapsed.TotalMilliseconds);
                }

                if (Interlocked.CompareExchange(ref completed, 0, 0) % 10 == 0)
                    Console.Write(".");
            }
            catch (Exception ex)
            {
                callSw.Stop();
                Interlocked.Increment(ref failed);
                Console.Error.WriteLine($"\n  [W{workerId}] Error: {ex.Message}");
            }
        }
    }));
}

await Task.WhenAll(tasks);
sw.Stop();

Console.WriteLine();
Console.WriteLine(new string('=', 60));
Console.WriteLine($"Results:");
Console.WriteLine($"  Total:      {completed + failed}");
Console.WriteLine($"  Succeeded:  {completed}");
Console.WriteLine($"  Failed:     {failed}");
Console.WriteLine($"  Duration:   {sw.Elapsed.TotalSeconds:F1}s");
Console.WriteLine($"  Throughput: {(completed + failed) / sw.Elapsed.TotalSeconds:F1} calls/sec");

if (responseTimes.Count > 0)
{
    responseTimes.Sort();
    Console.WriteLine($"  Latency:");
    Console.WriteLine($"    Min:  {responseTimes.First():F0}ms");
    Console.WriteLine($"    Avg:  {responseTimes.Average():F0}ms");
    Console.WriteLine($"    P50:  {responseTimes[(int)(responseTimes.Count * 0.5)]:F0}ms");
    Console.WriteLine($"    P95:  {responseTimes[(int)(responseTimes.Count * 0.95)]:F0}ms");
    Console.WriteLine($"    P99:  {responseTimes[(int)(responseTimes.Count * 0.99)]:F0}ms");
    Console.WriteLine($"    Max:  {responseTimes.Last():F0}ms");
}
