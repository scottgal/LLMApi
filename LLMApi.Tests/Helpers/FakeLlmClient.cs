using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using mostlylucid.mockllmapi;
using mostlylucid.mockllmapi.Services;

namespace LLMApi.Tests.Helpers;

/// <summary>
/// Fake LLM client for testing that returns mock data without making real API calls
/// </summary>
public class FakeLlmClient : LlmClient
{
    private static int _counter = 0;

    public FakeLlmClient(IOptions<LLMockApiOptions> options, ILogger<LlmClient> logger)
        : base(options, new FakeChatClient(), logger)
    {
    }

    // Factory method for tests that pass IHttpClientFactory
    public static FakeLlmClient Create(IOptions<LLMockApiOptions> options, IHttpClientFactory httpClientFactory, ILogger<LlmClient> logger)
    {
        // Ignore the httpClientFactory since we're using IChatClient now
        return new FakeLlmClient(options, logger);
    }

    public override Task<string> GetCompletionAsync(string prompt, CancellationToken cancellationToken = default, int? maxTokens = null)
    {
        // Return fake JSON data
        var id = Interlocked.Increment(ref _counter);
        var json = $$"""
        {
            "id": {{id}},
            "name": "Test Item {{id}}",
            "value": {{Random.Shared.Next(1, 100)}},
            "timestamp": "{{DateTime.UtcNow:O}}"
        }
        """;
        return Task.FromResult(json);
    }

    public new Task<List<string>> GetNCompletionsAsync(string prompt, int count, CancellationToken cancellationToken = default)
    {
        var results = new List<string>();
        for (int i = 0; i < count; i++)
        {
            var id = Interlocked.Increment(ref _counter);
            var json = $$"""
            {
                "id": {{id}},
                "name": "Test Item {{id}}",
                "value": {{Random.Shared.Next(1, 100)}},
                "timestamp": "{{DateTime.UtcNow:O}}"
            }
            """;
            results.Add(json);
        }
        return Task.FromResult(results);
    }
}

/// <summary>
/// Fake IChatClient implementation for testing
/// </summary>
internal class FakeChatClient : IChatClient
{
    public ChatClientMetadata Metadata => new("fake-provider", new Uri("http://localhost"), "fake-model");

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        // Return a simple fake response
        var response = new ChatResponse(new[]
        {
            new ChatMessage(ChatRole.Assistant, "{\"test\": \"data\"}")
        });
        return Task.FromResult(response);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Streaming not implemented in fake client");
    }

    public TService? GetService<TService>(object? key = null) where TService : class
    {
        return null;
    }

    public object? GetService(Type serviceType, object? key = null)
    {
        return null;
    }

    public void Dispose()
    {
        // Nothing to dispose
    }
}
