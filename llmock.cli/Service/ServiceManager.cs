using System.Diagnostics;
using LLMock.Cli.Daemon;

namespace LLMock.Cli.Service;

public static class ServiceManager
{
    public static readonly string PlistPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "LaunchAgents", "com.llmock.agent.plist");

    private static readonly string LogPath = DaemonController.LogFilePath;

    public static string GeneratePlist(string executablePath, int port)
    {
        return $"""
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>com.llmock.agent</string>
    <key>ProgramArguments</key>
    <array>
        <string>{executablePath}</string>
        <string>serve</string>
        <string>--headless</string>
        <string>--port</string>
        <string>{port}</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
    <key>KeepAlive</key>
    <true/>
    <key>StandardOutPath</key>
    <string>{LogPath}</string>
    <key>StandardErrorPath</key>
    <string>{LogPath}</string>
</dict>
</plist>
""";
    }

    public static async Task InstallAsync(int port = 5555)
    {
        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot determine executable path.");

        var plistContent = GeneratePlist(executablePath, port);
        var plistDir = Path.GetDirectoryName(PlistPath)!;
        Directory.CreateDirectory(plistDir);

        Console.WriteLine($"  Writing {PlistPath}");
        await File.WriteAllTextAsync(PlistPath, plistContent);

        Console.Write("  Loading service via launchctl... ");
        var exitCode = await RunLaunchctl("load", PlistPath);
        if (exitCode == 0)
        {
            Console.WriteLine("✓");
            Console.WriteLine("  LLMock will now start automatically on login.");
            Console.WriteLine("  Run 'llmock status' to verify.");
        }
        else
        {
            Console.WriteLine("FAILED");
            Console.WriteLine($"  launchctl load exited with code {exitCode}");
            Console.WriteLine($"  Try: launchctl load {PlistPath}");
        }
    }

    public static async Task UninstallAsync()
    {
        if (!File.Exists(PlistPath))
        {
            Console.WriteLine("  Service not installed (plist not found).");
            return;
        }

        Console.Write("  Unloading service... ");
        await RunLaunchctl("unload", PlistPath);
        Console.WriteLine("✓");

        Console.WriteLine($"  Removing {PlistPath}");
        File.Delete(PlistPath);

        Console.WriteLine("  Service removed.");
    }

    private static async Task<int> RunLaunchctl(string command, string arg)
    {
        var psi = new ProcessStartInfo("launchctl", $"{command} {arg}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(psi)!;
        await process.WaitForExitAsync();
        return process.ExitCode;
    }
}
