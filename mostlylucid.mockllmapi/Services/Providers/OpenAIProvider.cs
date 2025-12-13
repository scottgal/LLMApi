using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using mostlylucid.mockllmapi.Models;

namespace mostlylucid.mockllmapi.Services.Providers;

/// <summary>
/// OpenAI provider (official OpenAI API)
/// Endpoint: https://api.openai.com/v1/
/// Models: gpt-4, gpt-4-turbo, gpt-3.5-turbo, etc.
/// </summary>
public class OpenAIProvider : ILlmProvider
{
    public string Name => "openai";

    private static string EscapeJsonString(string str)
    {
        // Manual JSON string escaping to avoid reflection-based serialization
        return "\"" + str
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t")
            + "\"";
    }

    public async Task<string> GetCompletionAsync(
        HttpClient client,
        string prompt,
        string modelName,
        double temperature,
        int? maxTokens,
        CancellationToken cancellationToken)
    {
        // Manually construct JSON to avoid reflection-based serialization
        var escapedPrompt = EscapeJsonString(prompt);
        var escapedModel = EscapeJsonString(modelName);
        var maxTokensJson = maxTokens.HasValue ? $",\"max_tokens\":{maxTokens.Value}" : "";

        var jsonPayload = $"{{\"model\":{escapedModel},\"messages\":[{{\"role\":\"user\",\"content\":{escapedPrompt}}}],\"temperature\":{temperature:F2}{maxTokensJson},\"stream\":false}}";

        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
        };

        var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<ChatCompletionLite?>(responseText);
        return result.HasValue ? result.Value.FirstContent ?? "{}" : "{}";
    }

    public async Task<HttpResponseMessage> GetStreamingCompletionAsync(
        HttpClient client,
        string prompt,
        string modelName,
        double temperature,
        CancellationToken cancellationToken)
    {
        // Manually construct JSON to avoid reflection-based serialization
        var escapedPrompt = EscapeJsonString(prompt);
        var escapedModel = EscapeJsonString(modelName);

        var jsonPayload = $"{{\"model\":{escapedModel},\"messages\":[{{\"role\":\"user\",\"content\":{escapedPrompt}}}],\"temperature\":{temperature:F2},\"stream\":true}}";

        var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
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
        // Manually construct JSON to avoid reflection-based serialization
        var escapedPrompt = EscapeJsonString(prompt);
        var escapedModel = EscapeJsonString(modelName);

        var jsonPayload = $"{{\"model\":{escapedModel},\"messages\":[{{\"role\":\"user\",\"content\":{escapedPrompt}}}],\"temperature\":{temperature:F2},\"n\":{n},\"stream\":false}}";

        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
        };

        var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<ChatCompletionLite?>(responseText);
        var list = new List<string>();

        if (result.HasValue && result.Value.Choices != null)
        {
            foreach (var choice in result.Value.Choices)
            {
                if (!string.IsNullOrEmpty(choice.Message.Content))
                    list.Add(choice.Message.Content);
            }
        }

        return list;
    }

    public void ConfigureClient(HttpClient client, string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "OpenAI provider requires an API key. Set 'ApiKey' in backend configuration.");
        }

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
    }
}
