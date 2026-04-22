using LLMock.Cli.Daemon;
using LLMock.Cli.Dashboard;

namespace LLMock.Cli.Commands;

public static class DashboardCommand
{
    public static async Task<int> RunAsync(int port, CancellationToken ct)
    {
        if (!DaemonController.IsDaemonRunning())
        {
            Console.WriteLine("  No daemon is running. Start with: llmock serve --daemon");
            return 1;
        }

        Console.WriteLine($"  Connecting to LLMock on :{port}...");
        var poller = new DashboardPoller(port);
        var renderer = new DashboardRenderer(poller);
        await renderer.RunAsync(ct);
        return 0;
    }
}
