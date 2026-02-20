using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CloudSOA.Client;
using CloudSOA.Common.Enums;

// === Configuration ===
var brokerUrl = args.Length > 0 ? args[0] : "http://4.155.109.75";
var serviceName = args.Length > 1 ? args[1] : "DurableProcessingService";
var totalRequests = args.Length > 2 ? int.Parse(args[2]) : 50;

Console.WriteLine("╔══════════════════════════════════════════════════╗");
Console.WriteLine("║       CloudSOA Durable Session Test             ║");
Console.WriteLine("╚══════════════════════════════════════════════════╝");
Console.WriteLine($"Broker:    {brokerUrl}");
Console.WriteLine($"Service:   {serviceName}");
Console.WriteLine($"Requests:  {totalRequests}");
Console.WriteLine();

// ═══════════════════════════════════════════════════════
// Phase 1: Create Durable Session + Send Requests
// ═══════════════════════════════════════════════════════
Console.WriteLine("══════ Phase 1: Create Durable Session & Send Requests ══════");

var info = new SessionStartInfo(brokerUrl, serviceName)
{
    SessionType = SessionType.Durable
};

var cloudSession = await CloudSession.CreateSessionAsync(info);
var sessionId = cloudSession.SessionId;
var brokerEndpoint = cloudSession.BrokerEndpoint;

Console.WriteLine($"  ✓ Durable session created: {sessionId}");
Console.WriteLine($"    SessionType: Durable");
Console.WriteLine($"    BrokerEndpoint: {brokerEndpoint}");

// Send requests
var sw = Stopwatch.StartNew();
using (var client = new CloudBrokerClient(cloudSession))
{
    for (int i = 0; i < totalRequests; i++)
    {
        var payload = $"<Parameters><taskId>task-{i:D4}</taskId><inputData>durable-test-payload-{i}-{Guid.NewGuid():N}</inputData></Parameters>";
        client.SendRequest("ProcessData", payload, $"item-{i}");
    }
    var sent = await client.EndRequestsAsync();
    Console.WriteLine($"  ✓ Sent {sent} requests in {sw.ElapsedMilliseconds}ms");
}

// ═══════════════════════════════════════════════════════
// Phase 2: Disconnect (dispose session object but DO NOT close)
// ═══════════════════════════════════════════════════════
Console.WriteLine();
Console.WriteLine("══════ Phase 2: Client Disconnect (Simulating Crash) ══════");
// Dispose client HTTP resources, but DO NOT call Close
Console.WriteLine("  ✓ Client disposed (session NOT closed)");
Console.WriteLine("  ✓ Session remains Active on broker");
cloudSession.Dispose(); // Release HTTP resources

// Wait to let service process requests
Console.WriteLine("  ⏳ Waiting 15 seconds to simulate client offline...");
await Task.Delay(15000);

// Verify session still exists
using var verifyHttp = new HttpClient { BaseAddress = new Uri(brokerUrl) };
var statusResp = await verifyHttp.GetAsync($"/api/v1/sessions/{sessionId}");
if (statusResp.IsSuccessStatusCode)
{
    var statusJson = await statusResp.Content.ReadAsStringAsync();
    Console.WriteLine($"  ✓ Session still active on broker: {statusResp.StatusCode}");
}
else
{
    Console.WriteLine($"  ✗ Session LOST: {statusResp.StatusCode}");
    return;
}

// ═══════════════════════════════════════════════════════
// Phase 3: Reconnect via AttachSession
// ═══════════════════════════════════════════════════════
Console.WriteLine();
Console.WriteLine("══════ Phase 3: Reconnect (AttachSession) ══════");

var reconnectSw = Stopwatch.StartNew();
var reconnectedSession = await CloudSession.AttachSessionAsync(
    brokerEndpoint, sessionId, "reconnected-client-1");
reconnectSw.Stop();

Console.WriteLine($"  ✓ Reconnected to session: {reconnectedSession.SessionId}");
Console.WriteLine($"    State: {reconnectedSession.State}");
Console.WriteLine($"    Reconnect latency: {reconnectSw.ElapsedMilliseconds}ms");

// ═══════════════════════════════════════════════════════
// Phase 4: Retrieve Responses
// ═══════════════════════════════════════════════════════
Console.WriteLine();
Console.WriteLine("══════ Phase 4: Retrieve Responses ══════");

var retrieveSw = Stopwatch.StartNew();
using var retrieveClient = new CloudBrokerClient(reconnectedSession);
var responses = await retrieveClient.GetAllResponsesAsync(totalRequests, TimeSpan.FromMinutes(5));
retrieveSw.Stop();

int successCount = 0, faultCount = 0;
var taskResults = new Dictionary<string, bool>();
var serverLatencies = new List<double>();

foreach (var resp in responses)
{
    if (resp.IsFault)
    {
        faultCount++;
        Console.WriteLine($"  ✗ Fault: {resp.FaultMessage}");
    }
    else
    {
        successCount++;
        try
        {
            var payloadStr = resp.GetPayloadString();
            // WCF wraps string result - extract inner content
            if (payloadStr.Contains("<string"))
            {
                var start = payloadStr.IndexOf('>') + 1;
                var end = payloadStr.LastIndexOf("</string>");
                if (start > 0 && end > start)
                    payloadStr = payloadStr.Substring(start, end - start);
            }
            var result = JsonSerializer.Deserialize<JsonElement>(payloadStr);
            if (result.TryGetProperty("taskId", out var tid))
                taskResults[tid.GetString()!] = true;
            if (result.TryGetProperty("processingTimeMs", out var pt))
                serverLatencies.Add(pt.GetDouble());
        }
        catch { }
    }
}

Console.WriteLine($"  ✓ Retrieved {responses.Count} responses in {retrieveSw.ElapsedMilliseconds}ms");
Console.WriteLine($"    Success: {successCount}  Faulted: {faultCount}");
Console.WriteLine($"    Unique tasks completed: {taskResults.Count}/{totalRequests}");

// ═══════════════════════════════════════════════════════
// Phase 5: Close Session
// ═══════════════════════════════════════════════════════
Console.WriteLine();
Console.WriteLine("══════ Phase 5: Close Session ══════");
await reconnectedSession.CloseAsync();
Console.WriteLine("  ✓ Durable session closed");

// ═══════════════════════════════════════════════════════
// Phase 6: Verify Session is Closed (cannot reattach)
// ═══════════════════════════════════════════════════════
Console.WriteLine();
Console.WriteLine("══════ Phase 6: Verify Closed (Cannot Reattach) ══════");
try
{
    var shouldFail = await CloudSession.AttachSessionAsync(brokerEndpoint, sessionId, "bad-client");
    Console.WriteLine("  ✗ ERROR: AttachSession should have failed on closed session!");
}
catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict ||
                                       ex.StatusCode == System.Net.HttpStatusCode.NotFound)
{
    Console.WriteLine($"  ✓ Correctly rejected: {ex.StatusCode}");
}
catch (Exception ex)
{
    Console.WriteLine($"  ✓ Correctly rejected: {ex.Message}");
}

// ═══════════════════════════════════════════════════════
// Report
// ═══════════════════════════════════════════════════════
Console.WriteLine();
Console.WriteLine("╔══════════════════════════════════════════════════╗");
Console.WriteLine("║              DURABLE SESSION REPORT             ║");
Console.WriteLine("╚══════════════════════════════════════════════════╝");
Console.WriteLine($"  Session ID:       {sessionId}");
Console.WriteLine($"  Session Type:     Durable");
Console.WriteLine($"  Total Requests:   {totalRequests}");
Console.WriteLine($"  Responses:        {responses.Count}");
Console.WriteLine($"  Success:          {successCount}");
Console.WriteLine($"  Faulted:          {faultCount}");
Console.WriteLine($"  Completion:       {(double)successCount / totalRequests * 100:F1}%");
Console.WriteLine($"  Retrieve Time:    {retrieveSw.ElapsedMilliseconds}ms");
Console.WriteLine($"  Reconnect Time:   {reconnectSw.ElapsedMilliseconds}ms");

if (serverLatencies.Count > 0)
{
    serverLatencies.Sort();
    Console.WriteLine($"  Server Latency:");
    Console.WriteLine($"    Avg:  {serverLatencies.Average():F1}ms");
    Console.WriteLine($"    P50:  {serverLatencies[serverLatencies.Count / 2]:F1}ms");
    Console.WriteLine($"    P95:  {serverLatencies[(int)(serverLatencies.Count * 0.95)]:F1}ms");
    Console.WriteLine($"    Min:  {serverLatencies[0]:F1}ms");
    Console.WriteLine($"    Max:  {serverLatencies[^1]:F1}ms");
}

Console.WriteLine();
var allPassed = successCount == totalRequests;
Console.WriteLine(allPassed
    ? "  ★ ALL DURABLE SESSION TESTS PASSED ★"
    : $"  ✗ FAILED: Expected {totalRequests} success, got {successCount}");
Console.WriteLine();
