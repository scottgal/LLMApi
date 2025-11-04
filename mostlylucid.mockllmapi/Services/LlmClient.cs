using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using mostlylucid.mockllmapi.Models;

namespace mostlylucid.mockllmapi.Services;

/// <summary>
/// Client for communicating with LLM API
/// </summary>
public class LlmClient(IOptions<LLMockApiOptions> options, IHttpClientFactory httpClientFactory)
{
    private readonly LLMockApiOptions _options = options.Value;

    /// <summary>
    /// Sends a non-streaming chat completion request
    /// </summary>
    public virtual async Task<string> GetCompletionAsync(string prompt, CancellationToken cancellationToken = default)
    {
        using var client = CreateHttpClient();
        var payload = BuildChatRequest(prompt, stream: false);
        using var httpReq = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(payload)
        };
        using var httpRes = await client.SendAsync(httpReq, cancellationToken);
        httpRes.EnsureSuccessStatusCode();
        var result = await httpRes.Content.ReadFromJsonAsync<ChatCompletionLite>(cancellationToken: cancellationToken);
        return result.FirstContent ?? "{}";
    }

    /// <summary>
    /// Sends N non-streaming chat completion requests for batch processing
    /// </summary>
    public virtual async Task<List<string>> GetNCompletionsAsync(string prompt, int count, CancellationToken cancellationToken = default)
    {
        var tasks = new List<Task<string>>();
        for (int i = 0; i < count; i++)
        {
            tasks.Add(GetCompletionAsync(prompt, cancellationToken));
        }
        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }

    /// <summary>
    /// Sends a streaming chat completion request
    /// </summary>
    public async Task<HttpResponseMessage> GetStreamingCompletionAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var client = CreateHttpClient();
        var payload = BuildChatRequest(prompt, stream: true);
        var httpReq = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(payload)
        };
        var httpRes = await client.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        httpRes.EnsureSuccessStatusCode();
        return httpRes;
    }

    /// <summary>
    /// Builds a chat request object for the LLM API
    /// </summary>
    public object BuildChatRequest(string prompt, bool stream)
    {
        return new
        {
            model = _options.ModelName,
            stream,
            temperature = _options.Temperature,
            messages = new[] { new { role = "user", content = prompt } }
        };
    }

    /// <summary>
    /// Creates an HttpClient configured for the LLM API
    /// </summary>
    private HttpClient CreateHttpClient()
    {
        var client = httpClientFactory.CreateClient("LLMockApi");
        client.BaseAddress = new Uri(_options.BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        return client;
    }
}
