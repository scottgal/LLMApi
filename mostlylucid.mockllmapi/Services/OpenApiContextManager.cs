using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace mostlylucid.mockllmapi.Services;

/// <summary>
/// Manages shared contexts for OpenAPI endpoints to maintain consistency across related requests
/// </summary>
public class OpenApiContextManager
{
    private readonly ILogger<OpenApiContextManager> _logger;
    private readonly LLMockApiOptions _options;
    private readonly ConcurrentDictionary<string, ApiContext> _contexts;
    private const int MaxRecentCalls = 15;
    private const int SummarizeThreshold = 20;
    private const int EstimatedTokensPerChar = 4; // Rough estimate: 1 token ≈ 4 characters

    public OpenApiContextManager(
        ILogger<OpenApiContextManager> logger,
        IOptions<LLMockApiOptions> options)
    {
        _logger = logger;
        _options = options.Value;
        _contexts = new ConcurrentDictionary<string, ApiContext>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Adds a request/response pair to the context
    /// </summary>
    public void AddToContext(string contextName, string method, string path, string? requestBody, string responseBody)
    {
        if (string.IsNullOrWhiteSpace(contextName))
            return;

        var context = _contexts.GetOrAdd(contextName, _ => new ApiContext
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

        lock (context.RecentCalls)
        {
            context.RecentCalls.Add(call);

            // Extract shared data (IDs, names, etc.)
            ExtractSharedData(context, responseBody);

            // If we have too many calls, summarize older ones
            if (context.RecentCalls.Count > MaxRecentCalls)
            {
                SummarizeOldCalls(context);
            }
        }

        _logger.LogDebug("Added call to context '{Context}': {Method} {Path}", contextName, method, path);
    }

    /// <summary>
    /// Gets the context history formatted for inclusion in LLM prompts
    /// </summary>
    public string? GetContextForPrompt(string contextName)
    {
        if (string.IsNullOrWhiteSpace(contextName) || !_contexts.TryGetValue(contextName, out var context))
            return null;

        var sb = new StringBuilder();
        sb.AppendLine($"API Context: {contextName}");
        sb.AppendLine($"Total calls in session: {context.TotalCalls}");

        // Add summary if available
        if (!string.IsNullOrWhiteSpace(context.ContextSummary))
        {
            sb.AppendLine("\nEarlier activity summary:");
            sb.AppendLine(context.ContextSummary);
        }

        // Add shared data
        if (context.SharedData.Count > 0)
        {
            sb.AppendLine("\nShared data to maintain consistency:");
            foreach (var kvp in context.SharedData.Take(20)) // Limit to prevent context explosion
            {
                sb.AppendLine($"  {kvp.Key}: {kvp.Value}");
            }
        }

        // Add recent calls with dynamic truncation based on MaxInputTokens
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
    /// Estimates token count from text (rough approximation: 1 token ≈ 4 characters)
    /// </summary>
    private int EstimateTokens(string text)
    {
        return string.IsNullOrEmpty(text) ? 0 : text.Length / EstimatedTokensPerChar;
    }

    /// <summary>
    /// Gets a specific context
    /// </summary>
    public ApiContext? GetContext(string contextName)
    {
        _contexts.TryGetValue(contextName, out var context);
        return context;
    }

    /// <summary>
    /// Lists all contexts
    /// </summary>
    public List<ApiContextSummary> GetAllContexts()
    {
        return _contexts.Values.Select(ctx => new ApiContextSummary
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
        var removed = _contexts.TryRemove(contextName, out _);
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
        var count = _contexts.Count;
        _contexts.Clear();
        _logger.LogInformation("Cleared all contexts ({Count})", count);
    }

    /// <summary>
    /// Extracts shared data from response JSON (IDs, names, etc.)
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
                ExtractValueIfExists(context, firstItem, "id", "lastId");
                ExtractValueIfExists(context, firstItem, "userId", "lastUserId");
                ExtractValueIfExists(context, firstItem, "orderId", "lastOrderId");
                ExtractValueIfExists(context, firstItem, "productId", "lastProductId");
                ExtractValueIfExists(context, firstItem, "customerId", "lastCustomerId");
                ExtractValueIfExists(context, firstItem, "petId", "lastPetId");
                ExtractValueIfExists(context, firstItem, "name", "lastName");
                ExtractValueIfExists(context, firstItem, "username", "lastUsername");
                ExtractValueIfExists(context, firstItem, "email", "lastEmail");
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                // Extract common ID patterns
                ExtractValueIfExists(context, root, "id", "lastId");
                ExtractValueIfExists(context, root, "userId", "lastUserId");
                ExtractValueIfExists(context, root, "orderId", "lastOrderId");
                ExtractValueIfExists(context, root, "productId", "lastProductId");
                ExtractValueIfExists(context, root, "customerId", "lastCustomerId");
                ExtractValueIfExists(context, root, "petId", "lastPetId");

                // Extract common name patterns
                ExtractValueIfExists(context, root, "name", "lastName");
                ExtractValueIfExists(context, root, "username", "lastUsername");
                ExtractValueIfExists(context, root, "email", "lastEmail");
            }
        }
        catch (JsonException)
        {
            // Not valid JSON or can't parse, skip extraction
        }
    }

    private void ExtractValueIfExists(ApiContext context, JsonElement element, string jsonKey, string storageKey)
    {
        if (element.TryGetProperty(jsonKey, out var value))
        {
            var stringValue = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            };

            if (stringValue != null)
            {
                context.SharedData[storageKey] = stringValue;
                context.SharedData[jsonKey] = stringValue; // Store under both keys
            }
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
