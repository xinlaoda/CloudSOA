using CloudSOA.Client;
using CalculatorService;
using CalculatorClient;

var brokerUrl = args.Length > 0 ? args[0] : "http://localhost:5000";
Console.WriteLine($"CloudSOA Calculator Client");
Console.WriteLine($"Broker: {brokerUrl}");
Console.WriteLine(new string('=', 50));

try
{
    // ============================================================
    // Example 1: HPC Pack-Compatible API (typed message contracts)
    // Only changes from HPC Pack code:
    //   1. using CloudSOA.Client  (instead of Microsoft.Hpc.Scheduler.Session)
    //   2. Broker URL             (instead of head node name)
    // ============================================================
    Console.WriteLine("\n[Example 1] HPC Pack-Compatible API");
    Console.WriteLine(new string('-', 40));

    SessionStartInfo info = new SessionStartInfo(brokerUrl, "CalculatorService");
    using (Session session = Session.CreateSession(info))
    {
        Console.WriteLine($"Session: {session.Id}");
        using (BrokerClient<ICalculator> client = new BrokerClient<ICalculator>(session))
        {
            // Send batch of Add requests
            for (int i = 0; i < 5; i++)
            {
                var req = new AddRequest(i * 10.0, i * 3.0);
                client.SendRequest<AddRequest>(req, $"add-{i}");
                Console.WriteLine($"  Sent: Add({req.a}, {req.b})");
            }
            client.EndRequests();

            Console.WriteLine("\n  Results:");
            foreach (BrokerResponse<AddResponse> resp in client.GetResponses<AddResponse>())
            {
                if (resp.IsFault)
                    Console.WriteLine($"  [{resp.UserData}] FAULT: {resp.FaultMessage}");
                else
                    Console.WriteLine($"  [{resp.UserData}] Result = {resp.Result!.AddResult}");
            }
        }
        session.Close();
    }

    // ============================================================
    // Example 2: Simplified API (no message contracts needed)
    // For new services or when message contracts are not available
    // ============================================================
    Console.WriteLine("\n[Example 2] Simplified API (raw)");
    Console.WriteLine(new string('-', 40));

    using (var session2 = await CloudSession.CreateSessionAsync(
        new SessionStartInfo(brokerUrl, "CalculatorService")))
    {
        Console.WriteLine($"Session: {session2.SessionId}");
        using var rawClient = new CloudBrokerClient(session2);

        rawClient.SendRequest("Echo", "Hello from CloudSOA!");
        rawClient.SendRequest("Echo", "Testing 1-2-3");
        await rawClient.EndRequestsAsync();

        var responses = await rawClient.GetAllResponsesAsync(2);
        foreach (var resp in responses)
        {
            Console.WriteLine($"  [{resp.UserData}] {resp.GetPayloadString()}");
        }

        await session2.CloseAsync();
    }
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"\nError: {ex.Message}");
    Console.ResetColor();
}

Console.WriteLine($"\n{new string('=', 50)}");
Console.WriteLine("Done.");
