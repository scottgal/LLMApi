using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace mostlylucid.mockllmapi.Services;

/// <summary>
/// Coordinates automatic chunking of large requests to fit within LLM token limits.
/// Transparently splits requests into optimal chunks, maintains consistency, and combines results.
/// </summary>
public class ChunkingCoordinator
{
    private readonly ILogger<ChunkingCoordinator> _logger;
    private readonly LLMockApiOptions _options;
    private const int EstimatedTokensPerChar = 4; // Rough estimate: 1 token ≈ 4 characters
    private const double PromptOverheadRatio = 0.25; // Reserve 25% of output tokens for prompt/overhead

    public ChunkingCoordinator(
        ILogger<ChunkingCoordinator> logger,
        IOptions<LLMockApiOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    /// <summary>
    /// Determines if chunking is needed for this request
    /// </summary>
    public bool ShouldChunk(HttpRequest request, string? shape, int requestedCount)
    {
        // Check if auto-chunking is globally disabled
        if (!_options.EnableAutoChunking)
        {
            _logger.LogDebug("Auto-chunking disabled in configuration");
            return false;
        }

        // Check for per-request opt-out
        if (request.Query.TryGetValue("autoChunk", out var autoChunkValue) &&
            string.Equals(autoChunkValue.ToString(), "false", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Auto-chunking disabled via query parameter");
            return false;
        }

        // If no count specified or count is 1, no chunking needed
        if (requestedCount <= 1)
        {
            return false;
        }

        // Estimate if the request would exceed token limits
        var estimatedTokensPerItem = EstimateTokensPerItem(shape);
        var estimatedTotalTokens = estimatedTokensPerItem * requestedCount;
        var availableOutputTokens = (int)(_options.MaxOutputTokens * (1 - PromptOverheadRatio));

        var needsChunking = estimatedTotalTokens > availableOutputTokens;

        if (needsChunking)
        {
            _logger.LogInformation(
                "Request needs chunking: {RequestedCount} items × {TokensPerItem} tokens/item = {TotalTokens} tokens > {AvailableTokens} available",
                requestedCount, estimatedTokensPerItem, estimatedTotalTokens, availableOutputTokens);
        }

        return needsChunking;
    }

    /// <summary>
    /// Calculates optimal chunk configuration for a request
    /// </summary>
    public ChunkingStrategy CalculateChunkingStrategy(string? shape, int requestedCount)
    {
        var estimatedTokensPerItem = EstimateTokensPerItem(shape);
        var availableOutputTokens = (int)(_options.MaxOutputTokens * (1 - PromptOverheadRatio));

        // Calculate how many items fit in a single chunk
        var itemsPerChunk = Math.Max(1, availableOutputTokens / estimatedTokensPerItem);

        // Calculate number of chunks needed
        var totalChunks = (int)Math.Ceiling((double)requestedCount / itemsPerChunk);

        // Adjust items per chunk to distribute evenly
        if (totalChunks > 1)
        {
            itemsPerChunk = (int)Math.Ceiling((double)requestedCount / totalChunks);
        }

        _logger.LogInformation(
            "Chunking strategy: {RequestedCount} items → {TotalChunks} chunks × ~{ItemsPerChunk} items/chunk " +
            "(estimated {TokensPerItem} tokens/item, {AvailableTokens} tokens available)",
            requestedCount, totalChunks, itemsPerChunk, estimatedTokensPerItem, availableOutputTokens);

        return new ChunkingStrategy
        {
            TotalItems = requestedCount,
            ItemsPerChunk = itemsPerChunk,
            TotalChunks = totalChunks,
            EstimatedTokensPerItem = estimatedTokensPerItem
        };
    }

    /// <summary>
    /// Extracts the requested count from HTTP request (query params, shape, or defaults to 1)
    /// Checks: ?count, ?limit, ?size, ?items, ?per_page query parameters
    /// Automatically caps at MaxItems configuration setting.
    /// </summary>
    public int ExtractRequestedCountFromRequest(HttpRequest request, string? shape)
    {
        // Priority 1: Explicit query parameters
        var queryKeys = new[] { "count", "limit", "size", "items", "per_page", "pageSize", "top" };
        foreach (var key in queryKeys)
        {
            if (request.Query.TryGetValue(key, out var value) && value.Count > 0)
            {
                if (int.TryParse(value[0], out var count) && count > 0)
                {
                    // Cap at MaxItems limit
                    if (count > _options.MaxItems)
                    {
                        _logger.LogWarning(
                            "AUTO-LIMIT: Request for {RequestedCount} items exceeds MaxItems limit ({MaxItems}). " +
                            "Capping to {MaxItems} items. Adjust MaxItems in configuration if you need more.",
                            count, _options.MaxItems, _options.MaxItems);
                        count = _options.MaxItems;
                    }

                    _logger.LogDebug("Found explicit count in query parameter '{Key}': {Count}", key, count);
                    return count;
                }
            }
        }

        // Priority 2: Look in shape JSON
        var shapeCount = ExtractRequestedCount(shape);
        if (shapeCount > 1)
        {
            // Cap at MaxItems limit
            if (shapeCount > _options.MaxItems)
            {
                _logger.LogWarning(
                    "AUTO-LIMIT: Shape requests {RequestedCount} items, exceeds MaxItems limit ({MaxItems}). Capping to {MaxItems}.",
                    shapeCount, _options.MaxItems, _options.MaxItems);
                shapeCount = _options.MaxItems;
            }
            return shapeCount;
        }

        // Priority 3: If shape is an array, default to reasonable count
        if (!string.IsNullOrWhiteSpace(shape) && shape.TrimStart().StartsWith("["))
        {
            // Array shape without explicit count - assume user wants multiple items
            // Default to 1 (no chunking needed) unless explicitly specified
            return 1;
        }

        return 1;
    }

    /// <summary>
    /// Extracts the requested count from shape JSON (looks for array lengths, counts, limits, etc.)
    /// </summary>
    public int ExtractRequestedCount(string? shape)
    {
        if (string.IsNullOrWhiteSpace(shape))
            return 1;

        try
        {
            // Try to parse as JSON
            using var doc = JsonDocument.Parse(shape);
            var root = doc.RootElement;

            // Look for array definitions with count/length hints
            // Pattern 1: [{...}] with explicit count - not directly in JSON, but inferred from context
            // Pattern 2: Look for "count", "limit", "size" properties
            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in root.EnumerateObject())
                {
                    var name = prop.Name.ToLowerInvariant();
                    if ((name == "count" || name == "limit" || name == "size" || name == "length") &&
                        prop.Value.ValueKind == JsonValueKind.Number)
                    {
                        var count = prop.Value.GetInt32();
                        if (count > 0)
                        {
                            _logger.LogDebug("Found explicit count in shape: {Count}", count);
                            return count;
                        }
                    }
                }
            }

            // Pattern 3: Array of objects - assume requesting multiple items
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                // Array notation suggests multiple items, but we don't know how many
                // Conservative default: assume user wants what they asked for elsewhere
                return 1; // Caller should provide explicit count
            }
        }
        catch (JsonException)
        {
            // Invalid JSON, can't extract count
        }

        return 1; // Default: single item
    }

    /// <summary>
    /// Estimates token count per item based on shape complexity
    /// </summary>
    private int EstimateTokensPerItem(string? shape)
    {
        if (string.IsNullOrWhiteSpace(shape))
        {
            // No shape provided - assume moderate complexity
            return 100;
        }

        // Rough estimation based on shape size and structure
        var baseTokens = shape.Length / EstimatedTokensPerChar;

        // Analyze shape complexity
        var complexity = AnalyzeShapeComplexity(shape);

        // Multiply by complexity factor (nested objects, arrays, etc. generate more content)
        var estimatedTokens = (int)(baseTokens * complexity.ComplexityMultiplier);

        // Add minimum floor and reasonable ceiling
        estimatedTokens = Math.Max(50, Math.Min(estimatedTokens, 1000));

        _logger.LogDebug(
            "Estimated {Tokens} tokens/item (shape size: {ShapeSize} chars, nesting: {Depth}, arrays: {Arrays}, complexity: {Multiplier:F2}x)",
            estimatedTokens, shape.Length, complexity.NestingDepth, complexity.ArrayCount, complexity.ComplexityMultiplier);

        return estimatedTokens;
    }

    /// <summary>
    /// Analyzes shape JSON complexity to better estimate output size
    /// </summary>
    private ShapeComplexity AnalyzeShapeComplexity(string shape)
    {
        var complexity = new ShapeComplexity();

        try
        {
            using var doc = JsonDocument.Parse(shape);
            complexity = AnalyzeElement(doc.RootElement, 0);
        }
        catch (JsonException)
        {
            // Fallback: use regex-based analysis
            complexity.NestingDepth = CountOccurrences(shape, "{") + CountOccurrences(shape, "[");
            complexity.ArrayCount = CountOccurrences(shape, "[");
            complexity.PropertyCount = CountOccurrences(shape, ":");
        }

        // Calculate complexity multiplier
        // Base: 1.0x
        // +0.5x per nesting level beyond 2
        // +0.3x per array
        // +0.05x per property beyond 5
        var multiplier = 1.0;
        multiplier += Math.Max(0, complexity.NestingDepth - 2) * 0.5;
        multiplier += complexity.ArrayCount * 0.3;
        multiplier += Math.Max(0, complexity.PropertyCount - 5) * 0.05;

        complexity.ComplexityMultiplier = Math.Max(1.0, Math.Min(multiplier, 5.0)); // Cap at 5x

        return complexity;
    }

    private ShapeComplexity AnalyzeElement(JsonElement element, int depth)
    {
        var complexity = new ShapeComplexity
        {
            NestingDepth = depth
        };

        if (element.ValueKind == JsonValueKind.Object)
        {
            complexity.PropertyCount = element.EnumerateObject().Count();

            foreach (var prop in element.EnumerateObject())
            {
                var childComplexity = AnalyzeElement(prop.Value, depth + 1);
                complexity.NestingDepth = Math.Max(complexity.NestingDepth, childComplexity.NestingDepth);
                complexity.ArrayCount += childComplexity.ArrayCount;
                complexity.PropertyCount += childComplexity.PropertyCount;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            complexity.ArrayCount = 1;

            if (element.GetArrayLength() > 0)
            {
                var firstChild = element[0];
                var childComplexity = AnalyzeElement(firstChild, depth + 1);
                complexity.NestingDepth = Math.Max(complexity.NestingDepth, childComplexity.NestingDepth);
                complexity.ArrayCount += childComplexity.ArrayCount;
                complexity.PropertyCount += childComplexity.PropertyCount;
            }
        }

        return complexity;
    }

    private int CountOccurrences(string text, string pattern)
    {
        return Regex.Matches(text, Regex.Escape(pattern)).Count;
    }

    /// <summary>
    /// Executes a request with automatic chunking if needed
    /// </summary>
    /// <param name="request">HTTP request</param>
    /// <param name="shape">JSON shape for the response</param>
    /// <param name="executeSingleRequest">Function to execute a single request (returns JSON string)</param>
    /// <param name="modifyShapeForChunk">Optional function to modify shape for each chunk (e.g., update count)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Combined JSON result (single item or array)</returns>
    public async Task<string> ExecuteWithChunkingAsync(
        HttpRequest request,
        string? shape,
        Func<string?, string?, Task<string>> executeSingleRequest,
        Func<string?, int, string?>? modifyShapeForChunk = null,
        CancellationToken cancellationToken = default)
    {
        // Extract requested count
        var requestedCount = ExtractRequestedCountFromRequest(request, shape);

        // Check if chunking is needed
        if (!ShouldChunk(request, shape, requestedCount))
        {
            _logger.LogDebug("No chunking needed for this request");
            return await executeSingleRequest(shape, null);
        }

        // Calculate chunking strategy
        var strategy = CalculateChunkingStrategy(shape, requestedCount);

        _logger.LogInformation(
            "AUTO-CHUNKING ENABLED: Breaking request into {TotalChunks} chunks to fit within {MaxTokens} token limit. " +
            "User requested {RequestedCount} items, will fetch {ItemsPerChunk} items per chunk.",
            strategy.TotalChunks, _options.MaxOutputTokens, requestedCount, strategy.ItemsPerChunk);

        // Execute chunks
        var previousResults = new List<string>();
        var allItems = new List<JsonElement>();

        for (int chunkNum = 1; chunkNum <= strategy.TotalChunks; chunkNum++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Calculate items for this chunk
            var startIdx = (chunkNum - 1) * strategy.ItemsPerChunk;
            var remainingItems = requestedCount - startIdx;
            var itemsInThisChunk = Math.Min(strategy.ItemsPerChunk, remainingItems);

            _logger.LogInformation(
                "AUTO-CHUNKING: Executing chunk {ChunkNum}/{TotalChunks} (items {StartIdx}-{EndIdx} of {Total})",
                chunkNum, strategy.TotalChunks, startIdx + 1, startIdx + itemsInThisChunk, requestedCount);

            // Modify shape for this chunk (update count if needed)
            var chunkShape = modifyShapeForChunk?.Invoke(shape, itemsInThisChunk) ?? shape;

            // Build context from previous chunks
            var chunkContext = BuildChunkContext(previousResults, chunkNum, strategy.TotalChunks);

            // Execute this chunk
            var chunkResult = await executeSingleRequest(chunkShape, chunkContext);
            previousResults.Add(chunkResult);

            // Parse and collect items
            try
            {
                using var doc = JsonDocument.Parse(chunkResult);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in root.EnumerateArray())
                    {
                        allItems.Add(item.Clone());
                    }
                    _logger.LogDebug("Chunk {ChunkNum} returned {ItemCount} items", chunkNum, root.GetArrayLength());
                }
                else if (root.ValueKind == JsonValueKind.Object)
                {
                    allItems.Add(root.Clone());
                    _logger.LogDebug("Chunk {ChunkNum} returned 1 item", chunkNum);
                }
            }
            catch (JsonException ex)
            {
                var preview = chunkResult.Length > 500 ? chunkResult.Substring(0, 500) + "..." : chunkResult;
                _logger.LogError(ex,
                    "Failed to parse chunk {ChunkNum} as JSON. Response preview: {Preview}",
                    chunkNum, preview);
                // If we can't parse it, we can't combine - just return what we have
                throw new InvalidOperationException(
                    $"Chunk {chunkNum} returned invalid JSON. Response: {preview}", ex);
            }
        }

        // Combine all items into a single JSON array
        var combinedJson = System.Text.Json.JsonSerializer.Serialize(allItems, new JsonSerializerOptions
        {
            WriteIndented = false
        });

        _logger.LogInformation(
            "AUTO-CHUNKING COMPLETE: Combined {TotalChunks} chunks into {TotalItems} items. " +
            "Original request: {RequestedCount} items, Final result: {ActualCount} items",
            strategy.TotalChunks, allItems.Count, requestedCount, allItems.Count);

        return combinedJson;
    }

    /// <summary>
    /// Creates context prompt for subsequent chunks to maintain consistency
    /// </summary>
    public string BuildChunkContext(List<string> previousChunkResults, int currentChunkNumber, int totalChunks)
    {
        if (previousChunkResults.Count == 0)
            return string.Empty;

        var context = $"\n\nIMPORTANT CONTEXT - Multi-part Response (Part {currentChunkNumber}/{totalChunks}):\n";
        context += "This is a continuation of a larger request. Previous parts have generated:\n";

        // Include summaries of previous chunks
        for (int i = 0; i < previousChunkResults.Count; i++)
        {
            var result = previousChunkResults[i];
            var summary = SummarizeChunkResult(result);
            context += $"  Part {i + 1}: {summary}\n";
        }

        context += "\nEnsure consistency with the above data (IDs, names, relationships, style).\n";
        context += "Continue numbering, IDs, and patterns logically from where the previous part left off.\n";
        context += "\nCRITICAL: Output MUST be a valid JSON array starting with [ and ending with ]. Do NOT output comma-separated objects.\n";

        return context;
    }

    /// <summary>
    /// Summarizes a chunk result for context (extracts key info without full content)
    /// </summary>
    private string SummarizeChunkResult(string result)
    {
        try
        {
            using var doc = JsonDocument.Parse(result);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                var count = root.GetArrayLength();
                if (count > 0)
                {
                    var first = root[0];
                    var last = root[count - 1];

                    // Extract identifying information from first and last items
                    var firstInfo = ExtractItemSummary(first);
                    var lastInfo = ExtractItemSummary(last);

                    return $"{count} items (first: {firstInfo}, last: {lastInfo})";
                }
                return $"{count} items";
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                return ExtractItemSummary(root);
            }
        }
        catch (JsonException)
        {
            // Fallback: use truncated content
        }

        return result.Length > 50 ? result.Substring(0, 50) + "..." : result;
    }

    private string ExtractItemSummary(JsonElement element)
    {
        var parts = new List<string>();

        // Look for common identifying properties
        if (element.TryGetProperty("id", out var id))
            parts.Add($"id={id.GetRawText()}");
        if (element.TryGetProperty("name", out var name))
            parts.Add($"name={name.GetRawText()}");
        if (element.TryGetProperty("email", out var email))
            parts.Add($"email={email.GetRawText()}");

        return parts.Count > 0 ? string.Join(", ", parts) : "item";
    }
}

/// <summary>
/// Describes how a request should be chunked
/// </summary>
public class ChunkingStrategy
{
    public int TotalItems { get; set; }
    public int ItemsPerChunk { get; set; }
    public int TotalChunks { get; set; }
    public int EstimatedTokensPerItem { get; set; }
}

/// <summary>
/// Represents the complexity of a JSON shape
/// </summary>
public class ShapeComplexity
{
    public int NestingDepth { get; set; }
    public int ArrayCount { get; set; }
    public int PropertyCount { get; set; }
    public double ComplexityMultiplier { get; set; } = 1.0;
}
