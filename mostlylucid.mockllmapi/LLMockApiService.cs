using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using System.IO.Hashing;
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

    private const int MaxSchemaHeaderLength = 4000;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, CacheEntry> Cache = new();

    private class CacheEntry
    {
        // Depleting queue of cached responses. Items are consumed (dequeued) once.
        public Queue<string> Responses { get; } = new();
        // Prevents multiple concurrent background refills.
        public bool IsRefilling;
        // Indicates whether we've performed the initial prime for this key.
        public bool IsPrimed;
        public DateTime CreatedUtc { get; } = DateTime.UtcNow;
        public System.Threading.SemaphoreSlim Gate { get; } = new(1,1);
    }

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

    public string? ExtractShapeAndCacheCount(HttpRequest request, string? body, out int cacheCount)
    {
        cacheCount = 0;
        var shapeText = ExtractShape(request, body);
        if (string.IsNullOrWhiteSpace(shapeText)) return null;

        try
        {
            using var doc = JsonDocument.Parse(shapeText);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return shapeText; // not an object, cannot contain cache hint
            }

            // Build sanitized object without cache hints
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    var name = prop.Name;
                    if (string.Equals(name, "$cache", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, "$cacheCount", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, "cache", StringComparison.OrdinalIgnoreCase))
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt32(out var n) && n > 0)
                        {
                            cacheCount = n;
                        }
                        else if (prop.Value.ValueKind == JsonValueKind.String && int.TryParse(prop.Value.GetString(), out var ns) && ns > 0)
                        {
                            cacheCount = ns;
                        }
                        continue; // skip writing cache hint
                    }
                    prop.WriteTo(writer);
                }
                writer.WriteEndObject();
            }
            var sanitized = Encoding.UTF8.GetString(stream.ToArray());
            return sanitized;
        }
        catch
        {
            return shapeText; // if invalid JSON, ignore cache count
        }
    }

    public bool ShouldIncludeSchema(HttpRequest request)
    {
        if (request.Query.TryGetValue("includeSchema", out var includeParam) && includeParam.Count > 0)
        {
            var val = includeParam[0];
            return string.Equals(val, "true", StringComparison.OrdinalIgnoreCase) || val == "1";
        }
        return _options.IncludeShapeInResponse;
    }

    public void TryAddSchemaHeader(HttpContext context, string? shape)
    {
        try
        {
            if (!ShouldIncludeSchema(context.Request)) return;
            if (string.IsNullOrWhiteSpace(shape)) return;
            // Only add header if shape is within limit
            if (shape.Length <= MaxSchemaHeaderLength)
            {
                context.Response.Headers["X-Response-Schema"] = shape;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add X-Response-Schema header.");
            // Swallow errors to avoid impacting response
        }
    }

    private static ulong ComputeCacheKey(string method, string fullPathWithQuery, string? shape)
    {
        var input = Encoding.UTF8.GetBytes(string.Concat(method, "|", fullPathWithQuery, "|", shape ?? string.Empty));
        var hash = System.IO.Hashing.XxHash64.Hash(input);
        return BitConverter.ToUInt64(hash, 0);
    }

    private async Task<string> FetchOnceAsync(string method, string fullPathWithQuery, string? body, string? shape)
    {
        var prompt = BuildPrompt(method, fullPathWithQuery, body, shape, streaming: false);
        using var client = CreateHttpClient();
        var payload = BuildChatRequest(prompt, stream: false);
        using var httpReq = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(payload)
        };
        using var httpRes = await client.SendAsync(httpReq);
        httpRes.EnsureSuccessStatusCode();
        var result = await httpRes.Content.ReadFromJsonAsync<ChatCompletionLite>();
        return result.FirstContent ?? "{}";
    }

    public async Task<string> GetResponseWithCachingAsync(string method, string fullPathWithQuery, string? body, string? shape, int cacheCount)
    {
        if (cacheCount <= 0)
        {
            return await FetchOnceAsync(method, fullPathWithQuery, body, shape);
        }

        var key = ComputeCacheKey(method, fullPathWithQuery, shape);
        var entry = Cache.GetOrAdd(key, static _ => new CacheEntry());
        var target = Math.Max(1, Math.Min(cacheCount, _options.MaxCachePerKey));

        string? chosen = null;
        bool scheduleRefill = false;

        await entry.Gate.WaitAsync();
        try
        {
            // Initial prime only once per key
            if (!entry.IsPrimed)
            {
                for (int i = 0; i < target; i++)
                {
                    try
                    {
                        var content = await FetchOnceAsync(method, fullPathWithQuery, body, shape);
                        if (!string.IsNullOrEmpty(content) && !entry.Responses.Contains(content))
                        {
                            entry.Responses.Enqueue(content);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error prefetching cached response for {Path}", fullPathWithQuery);
                    }
                }
                entry.IsPrimed = true;
            }

            // Serve one and deplete
            if (entry.Responses.Count > 0)
            {
                chosen = entry.Responses.Dequeue();
                if (entry.Responses.Count == 0 && !entry.IsRefilling)
                {
                    // Trigger background refill of a new batch
                    entry.IsRefilling = true;
                    scheduleRefill = true;
                }
            }
        }
        finally
        {
            entry.Gate.Release();
        }

        if (scheduleRefill)
        {
            _ = Task.Run(() => RefillCacheAsync(entry, target, method, fullPathWithQuery, body, shape));
        }

        // If we served from cache, return it
        if (chosen != null)
        {
            return chosen;
        }

        // Fallback if cache was empty and not yet refilled
        return await FetchOnceAsync(method, fullPathWithQuery, body, shape);
    }

    private async Task RefillCacheAsync(CacheEntry entry, int target, string method, string fullPathWithQuery, string? body, string? shape)
    {
        try
        {
            int attempts = 0;
            while (true)
            {
                // Check how many we still need
                await entry.Gate.WaitAsync();
                int missing;
                try
                {
                    missing = target - entry.Responses.Count;
                    if (missing <= 0)
                    {
                        return;
                    }
                }
                finally
                {
                    entry.Gate.Release();
                }

                // Avoid infinite loops
                if (attempts++ > target * 5)
                {
                    return;
                }

                string? content = null;
                try
                {
                    content = await FetchOnceAsync(method, fullPathWithQuery, body, shape);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Background refill fetch failed for {Path}", fullPathWithQuery);
                    await Task.Delay(50);
                }

                if (string.IsNullOrEmpty(content))
                {
                    continue;
                }

                await entry.Gate.WaitAsync();
                try
                {
                    if (!entry.Responses.Contains(content))
                    {
                        entry.Responses.Enqueue(content);
                    }
                }
                finally
                {
                    entry.Gate.Release();
                }
            }
        }
        finally
        {
            await entry.Gate.WaitAsync();
            try { entry.IsRefilling = false; }
            finally { entry.Gate.Release(); }
        }
    }

    public string BuildPrompt(string method, string fullPathWithQuery, string? body, string? shape, bool streaming)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var randomSeed = Guid.NewGuid().ToString("N")[..8];

        string ApplyTemplate(string template)
        {
            return template
                .Replace("{method}", method)
                .Replace("{path}", fullPathWithQuery)
                .Replace("{body}", body ?? "none")
                .Replace("{randomSeed}", randomSeed)
                .Replace("{timestamp}", timestamp.ToString())
                .Replace("{shape}", shape ?? "");
        }

        // Use custom template if provided
        if (!string.IsNullOrWhiteSpace(_options.CustomPromptTemplate) && !streaming)
        {
            return ApplyTemplate(_options.CustomPromptTemplate);
        }

        if (!string.IsNullOrWhiteSpace(_options.CustomStreamingPromptTemplate) && streaming)
        {
            return ApplyTemplate(_options.CustomStreamingPromptTemplate);
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
