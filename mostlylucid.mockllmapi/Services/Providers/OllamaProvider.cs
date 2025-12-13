using System.Text;
using System.Text.RegularExpressions;

namespace mostlylucid.mockllmapi.Services.Providers;

/// <summary>
/// Ollama provider with manual JSON handling for .NET 10 compatibility
/// Default provider for backward compatibility
/// </summary>
public class OllamaProvider : ILlmProvider
{
    public string Name => "ollama";

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

    private static string ExtractContentFromResponse(string jsonResponse)
    {
        // Manual JSON parsing to avoid reflection-based deserialization
        // Looking for: "choices":[{"message":{"content":"..."}}]
        var match = Regex.Match(jsonResponse, @"""choices"":\s*\[\s*\{\s*[^}]*?""message"":\s*\{\s*[^}]*?""content"":\s*""((?:[^""\\]|\\.)*?)""", RegexOptions.Singleline);
        if (match.Success)
        {
            var content = match.Groups[1].Value;
            // Unescape JSON string
            return content
                .Replace("\\n", "\n")
                .Replace("\\r", "\r")
                .Replace("\\t", "\t")
                .Replace("\\\"", "\"")
                .Replace("\\\\", "\\");
        }
        return "{}";
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

        var jsonPayload = $"{{\"model\":{escapedModel},\"stream\":false,\"temperature\":{temperature:F2}{maxTokensJson},\"messages\":[{{\"role\":\"user\",\"content\":{escapedPrompt}}}]}}";

        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
        };

        var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        return ExtractContentFromResponse(responseText);
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

        var jsonPayload = $"{{\"model\":{escapedModel},\"stream\":true,\"temperature\":{temperature:F2},\"messages\":[{{\"role\":\"user\",\"content\":{escapedPrompt}}}]}}";

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
        var results = new List<string>();

        // Ollama doesn't support n-completions in a single request like OpenAI
        // So we make n separate requests
        for (int i = 0; i < n; i++)
        {
            // Manually construct JSON to avoid reflection-based serialization
            var escapedPrompt = EscapeJsonString(prompt);
            var escapedModel = EscapeJsonString(modelName);

            var jsonPayload = $"{{\"model\":{escapedModel},\"stream\":false,\"temperature\":{temperature:F2},\"messages\":[{{\"role\":\"user\",\"content\":{escapedPrompt}}}]}}";

            using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            {
                Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
            };

            var response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            var content = ExtractContentFromResponse(responseText);
            if (!string.IsNullOrEmpty(content) && content != "{}")
            {
                results.Add(content);
            }
        }

        return results;
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
