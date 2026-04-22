using LLMock.Cli.Service;

namespace LLMock.Cli.Commands;

public static class InstallServiceCommand
{
    public static async Task<int> RunAsync(int port = 5555)
    {
        if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.OSX))
        {
            Console.WriteLine("  install-service is only supported on macOS (launchd).");
            return 1;
        }

        await ServiceManager.InstallAsync(port);
        return 0;
    }
}
