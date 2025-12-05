using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace mostlylucid.mockllmapi.Services;

/// <summary>
/// Manages shared contexts for OpenAPI endpoints to maintain consistency across related requests.
/// Uses IContextStore for storage with automatic expiration (default: 15 minutes of inactivity).
/// </summary>
public class OpenApiContextManager
{
    private readonly ILogger<OpenApiContextManager> _logger;
    private readonly LLMockApiOptions _options;
    private readonly IContextStore _contextStore;
    private readonly object _contextLock = new object(); // Global lock for thread safety
    private const int MaxRecentCalls = 15;
    private const int SummarizeThreshold = 20;

    // Token estimation constants - more accurate than simple character division
    // Based on OpenAI's tokenization patterns: avg ~4 chars per token for English
    // but JSON/code tends to be ~3 chars per token due to punctuation and keywords
    private const double TokensPerCharForJson = 0.33; // ~3 chars per token for JSON
    private const double TokensPerCharForText = 0.25; // ~4 chars per token for natural text

    public OpenApiContextManager(
        ILogger<OpenApiContextManager> logger,
        IOptions<LLMockApiOptions> options,
        IContextStore contextStore)
    {
        _logger = logger;
        _options = options.Value;
        _contextStore = contextStore;
    }

    /// <summary>
    /// Adds a request/response pair to the context
    /// </summary>
    public void AddToContext(string contextName, string method, string path, string? requestBody, string responseBody)
    {
        if (string.IsNullOrWhiteSpace(contextName))
            return;

        var context = _contextStore.GetOrAdd(contextName, _ => new ApiContext
        {
            Name = contextName,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUsedAt = DateTimeOffset.UtcNow,
            RecentCalls = new List<RequestSummary>(),
            SharedData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            ContextSummary = string.Empty,
            TotalCalls = 0
        });

        context.LastUsedAt = DateTimeOffset.UtcNow;
        _contextStore.TouchContext(contextName); // Update sliding expiration
        context.TotalCalls++;

        // Add the call
        var call = new RequestSummary
        {
            Timestamp = DateTimeOffset.UtcNow,
            Method = method,
            Path = path,
            RequestBody = requestBody,
            ResponseBody = responseBody
        };

        // Use a more comprehensive lock that covers both RecentCalls and SharedData
        // to prevent race conditions when extracting data while building prompts
        lock (_contextLock)
        {
            lock (context.RecentCalls)
            {
                context.RecentCalls.Add(call);

                // Extract shared data (IDs, names, etc.) - now also protected by _contextLock
                ExtractSharedData(context, responseBody);

                // If we have too many calls, summarize older ones
                if (context.RecentCalls.Count > MaxRecentCalls)
                {
                    SummarizeOldCalls(context);
                }
            }
        }

        _logger.LogDebug("Added call to context '{Context}': {Method} {Path}", contextName, method, path);
    }

    /// <summary>
    /// Gets the context history formatted for inclusion in LLM prompts
    /// </summary>
    public string? GetContextForPrompt(string contextName)
    {
        if (string.IsNullOrWhiteSpace(contextName) || !_contextStore.TryGetValue(contextName, out var context) || context == null)
            return null;

        _contextStore.TouchContext(contextName); // Refresh sliding expiration

        var sb = new StringBuilder();
        sb.AppendLine($"API Context: {contextName}");
        sb.AppendLine($"Total calls in session: {context.TotalCalls}");

        // Add summary if available
        if (!string.IsNullOrWhiteSpace(context.ContextSummary))
        {
            sb.AppendLine("\nEarlier activity summary:");
            sb.AppendLine(context.ContextSummary);
        }

        // Add shared data - take a snapshot under lock for thread safety
        List<KeyValuePair<string, string>> sharedDataSnapshot;
        lock (_contextLock)
        {
            sharedDataSnapshot = context.SharedData.Take(20).ToList();
        }

        if (sharedDataSnapshot.Count > 0)
        {
            sb.AppendLine("\nShared data to maintain consistency:");
            foreach (var kvp in sharedDataSnapshot)
            {
                sb.AppendLine($"  {kvp.Key}: {kvp.Value}");
            }
        }

        // Add recent calls with dynamic truncation based on MaxInputTokens
        lock (_contextLock)
        lock (context.RecentCalls)
        {
            if (context.RecentCalls.Count > 0)
            {
                sb.AppendLine("\nRecent API calls:");

                // Calculate tokens used so far (base context: summary + shared data)
                var baseTokens = EstimateTokens(sb.ToString());

                // Reserve 20% of max tokens for context history (80% for base prompt)
                var maxContextTokens = (int)(_options.MaxInputTokens * 0.2);
                var remainingTokens = maxContextTokens - baseTokens;

                _logger.LogDebug("Context '{Context}': Base tokens={BaseTokens}, Max context tokens={MaxContext}, Remaining={Remaining}",
                    contextName, baseTokens, maxContextTokens, remainingTokens);

                // Add calls from most recent, stopping if we exceed token limit
                var recentCalls = context.RecentCalls.TakeLast(MaxRecentCalls).ToList();
                var callsToInclude = new List<string>();
                var droppedCalls = new List<string>();

                foreach (var call in recentCalls.AsEnumerable().Reverse())
                {
                    var callText = new StringBuilder();
                    callText.AppendLine($"  [{call.Timestamp:HH:mm:ss}] {call.Method} {call.Path}");
                    if (!string.IsNullOrWhiteSpace(call.RequestBody))
                    {
                        callText.AppendLine($"    Request: {TruncateJson(call.RequestBody, 200)}");
                    }
                    callText.AppendLine($"    Response: {TruncateJson(call.ResponseBody, 300)}");

                    var callTokens = EstimateTokens(callText.ToString());
                    if (remainingTokens - callTokens < 0 && callsToInclude.Count > 0)
                    {
                        // Can't fit this call, but we have at least one
                        droppedCalls.Add($"{call.Method} {call.Path}");
                        continue;
                    }

                    callsToInclude.Insert(0, callText.ToString());
                    remainingTokens -= callTokens;
                }

                if (droppedCalls.Count > 0)
                {
                    _logger.LogInformation(
                        "Context '{Context}': Dropped {DroppedCount}/{TotalCount} calls to fit {MaxTokens} token limit (20% of max). " +
                        "Dropped: [{DroppedCalls}]",
                        contextName, droppedCalls.Count, recentCalls.Count, _options.MaxInputTokens,
                        string.Join(", ", droppedCalls));
                }

                foreach (var callText in callsToInclude)
                {
                    sb.Append(callText);
                }

                if (callsToInclude.Count < recentCalls.Count)
                {
                    sb.AppendLine($"  ... ({recentCalls.Count - callsToInclude.Count} earlier calls omitted to fit {_options.MaxInputTokens} token limit)");
                }
            }
        }

        sb.AppendLine("\nGenerate a response that maintains consistency with the above context.");

        return sb.ToString();
    }

    /// <summary>
    /// Estimates token count from text using a more accurate algorithm.
    /// JSON/code typically has ~3 chars per token due to punctuation,
    /// while natural text averages ~4 chars per token.
    /// This method analyzes the content type for better accuracy.
    /// </summary>
    private int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        // Count JSON-like characters to determine content type ratio
        var jsonChars = 0;
        var totalChars = text.Length;

        foreach (var c in text)
        {
            if (c == '{' || c == '}' || c == '[' || c == ']' || c == ':' || c == ',' || c == '"')
                jsonChars++;
        }

        // Calculate ratio of JSON to total content
        var jsonRatio = (double)jsonChars / totalChars;

        // Use weighted average based on content type
        // High JSON ratio (>10%) uses JSON tokenization, otherwise use text tokenization
        var tokensPerChar = jsonRatio > 0.1 ? TokensPerCharForJson : TokensPerCharForText;

        // Add extra tokens for special patterns that tokenize separately:
        // - Numbers often become 1-2 tokens each
        // - Punctuation can be separate tokens
        var estimatedTokens = (int)(totalChars * tokensPerChar);

        // Add buffer for safety (10% overhead)
        return (int)(estimatedTokens * 1.1);
    }

    /// <summary>
    /// Gets a specific context (read-only - does not update timestamps)
    /// </summary>
    public ApiContext? GetContext(string contextName)
    {
        _contextStore.TryGetValue(contextName, out var context);
        return context;
    }

    /// <summary>
    /// Lists all contexts
    /// </summary>
    public List<ApiContextSummary> GetAllContexts()
    {
        return _contextStore.GetAllContexts().Select(ctx => new ApiContextSummary
        {
            Name = ctx.Name,
            TotalCalls = ctx.TotalCalls,
            RecentCallCount = ctx.RecentCalls.Count,
            SharedDataCount = ctx.SharedData.Count,
            CreatedAt = ctx.CreatedAt,
            LastUsedAt = ctx.LastUsedAt,
            HasSummary = !string.IsNullOrWhiteSpace(ctx.ContextSummary)
        }).ToList();
    }

    /// <summary>
    /// Clears a specific context
    /// </summary>
    public bool ClearContext(string contextName)
    {
        var removed = _contextStore.TryRemove(contextName, out _);
        if (removed)
        {
            _logger.LogInformation("Cleared context: {Context}", contextName);
        }
        return removed;
    }

    /// <summary>
    /// Clears all contexts
    /// </summary>
    public void ClearAllContexts()
    {
        var count = _contextStore.Count;
        _contextStore.Clear();
        _logger.LogInformation("Cleared all contexts ({Count})", count);
    }

    /// <summary>
    /// Adds journey state data to context's SharedData.
    /// Journey keys are prefixed with "journey." to avoid conflicts.
    /// </summary>
    public void AddJourneyState(string contextName, Dictionary<string, string> journeyState)
    {
        if (string.IsNullOrWhiteSpace(contextName) || journeyState == null || journeyState.Count == 0)
            return;

        if (!_contextStore.TryGetValue(contextName, out var context) || context == null)
        {
            // Create context if it doesn't exist
            context = _contextStore.GetOrAdd(contextName, _ => new ApiContext
            {
                Name = contextName,
                CreatedAt = DateTimeOffset.UtcNow,
                LastUsedAt = DateTimeOffset.UtcNow,
                RecentCalls = new List<RequestSummary>(),
                SharedData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                ContextSummary = string.Empty,
                TotalCalls = 0
            });
        }

        lock (_contextLock)
        {
            foreach (var kvp in journeyState)
            {
                context.SharedData[kvp.Key] = kvp.Value;
            }
        }

        _contextStore.TouchContext(contextName);
        _logger.LogDebug("Added journey state to context '{Context}'", contextName);
    }

    /// <summary>
    /// Gets the shared data from a context (thread-safe snapshot).
    /// </summary>
    public IReadOnlyDictionary<string, string>? GetSharedData(string contextName)
    {
        if (string.IsNullOrWhiteSpace(contextName) || !_contextStore.TryGetValue(contextName, out var context) || context == null)
            return null;

        lock (_contextLock)
        {
            return new Dictionary<string, string>(context.SharedData, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Extracts shared data from response JSON - captures ALL fields dynamically
    /// </summary>
    private void ExtractSharedData(ApiContext context, string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            // If response is an array, extract from first item
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                var firstItem = root[0];
                ExtractAllFields(context, firstItem);
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                ExtractAllFields(context, root);
            }
        }
        catch (JsonException)
        {
            // Not valid JSON or can't parse, skip extraction
        }
    }

    /// <summary>
    /// Recursively extracts all fields from a JSON object up to maxDepth levels
    /// </summary>
    private void ExtractAllFields(ApiContext context, JsonElement element, string prefix = "", int depth = 0, int maxDepth = 2)
    {
        if (element.ValueKind != JsonValueKind.Object || depth > maxDepth)
            return;

        foreach (var property in element.EnumerateObject())
        {
            var key = string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}.{property.Name}";

            switch (property.Value.ValueKind)
            {
                case JsonValueKind.String:
                    var strValue = property.Value.GetString();
                    if (strValue != null)
                    {
                        // Truncate long string values to prevent SharedData bloat
                        var truncatedValue = strValue.Length > 100 ? strValue.Substring(0, 100) + "..." : strValue;
                        context.SharedData[key] = truncatedValue;
                        AddLegacyKey(context, property.Name, truncatedValue, depth);
                    }
                    break;

                case JsonValueKind.Number:
                    var numValue = property.Value.GetRawText();
                    context.SharedData[key] = numValue;
                    AddLegacyKey(context, property.Name, numValue, depth);
                    break;

                case JsonValueKind.True:
                    context.SharedData[key] = "true";
                    break;

                case JsonValueKind.False:
                    context.SharedData[key] = "false";
                    break;

                case JsonValueKind.Object:
                    // Recurse into nested objects
                    ExtractAllFields(context, property.Value, key, depth + 1, maxDepth);
                    break;

                case JsonValueKind.Array:
                    // For arrays, store count and optionally first item
                    var arrayLength = property.Value.GetArrayLength();
                    context.SharedData[$"{key}.length"] = arrayLength.ToString();

                    if (arrayLength > 0 && depth < maxDepth)
                    {
                        var firstArrayItem = property.Value[0];
                        if (firstArrayItem.ValueKind == JsonValueKind.Object)
                        {
                            ExtractAllFields(context, firstArrayItem, $"{key}[0]", depth + 1, maxDepth);
                        }
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Adds legacy "last*" keys for common patterns to maintain backward compatibility
    /// </summary>
    private void AddLegacyKey(ApiContext context, string fieldName, string value, int depth)
    {
        // Only add legacy "last*" keys for top-level fields
        if (depth > 0)
            return;

        var lowerKey = fieldName.ToLowerInvariant();
        // Add "last*" prefix for common ID patterns (backward compatibility)
        if (lowerKey.EndsWith("id") || lowerKey == "name" || lowerKey == "username" || lowerKey == "email")
        {
            var legacyKey = $"last{char.ToUpper(fieldName[0])}{fieldName.Substring(1)}";
            context.SharedData[legacyKey] = value;
        }
    }

    /// <summary>
    /// Summarizes older calls to reduce context size
    /// This is a simplified version - in production you'd call an LLM to create the summary
    /// </summary>
    private void SummarizeOldCalls(ApiContext context)
    {
        lock (context.RecentCalls)
        {
            if (context.RecentCalls.Count <= MaxRecentCalls)
                return;

            var toSummarize = context.RecentCalls.Take(context.RecentCalls.Count - MaxRecentCalls).ToList();

            // Simple summary: just list what was called
            var summary = new StringBuilder();
            summary.AppendLine($"Earlier calls ({toSummarize.Count}):");

            var groupedByPath = toSummarize.GroupBy(c => $"{c.Method} {c.Path.Split('?')[0]}");
            foreach (var group in groupedByPath)
            {
                summary.AppendLine($"  {group.Key} - called {group.Count()} time(s)");
            }

            // Update or append to existing summary
            if (!string.IsNullOrWhiteSpace(context.ContextSummary))
            {
                context.ContextSummary += "\n" + summary.ToString();
            }
            else
            {
                context.ContextSummary = summary.ToString();
            }

            // Remove the summarized calls, keep only recent ones
            context.RecentCalls.RemoveRange(0, toSummarize.Count);

            _logger.LogInformation("Summarized {Count} old calls in context '{Context}'", toSummarize.Count, context.Name);
        }
    }

    private string TruncateJson(string json, int maxLength)
    {
        if (json.Length <= maxLength)
            return json;

        return json.Substring(0, maxLength) + "...";
    }
}

/// <summary>
/// Represents a shared context across related API calls
/// </summary>
public class ApiContext
{
    public string Name { get; set; } = string.Empty;
    public int TotalCalls { get; set; }
    public List<RequestSummary> RecentCalls { get; set; } = new();
    public Dictionary<string, string> SharedData { get; set; } = new();
    public string ContextSummary { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastUsedAt { get; set; }
}

/// <summary>
/// Summary of a single request/response
/// </summary>
public class RequestSummary
{
    public DateTimeOffset Timestamp { get; set; }
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string? RequestBody { get; set; }
    public string ResponseBody { get; set; } = string.Empty;
}

/// <summary>
/// Summary information about a context (for API responses)
/// </summary>
public class ApiContextSummary
{
    public string Name { get; set; } = string.Empty;
    public int TotalCalls { get; set; }
    public int RecentCallCount { get; set; }
    public int SharedDataCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastUsedAt { get; set; }
    public bool HasSummary { get; set; }
}
