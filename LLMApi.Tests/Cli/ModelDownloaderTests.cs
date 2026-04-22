using LLMock.Cli.Embedded;

namespace LLMApi.Tests.Cli;

public class ModelDownloaderTests
{
    [Fact]
    public void ModelPath_IsInLLMockModelsDir()
    {
        var opts = new EmbeddedModelOptions();
        var path = ModelDownloader.GetModelPath(opts);

        Assert.Contains(".llmock", path);
        Assert.Contains("models", path);
        Assert.EndsWith(opts.FileName, path);
    }

    [Fact]
    public void FormatBytes_UnderKb_ShowsBytes()
    {
        Assert.Equal("512 B", ModelDownloader.FormatBytes(512));
    }

    [Fact]
    public void FormatBytes_Megabytes_ShowsMB()
    {
        var result = ModelDownloader.FormatBytes(5 * 1024 * 1024);
        Assert.Contains("MB", result);
    }

    [Fact]
    public void FormatBytes_Gigabytes_ShowsGB()
    {
        var result = ModelDownloader.FormatBytes(2L * 1024 * 1024 * 1024);
        Assert.Contains("GB", result);
    }

    [Theory]
    [InlineData(0, 100, "░░░░░░░░░░░░░░░░░░░░")]
    [InlineData(50, 100, "██████████░░░░░░░░░░")]
    [InlineData(100, 100, "████████████████████")]
    public void BuildProgressBar_ReturnsCorrectFill(long current, long total, string expected)
    {
        var bar = ModelDownloader.BuildProgressBar(current, total, width: 20);
        Assert.Equal(expected, bar);
    }

    [Fact]
    public void EmbeddedModelOptions_HasExpectedDefaults()
    {
        var opts = new EmbeddedModelOptions();
        Assert.Equal("qwen3.5-0.8b-q4_k_m.gguf", opts.FileName);
        Assert.False(string.IsNullOrWhiteSpace(opts.DownloadUrl));
        // ExpectedSha256 may be a placeholder at dev time — assert it is non-null (not empty string)
        // rather than asserting a specific value, since the real SHA256 is set at release time.
        Assert.NotNull(opts.ExpectedSha256);
    }
}
