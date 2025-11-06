using System.Net.Http.Json;
using mostlylucid.mockllmapi.Models;

namespace mostlylucid.mockllmapi.Services.Providers;

/// <summary>
/// Ollama provider (local LLM via OpenAI-compatible API)
/// Default provider for backward compatibility
/// </summary>
public class OllamaProvider : ILlmProvider
{
    public string Name => "ollama";

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
            stream = false,
            temperature,
            max_tokens = maxTokens,
            messages = new[] { new { role = "user", content = prompt } }
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
            stream = true,
            temperature,
            messages = new[] { new { role = "user", content = prompt } }
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
        var payload = new
        {
            model = modelName,
            stream = false,
            temperature,
            n,
            messages = new[] { new { role = "user", content = prompt } }
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
        // Ollama typically doesn't require authentication
        // But if apiKey is provided, add it as bearer token
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        }
    }
}
