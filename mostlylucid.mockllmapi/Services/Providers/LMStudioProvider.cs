using System.Net.Http.Json;
using mostlylucid.mockllmapi.Models;

namespace mostlylucid.mockllmapi.Services.Providers;

/// <summary>
/// LM Studio provider (local LLM server with OpenAI-compatible API)
/// Endpoint: http://localhost:1234/v1/
/// Works with any model loaded in LM Studio
/// </summary>
public class LMStudioProvider : ILlmProvider
{
    public string Name => "lmstudio";

    public async Task<string> GetCompletionAsync(
        HttpClient client,
        string prompt,
        string modelName,
        double temperature,
        int? maxTokens,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            model = modelName,
            messages = new[] { new { role = "user", content = prompt } },
            temperature,
            max_tokens = maxTokens,
            stream = false
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(payload)
        };

        var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ChatCompletionLite>(cancellationToken: cancellationToken);
        return result.FirstContent ?? "{}";
    }

    public async Task<HttpResponseMessage> GetStreamingCompletionAsync(
        HttpClient client,
        string prompt,
        string modelName,
        double temperature,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            model = modelName,
            messages = new[] { new { role = "user", content = prompt } },
            temperature,
            stream = true
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(payload)
        };

        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return response;
    }

    public async Task<List<string>> GetNCompletionsAsync(
        HttpClient client,
        string prompt,
        string modelName,
        double temperature,
        int n,
        CancellationToken cancellationToken)
    {
        // LM Studio supports n parameter
        var payload = new
        {
            model = modelName,
            messages = new[] { new { role = "user", content = prompt } },
            temperature,
            n,
            stream = false
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(payload)
        };

        var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ChatCompletionLite>(cancellationToken: cancellationToken);
        var list = new List<string>();

        if (result.Choices != null)
        {
            foreach (var choice in result.Choices)
            {
                if (!string.IsNullOrEmpty(choice.Message.Content))
                    list.Add(choice.Message.Content);
            }
        }

        return list;
    }

    public void ConfigureClient(HttpClient client, string? apiKey)
    {
        // LM Studio typically doesn't require authentication
        // But support it if provided
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        }
    }
}
