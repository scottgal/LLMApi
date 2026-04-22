using System.Security.Cryptography;

namespace LLMock.Cli.Embedded;

public static class ModelDownloader
{
    private static readonly string ModelsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".llmock", "models");

    public static string GetModelPath(EmbeddedModelOptions opts) =>
        Path.Combine(ModelsDir, opts.FileName);

    /// <summary>
    /// Ensures the model file exists locally. Downloads it if missing.
    /// Returns the local path to the GGUF file.
    /// </summary>
    public static async Task<string> EnsureModelAsync(
        EmbeddedModelOptions opts,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(ModelsDir);
        var localPath = GetModelPath(opts);

        if (File.Exists(localPath))
        {
            // Skip verification if SHA256 is a placeholder or empty
            if (string.IsNullOrWhiteSpace(opts.ExpectedSha256) ||
                opts.ExpectedSha256.StartsWith("placeholder", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"  Model found: {opts.FileName} (checksum skipped — placeholder SHA256)");
                return localPath;
            }

            Console.Write($"  Verifying checksum... ");
            if (await VerifySha256Async(localPath, opts.ExpectedSha256, ct))
            {
                Console.WriteLine("OK");
                return localPath;
            }

            Console.WriteLine("MISMATCH — re-downloading");
            File.Delete(localPath);
        }

        await DownloadAsync(opts, localPath, ct);
        return localPath;
    }

    private static async Task DownloadAsync(
        EmbeddedModelOptions opts,
        string localPath,
        CancellationToken ct)
    {
        Console.WriteLine($"  Downloading {opts.FileName}");
        using var http = new HttpClient();
        http.Timeout = TimeSpan.FromHours(2);

        using var response = await http.GetAsync(opts.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1;
        var startTime = DateTime.UtcNow;
        var downloaded = 0L;

        var tmpPath = localPath + ".tmp";
        await using (var dest = File.Create(tmpPath))
        await using (var src = await response.Content.ReadAsStreamAsync(ct))
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dest.WriteAsync(buffer.AsMemory(0, read), ct);
                downloaded += read;
                PrintProgress(downloaded, total, startTime);
            }
        }

        Console.WriteLine();

        if (!string.IsNullOrWhiteSpace(opts.ExpectedSha256) &&
            !opts.ExpectedSha256.StartsWith("placeholder", StringComparison.OrdinalIgnoreCase))
        {
            Console.Write("  Verifying checksum... ");
            if (!await VerifySha256Async(tmpPath, opts.ExpectedSha256, ct))
            {
                File.Delete(tmpPath);
                throw new InvalidOperationException("Downloaded file failed SHA256 verification. Please try again.");
            }
            Console.WriteLine("OK");
        }

        File.Move(tmpPath, localPath, overwrite: true);
        Console.WriteLine($"  Model ready: {localPath}");
    }

    private static void PrintProgress(long downloaded, long total, DateTime startTime)
    {
        var elapsed = (DateTime.UtcNow - startTime).TotalSeconds;
        var speed = elapsed > 0 ? downloaded / elapsed : 0;
        var bar = total > 0 ? BuildProgressBar(downloaded, total, 20) : new string('\u2591', 20);
        var pct = total > 0 ? (int)(downloaded * 100 / total) : 0;
        var remaining = total > 0 && speed > 0
            ? TimeSpan.FromSeconds((total - downloaded) / speed).ToString(@"mm\:ss")
            : "--:--";

        Console.Write($"\r  [{bar}] {pct,3}% · {FormatBytes((long)speed)}/s · {remaining} remaining  ");
    }

    private static async Task<bool> VerifySha256Async(string path, string expectedHex, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).Equals(expectedHex, StringComparison.OrdinalIgnoreCase);
    }

    public static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
            _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
        };
    }

    public static string BuildProgressBar(long current, long total, int width = 20)
    {
        if (total <= 0) return new string('\u2591', width);
        var filled = (int)(current * width / total);
        filled = Math.Clamp(filled, 0, width);
        return new string('\u2588', filled) + new string('\u2591', width - filled);
    }
}
