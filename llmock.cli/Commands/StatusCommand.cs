using System.Text.Json;
using LLMock.Cli.Daemon;

namespace LLMock.Cli.Commands;

public static class StatusCommand
{
    public static async Task<int> RunAsync(CancellationToken ct)
    {
        if (!DaemonController.IsDaemonRunning())
        {
            Console.WriteLine("  LLMock: NOT RUNNING");
            return 1;
        }

        Console.WriteLine("  LLMock: RUNNING");

        try
        {
            var port = 5555;
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var json = await http.GetStringAsync($"http://localhost:{port}/api/dashboard/stats", ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var totalRequests = root.TryGetProperty("totalRequests", out var tr) ? tr.GetInt32() : 0;
            var activeContexts = root.TryGetProperty("activeContexts", out var ac) ? ac.GetInt32() : 0;

            Console.WriteLine($"  Port:     {port}");
            Console.WriteLine($"  Requests: {totalRequests} total");
            Console.WriteLine($"  Contexts: {activeContexts} active");
        }
        catch
        {
            Console.WriteLine("  (Stats unavailable — server may be starting)");
        }

        return 0;
    }
}
