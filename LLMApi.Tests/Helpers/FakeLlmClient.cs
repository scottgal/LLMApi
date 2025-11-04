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

    public FakeLlmClient(IOptions<LLMockApiOptions> options, IHttpClientFactory httpClientFactory)
        : base(options, httpClientFactory)
    {
    }

    public override Task<string> GetCompletionAsync(string prompt, CancellationToken cancellationToken = default)
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

    public Task<List<string>> GetNCompletionsAsync(string prompt, int count, CancellationToken cancellationToken = default)
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
