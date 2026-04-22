using LLMock.Cli.Embedded;

namespace LLMock.Cli.Commands;

public static class ModelsCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        var download = args.Contains("download");
        var opts = new EmbeddedModelOptions();
        var modelPath = ModelDownloader.GetModelPath(opts);

        if (download)
        {
            Console.WriteLine("  Downloading embedded model...");
            await ModelDownloader.EnsureModelAsync(opts, ct);
            return 0;
        }

        Console.WriteLine("  Downloaded models (~/.llmock/models/):");
        if (File.Exists(modelPath))
        {
            var size = new FileInfo(modelPath).Length;
            Console.WriteLine($"    ✓ {opts.FileName}   {ModelDownloader.FormatBytes(size)}   [active - embedded]");
        }
        else
        {
            Console.WriteLine($"    ✗ {opts.FileName}   not downloaded");
            Console.WriteLine($"      Run: llmock models download");
        }

        return 0;
    }
}
