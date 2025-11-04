namespace LLMApi.Helpers;

public static class HelpDisplay
{
    public static void Show()
    {
        Console.WriteLine("╔════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║              LLMock API Server v1.0.0                          ║");
        Console.WriteLine("║        Mock LLM-powered API with SignalR Support               ║");
        Console.WriteLine("╚════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine("USAGE:");
        Console.WriteLine("  LLMApi.exe [options]");
        Console.WriteLine();
        Console.WriteLine("OPTIONS:");
        Console.WriteLine("  -c, --config <path>              Load configuration from JSON file");
        Console.WriteLine("                                   (overrides appsettings.json)");
        Console.WriteLine();
        Console.WriteLine("  --MockLlmApi:BaseUrl=<url>       LLM backend URL");
        Console.WriteLine("                                   Default: http://localhost:11434/v1/");
        Console.WriteLine();
        Console.WriteLine("  --MockLlmApi:ModelName=<model>   Model name (e.g., llama3, mistral)");
        Console.WriteLine("                                   Default: llama3");
        Console.WriteLine();
        Console.WriteLine("  --MockLlmApi:Temperature=<n>     Sampling temperature (0.0-2.0)");
        Console.WriteLine("                                   Default: 1.2");
        Console.WriteLine();
        Console.WriteLine("  --MockLlmApi:TimeoutSeconds=<n>  Request timeout in seconds");
        Console.WriteLine("                                   Default: 30");
        Console.WriteLine();
        Console.WriteLine("  --urls <url>                     Listening address");
        Console.WriteLine("                                   Default: http://localhost:5116");
        Console.WriteLine();
        Console.WriteLine("EXAMPLES:");
        Console.WriteLine("  # Start with default settings");
        Console.WriteLine("  LLMApi.exe");
        Console.WriteLine();
        Console.WriteLine("  # Use custom port and model");
        Console.WriteLine("  LLMApi.exe --urls http://localhost:8080 --MockLlmApi:ModelName=mistral");
        Console.WriteLine();
        Console.WriteLine("  # Load external config file");
        Console.WriteLine("  LLMApi.exe --config /path/to/config.json");
        Console.WriteLine();
        Console.WriteLine("ENDPOINTS:");
        Console.WriteLine("  HTTP API:        /api/mock/**");
        Console.WriteLine("  Streaming API:   /api/mock/stream/**");
        Console.WriteLine("  SignalR Hub:     /hub/mock");
        Console.WriteLine("  Demo Pages:      / (index.html), /streaming.html");
        Console.WriteLine("  Context Mgmt:    /api/mock/contexts");
        Console.WriteLine();
        Console.WriteLine("For more info: https://github.com/scottgal/LLMApi");
        Console.WriteLine();
    }
}
