using Microsoft.Extensions.Options;
using mostlylucid.mockllmapi;

namespace LLMApi.Helpers;

public static class StartupBanner
{
    public static void Display(LLMockApiOptions options)
    {
        Console.ForegroundColor = ConsoleColor.DarkRed;
        Console.WriteLine("╔════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║              LLMock API Server v1.0.0                          ║");
        Console.WriteLine("║        Mock LLM-powered API with SignalR Support               ║");
        Console.WriteLine("╚════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("Configuration:");
        Console.WriteLine($"  LLM Backend: {options.BaseUrl}");
        Console.WriteLine($"  Model:       {options.ModelName}");
        Console.WriteLine($"  Temperature: {options.Temperature}");
        Console.WriteLine($"  Timeout:     {options.TimeoutSeconds}s");
        Console.WriteLine();
        Console.WriteLine("Endpoints:");
        Console.WriteLine("  HTTP API:        /api/mock/**");
        Console.WriteLine("  Streaming API:   /api/mock/stream/**");
        Console.WriteLine("  SignalR Hub:     /hub/mock");
        Console.WriteLine("  Demo Pages:      / (index.html), /streaming.html");
        Console.WriteLine("  Context Mgmt:    /api/mock/contexts");
        Console.WriteLine();
        Console.WriteLine("Command-line options:");
        Console.WriteLine("  --config <path>              Load config from JSON file");
        Console.WriteLine("  --MockLlmApi:BaseUrl=<url>   Override LLM backend URL");
        Console.WriteLine("  --MockLlmApi:ModelName=<m>   Override model name");
        Console.WriteLine("  --urls http://host:port      Set listening address");
        Console.WriteLine("  --help, -h                   Show detailed help");
        Console.WriteLine();
        Console.WriteLine("Press Ctrl+C to shutdown");
        Console.WriteLine(new string('─', 66));
        Console.WriteLine();
    }
}
