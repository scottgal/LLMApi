using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using mostlylucid.mockllmapi;
using mostlylucid.mockllmapi.Services;
using mostlylucid.mockllmapi.Services.Providers;

namespace LLMApi.Tests.Helpers;

/// <summary>
///     Fake LLM client for testing that returns mock data without making real API calls
/// </summary>
public class FakeLlmClient : LlmClient
{
    private static int _counter;

    public FakeLlmClient(IOptions<LLMockApiOptions> options, IHttpClientFactory httpClientFactory,
        ILogger<LlmClient> logger, LlmBackendSelector backendSelector, LlmProviderFactory providerFactory)
        : base(options, httpClientFactory, logger, backendSelector, providerFactory)
    {
    }

    public override Task<string> GetCompletionAsync(string prompt, CancellationToken cancellationToken = default,
        int? maxTokens = null, HttpRequest? request = null)
    {
        // Detect if array shape is requested by checking if prompt contains array notation
        // But NOT if it's file metadata (files property with nested objects)
        var hasArrayNotation = prompt.Contains("[{") || prompt.Contains("[ {");
        var isFileMetadata = prompt.Contains("\"files\"") || prompt.Contains("\"filename\"") ||
                             prompt.Contains("\"contentType\"") || prompt.Contains("\"formFields\"");

        var isArrayRequest = hasArrayNotation && !isFileMetadata;

        if (isArrayRequest)
        {
            // Return array of items
            var items = new List<string>();
            for (var i = 0; i < 3; i++)
            {
                var id = Interlocked.Increment(ref _counter);
                items.Add($$"""
                            {
                                "id": {{id}},
                                "name": "Test Item {{id}}",
                                "title": "Title {{id}}",
                                "author": "Author {{id}}",
                                "value": {{Random.Shared.Next(1, 100)}},
                                "description": "Description for item {{id}}",
                                "metadata": { "tag1": "value1", "tag2": "value2", "tag3": "value3" },
                                "timestamp": "{{DateTime.UtcNow:O}}"
                            }
                            """);
            }

            return Task.FromResult($"[{string.Join(",", items)}]");
        }

        // Return single object (including for file uploads)
        var singleId = Interlocked.Increment(ref _counter);
        var json = $$"""
                     {
                         "id": {{singleId}},
                         "name": "Test Item {{singleId}}",
                         "email": "test{{singleId}}@example.com",
                         "status": "active",
                         "value": {{Random.Shared.Next(1, 100)}},
                         "address": {
                             "street": "123 Main St",
                             "city": "TestCity",
                             "country": "TestCountry"
                         },
                         "tags": ["tag1", "tag2", "tag3"],
                         "timestamp": "{{DateTime.UtcNow:O}}"
                     }
                     """;
        return Task.FromResult(json);
    }

    public new Task<List<string>> GetNCompletionsAsync(string prompt, int count,
        CancellationToken cancellationToken = default, HttpRequest? request = null)
    {
        var results = new List<string>();
        for (var i = 0; i < count; i++)
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