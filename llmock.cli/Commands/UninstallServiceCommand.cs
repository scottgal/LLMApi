using LLMock.Cli.Service;

namespace LLMock.Cli.Commands;

public static class UninstallServiceCommand
{
    public static async Task<int> RunAsync()
    {
        if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.OSX))
        {
            Console.WriteLine("  uninstall-service is only supported on macOS (launchd).");
            return 1;
        }

        await ServiceManager.UninstallAsync();
        return 0;
    }
}
