using LLMock.Cli.Daemon;

namespace LLMock.Cli.Commands;

public static class LogsCommand
{
    public static async Task<int> RunAsync(CancellationToken ct)
    {
        var logPath = DaemonController.LogFilePath;
        if (!File.Exists(logPath))
        {
            Console.WriteLine($"  No log file at {logPath}");
            Console.WriteLine("  Start LLMock with: llmock serve --daemon");
            return 1;
        }

        Console.WriteLine($"  Tailing {logPath}  (Ctrl-C to stop)");
        Console.WriteLine();

        using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Seek(0, SeekOrigin.End);
        using var reader = new StreamReader(stream);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line != null)
                Console.WriteLine(line);
            else
            {
                try { await Task.Delay(250, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }

        return 0;
    }
}
