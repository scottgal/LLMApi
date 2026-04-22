using LLMock.Cli.Embedded;

namespace LLMApi.Tests.Cli;

public class EmbeddedLlmProviderTests
{
    [Fact]
    public void Name_IsEmbedded()
    {
        Assert.Equal("embedded", EmbeddedLlmProvider.ProviderName);
    }

    [Fact]
    public void ConfigureClient_DoesNotThrow()
    {
        var provider = new EmbeddedLlmProvider(null!);
        var client = new HttpClient();
        provider.ConfigureClient(client, apiKey: null); // should be a no-op
    }

    [Fact]
    public async Task GetCompletionAsync_ThrowsIfModelNotLoaded()
    {
        var provider = new EmbeddedLlmProvider(null!);
        var client = new HttpClient();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetCompletionAsync(client, "test", "model", 0.7, null, CancellationToken.None));

        Assert.Equal("Embedded model not loaded. Call LoadModel() first.", ex.Message);
    }

    [Fact]
    public async Task GetStreamingCompletionAsync_ThrowsIfModelNotLoaded()
    {
        var provider = new EmbeddedLlmProvider(null!);
        var client = new HttpClient();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetStreamingCompletionAsync(client, "test", "model", 0.7, CancellationToken.None));

        Assert.Equal("Embedded model not loaded. Call LoadModel() first.", ex.Message);
    }

    [Fact]
    public async Task GetNCompletionsAsync_ThrowsIfModelNotLoaded()
    {
        var provider = new EmbeddedLlmProvider(null!);
        var client = new HttpClient();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetNCompletionsAsync(client, "test", "model", 0.7, 3, CancellationToken.None));

        Assert.Equal("Embedded model not loaded. Call LoadModel() first.", ex.Message);
    }
}
