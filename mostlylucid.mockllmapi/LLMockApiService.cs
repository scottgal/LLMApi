using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace mostlylucid.mockllmapi;

/// <summary>
/// Service for handling LLM-powered mock API requests
/// </summary>
public class LLMockApiService
{
    private readonly LLMockApiOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LLMockApiService> _logger;

    public LLMockApiService(
        IOptions<LLMockApiOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<LLMockApiService> logger)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<string> ReadBodyAsync(HttpRequest request)
    {
        if (request.ContentLength is > 0)
        {
            using var reader = new StreamReader(request.Body);
            return await reader.ReadToEndAsync();
        }
        return string.Empty;
    }

    public string? ExtractShape(HttpRequest request, string? body)
    {
        // 1) Query parameter
        if (request.Query.TryGetValue("shape", out var shapeQuery) && shapeQuery.Count > 0)
        {
            return shapeQuery[0];
        }
        // 2) Header
        if (request.Headers.TryGetValue("X-Response-Shape", out var shapeHeader) && shapeHeader.Count > 0)
        {
            return shapeHeader[0];
        }
        // 3) Body property
        if (!string.IsNullOrWhiteSpace(body) &&
            request.ContentType != null &&
            request.ContentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("shape", out var shapeNode))
                {
                    return shapeNode.GetRawText();
                }
            }
            catch
            {
                // ignore JSON parse errors
            }
        }
        return null;
    }

    public string BuildPrompt(string method, string fullPathWithQuery, string? body, string? shape, bool streaming)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var randomSeed = Guid.NewGuid().ToString("N")[..8];

        // Use custom template if provided
        if (!string.IsNullOrWhiteSpace(_options.CustomPromptTemplate) && !streaming)
        {
            return _options.CustomPromptTemplate
                .Replace("{method}", method)
                .Replace("{path}", fullPathWithQuery)
                .Replace("{body}", body ?? "none")
                .Replace("{randomSeed}", randomSeed)
                .Replace("{timestamp}", timestamp.ToString())
                .Replace("{shape}", shape ?? "");
        }

        if (!string.IsNullOrWhiteSpace(_options.CustomStreamingPromptTemplate) && streaming)
        {
            return _options.CustomStreamingPromptTemplate
                .Replace("{method}", method)
                .Replace("{path}", fullPathWithQuery)
                .Replace("{body}", body ?? "none")
                .Replace("{randomSeed}", randomSeed)
                .Replace("{timestamp}", timestamp.ToString())
                .Replace("{shape}", shape ?? "");
        }

        var prompt = streaming
            ? $@"Generate realistic mock API data. Output ONLY raw JSON — no markdown, no code fences, no explanatory text.

IMPORTANT: Be highly creative and varied. Use random values, different names, varied numbers, diverse data.
Each request should produce COMPLETELY DIFFERENT data. Random seed: {randomSeed}, timestamp: {timestamp}

Method: {method}
Path: {fullPathWithQuery}
Body: {body ?? "none"}
"
            : $@"Generate a realistic mock API response. Output ONLY raw JSON — no markdown, no code fences, no explanatory text.

IMPORTANT: Be highly creative and varied. Use random values, different names, varied numbers, diverse data.
Each request should produce COMPLETELY DIFFERENT data. Random seed: {randomSeed}, timestamp: {timestamp}

Method: {method}
Path: {fullPathWithQuery}
Body: {body ?? "none"}
";

        if (!string.IsNullOrWhiteSpace(shape))
        {
            prompt += "\nSHAPE REQUIREMENT: Your output MUST strictly conform to this JSON shape (exact properties, casing, structure).\nFill with realistic, varied sample data matching the implied types.\nShape: " + shape + "\n";
        }

        return prompt;
    }

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

    public HttpClient CreateHttpClient()
    {
        var client = _httpClientFactory.CreateClient("LLMockApi");
        client.BaseAddress = new Uri(_options.BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        return client;
    }
}
