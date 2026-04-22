using LLMock.Cli.Daemon;

namespace LLMock.Cli.Commands;

public static class StopCommand
{
    public static async Task<int> RunAsync(CancellationToken ct)
    {
        if (!DaemonController.IsDaemonRunning())
        {
            Console.WriteLine("  LLMock is not running.");
            return 0;
        }

        Console.Write("  Stopping LLMock... ");

        if (int.TryParse((await File.ReadAllTextAsync(DaemonController.PidFilePath, ct)).Trim(), out var pid))
        {
            try
            {
                var process = System.Diagnostics.Process.GetProcessById(pid);
                process.Kill(entireProcessTree: false);
                await process.WaitForExitAsync(ct);
            }
            catch { /* already gone */ }
        }

        if (File.Exists(DaemonController.SocketPath)) File.Delete(DaemonController.SocketPath);
        if (File.Exists(DaemonController.PidFilePath)) File.Delete(DaemonController.PidFilePath);
        if (File.Exists(DaemonController.PortFilePath)) File.Delete(DaemonController.PortFilePath);

        Console.WriteLine("✓");
        return 0;
    }
}
