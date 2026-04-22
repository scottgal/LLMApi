using LLMock.Cli.Commands;
using Serilog;

namespace LLMock.Cli;

internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        try
        {
            return args.Length == 0 || args[0] == "serve"
                ? await HandleServe(args, cts.Token)
                : await HandleCommand(args, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n  Fatal error: {ex.Message}");
            Console.ResetColor();
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    private static async Task<int> HandleServe(string[] args, CancellationToken ct)
    {
        var port = 5000;
        var specs = new List<string>();
        string? backend = null, model = null, baseUrl = null, apiKey = null, configFile = null, pack = null;
        var headless = false;
        var daemon = false;

        for (var i = 0; i < args.Length; i++)
            switch (args[i])
            {
                case "--port" or "-p" when i + 1 < args.Length: port = int.Parse(args[++i]); break;
                case "--spec" or "-s" when i + 1 < args.Length: specs.Add(args[++i]); break;
                case "--backend" or "-b" when i + 1 < args.Length: backend = args[++i]; break;
                case "--model" or "-m" when i + 1 < args.Length: model = args[++i]; break;
                case "--base-url" when i + 1 < args.Length: baseUrl = args[++i]; break;
                case "--api-key" or "-k" when i + 1 < args.Length: apiKey = args[++i]; break;
                case "--config" or "-c" when i + 1 < args.Length: configFile = args[++i]; break;
                case "--pack" or "-P" when i + 1 < args.Length: pack = args[++i]; break;
                case "--headless": headless = true; break;
                case "--daemon": daemon = true; break;
                case "--help" or "-h": ShowHelp(); return 0;
                default:
                    if (!args[i].StartsWith('-') && args[i] != "serve")
                        specs.Add(args[i]);
                    break;
            }

        return await ServeCommand.RunAsync(port, [..specs], backend, model, baseUrl, apiKey,
            configFile, pack, headless, daemon, ct);
    }

    private static async Task<int> HandleCommand(string[] args, CancellationToken ct)
    {
        return args[0] switch
        {
            "dashboard" => await DashboardCommand.RunAsync(GetPort(args), ct),
            "status" => await StatusCommand.RunAsync(ct),
            "stop" => await StopCommand.RunAsync(ct),
            "logs" => await LogsCommand.RunAsync(ct),
            "models" => await ModelsCommand.RunAsync(args[1..], ct),
            "install-service" => await InstallServiceCommand.RunAsync(GetPort(args)),
            "uninstall-service" => await UninstallServiceCommand.RunAsync(),
            "--help" or "-h" or "help" => ShowHelpReturn(),
            _ => UnknownCommand(args[0]),
        };
    }

    private static int GetPort(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] is "--port" or "-p" && int.TryParse(args[i + 1], out var p))
                return p;
        return 5555;
    }

    private static int ShowHelpReturn() { ShowHelp(); return 0; }

    private static int UnknownCommand(string cmd)
    {
        Console.WriteLine($"Unknown command: {cmd}. Run 'llmock --help' for usage.");
        return 1;
    }

    private static void ShowHelp()
    {
        Console.WriteLine("""
            LLMock CLI - LLM-powered mock API server with live dashboard

            USAGE:
                llmock [command] [options]

            COMMANDS:
                serve                           Start the mock API server (default)
                dashboard                       Open live dashboard (connects to running daemon)
                status                          Show daemon status
                stop                            Stop the running daemon
                logs                            Tail daemon logs
                models                          List downloaded models
                models download                 Download the embedded model
                install-service                 Install as a login item (macOS launchd)
                uninstall-service               Remove login item

            SERVE OPTIONS:
                --port, -p <port>              Server port (default: 5555)
                --spec, -s <file-or-url>       OpenAPI spec file or URL (repeatable)
                --backend, -b <provider>       LLM backend (ollama, openai, lmstudio, embedded)
                --model, -m <model>            Model name
                --base-url <url>               LLM backend base URL
                --api-key, -k <key>            API key for LLM backend
                --config, -c <file>            Path to appsettings.json file
                --pack, -P <pack-id>           API Holodeck pack to activate
                --headless                     Run without dashboard UI
                --daemon                       Start in background (implies --headless)
                --help, -h                     Show this help

            EXAMPLES:
                llmock serve
                llmock serve --daemon
                llmock serve --pack wordpress-rest
                llmock serve --port 8080 --pack banking
                llmock install-service
                llmock status
                llmock dashboard
            """);
    }
}
