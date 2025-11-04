using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Options;
using mostlylucid.mockllmapi.Models;

namespace mostlylucid.mockllmapi.Services;

/// <summary>
/// Client for communicating with LLM API
/// </summary>
public class LlmClient(IOptions<LLMockApiOptions> options, IHttpClientFactory httpClientFactory)
{
    private readonly LLMockApiOptions _options = options.Value;

    // Explicitly enable reflection-based serialization to avoid AOT/trim restrictions
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    /// <summary>
    /// Sends a non-streaming chat completion request
    /// </summary>
    public virtual async Task<string> GetCompletionAsync(string prompt, CancellationToken cancellationToken = default)
    {
        using var client = CreateHttpClient();
        var payload = BuildChatRequest(prompt, stream: false);
        using var httpReq = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
        httpReq.Content = JsonContent.Create(payload, options: s_jsonOptions);
        using var httpRes = await client.SendAsync(httpReq, cancellationToken);
        httpRes.EnsureSuccessStatusCode();
        var result = await httpRes.Content.ReadFromJsonAsync<ChatCompletionLite>(s_jsonOptions, cancellationToken);
        return result.FirstContent ?? "{}";
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
            Content = JsonContent.Create(payload, options: s_jsonOptions)
        };
        var httpRes = await client.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        httpRes.EnsureSuccessStatusCode();
        return httpRes;
    }

    /// <summary>
    /// Requests multiple completions in a single call and returns all message contents.
    /// </summary>
    public virtual async Task<List<string>> GetNCompletionsAsync(string prompt, int n, CancellationToken cancellationToken = default)
    {
        using var client = CreateHttpClient();
        var payload = BuildChatRequest(prompt, stream: false, n: n);
        using var httpReq = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(payload, options: s_jsonOptions)
        };
        using var httpRes = await client.SendAsync(httpReq, cancellationToken);
        httpRes.EnsureSuccessStatusCode();
        var result = await httpRes.Content.ReadFromJsonAsync<ChatCompletionLite>(s_jsonOptions, cancellationToken);
        var list = new List<string>();
        foreach (var c in result.Choices)
        {
            if (!string.IsNullOrEmpty(c.Message.Content))
                list.Add(c.Message.Content);
        }
        return list;
    }

    /// <summary>
    /// Builds a chat request object for the LLM API
    /// </summary>
    public object BuildChatRequest(string prompt, bool stream, int? n = null)
    {
        return new
        {
            model = _options.ModelName,
            stream,
            temperature = _options.Temperature,
            n,
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
