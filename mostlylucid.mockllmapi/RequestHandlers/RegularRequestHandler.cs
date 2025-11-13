using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using mostlylucid.mockllmapi.Models;
using mostlylucid.mockllmapi.Services;

namespace mostlylucid.mockllmapi.RequestHandlers;

/// <summary>
/// Handles non-streaming mock API requests with automatic chunking and rate limiting support
/// </summary>
public class RegularRequestHandler
{
    private readonly LLMockApiOptions _options;
    private readonly ShapeExtractor _shapeExtractor;
    private readonly ContextExtractor _contextExtractor;
    private readonly OpenApiContextManager _contextManager;
    private readonly PromptBuilder _promptBuilder;
    private readonly LlmClient _llmClient;
    private readonly CacheManager _cacheManager;
    private readonly DelayHelper _delayHelper;
    private readonly ChunkingCoordinator _chunkingCoordinator;
    private readonly RateLimitService _rateLimitService;
    private readonly BatchingCoordinator _batchingCoordinator;
    private readonly ILogger<RegularRequestHandler> _logger;

    private const int MaxSchemaHeaderLength = 4000;

    public RegularRequestHandler(
        IOptions<LLMockApiOptions> options,
        ShapeExtractor shapeExtractor,
        ContextExtractor contextExtractor,
        OpenApiContextManager contextManager,
        PromptBuilder promptBuilder,
        LlmClient llmClient,
        CacheManager cacheManager,
        DelayHelper delayHelper,
        ChunkingCoordinator chunkingCoordinator,
        RateLimitService rateLimitService,
        BatchingCoordinator batchingCoordinator,
        ILogger<RegularRequestHandler> logger)
    {
        _options = options.Value;
        _shapeExtractor = shapeExtractor;
        _contextExtractor = contextExtractor;
        _contextManager = contextManager;
        _promptBuilder = promptBuilder;
        _llmClient = llmClient;
        _cacheManager = cacheManager;
        _delayHelper = delayHelper;
        _chunkingCoordinator = chunkingCoordinator;
        _rateLimitService = rateLimitService;
        _batchingCoordinator = batchingCoordinator;
        _logger = logger;
    }

    /// <summary>
    /// Handles a regular (non-streaming) request with automatic chunking and rate limiting support
    /// </summary>
    public async Task<string> HandleRequestAsync(
        string method,
        string fullPathWithQuery,
        string? body,
        HttpRequest request,
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        // Start overall timing
        var overallStopwatch = Stopwatch.StartNew();

        // Apply random request delay if configured
        await _delayHelper.ApplyRequestDelayAsync(cancellationToken);

        // Extract shape information
        var shapeInfo = _shapeExtractor.ExtractShapeInfo(request, body);

        // Check if error simulation is requested
        if (shapeInfo.ErrorConfig != null)
        {
            context.Response.StatusCode = shapeInfo.ErrorConfig.StatusCode;
            _logger.LogDebug("Returning simulated error: {StatusCode} - {Message}",
                shapeInfo.ErrorConfig.StatusCode, shapeInfo.ErrorConfig.GetMessage());
            return shapeInfo.ErrorConfig.ToJson();
        }

        // Check for n-completions parameter
        var nCompletions = GetNCompletionsParameter(request);

        // Check for rate limiting parameters
        var rateLimitDelay = GetRateLimitDelayParameter(request);
        var rateLimitStrategy = GetRateLimitStrategyParameter(request);

        // If n-completions and rate limiting are requested, use BatchingCoordinator
        if (nCompletions > 1 && (_options.EnableRateLimiting || rateLimitDelay != null))
        {
            return await HandleBatchedRequestAsync(
                method, fullPathWithQuery, body, request, context,
                nCompletions, rateLimitDelay, rateLimitStrategy,
                overallStopwatch, cancellationToken);
        }

        // Extract context name
        var contextName = _contextExtractor.ExtractContextName(request, body);

        // Get context history if context is specified
        var contextHistory = !string.IsNullOrWhiteSpace(contextName)
            ? _contextManager.GetContextForPrompt(contextName)
            : null;

        // Execute with automatic chunking if needed
        var content = await _chunkingCoordinator.ExecuteWithChunkingAsync(
            request,
            shapeInfo.Shape,
            async (chunkShape, chunkContext) =>
            {
                // Combine context history with chunk context
                var fullContext = contextHistory;
                if (!string.IsNullOrWhiteSpace(chunkContext))
                {
                    fullContext = string.IsNullOrWhiteSpace(fullContext)
                        ? chunkContext
                        : fullContext + "\n" + chunkContext;
                }

                // Get response (with caching if requested and not chunking)
                // Note: Caching is disabled during chunking to avoid cache pollution
                if (string.IsNullOrWhiteSpace(chunkContext))
                {
                    // Single request - use cache
                    return await _cacheManager.GetOrFetchAsync(
                        method,
                        fullPathWithQuery,
                        body,
                        chunkShape,
                        shapeInfo.CacheCount,
                        async () => await ExecuteSingleRequestAsync(method, fullPathWithQuery, body, chunkShape, fullContext, cancellationToken));
                }
                else
                {
                    // Chunked request - bypass cache
                    return await ExecuteSingleRequestAsync(method, fullPathWithQuery, body, chunkShape, fullContext, cancellationToken);
                }
            },
            modifyShapeForChunk: (originalShape, itemCount) => ModifyShapeForChunk(originalShape, itemCount),
            cancellationToken: cancellationToken);

        // Store in context if context name was provided
        if (!string.IsNullOrWhiteSpace(contextName))
        {
            _contextManager.AddToContext(contextName, method, fullPathWithQuery, body, content);
        }

        // Optionally include schema in header
        TryAddSchemaHeader(context, request, shapeInfo.Shape);

        return content;
    }

    /// <summary>
    /// Executes a single LLM request (used by both cached and chunked requests)
    /// </summary>
    private async Task<string> ExecuteSingleRequestAsync(
        string method,
        string fullPathWithQuery,
        string? body,
        string? shape,
        string? contextHistory,
        CancellationToken cancellationToken)
    {
        var shapeInfo = new ShapeInfo { Shape = shape };
        var prompt = _promptBuilder.BuildPrompt(method, fullPathWithQuery, body, shapeInfo, streaming: false, contextHistory: contextHistory);
        var rawResponse = await _llmClient.GetCompletionAsync(prompt, cancellationToken);
        // Extract clean JSON from LLM response (might include markdown or explanatory text)
        return JsonExtractor.ExtractJson(rawResponse);
    }

    /// <summary>
    /// Modifies shape JSON to update count for a specific chunk
    /// </summary>
    private string? ModifyShapeForChunk(string? originalShape, int itemCount)
    {
        if (string.IsNullOrWhiteSpace(originalShape))
            return originalShape;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(originalShape);
            var root = doc.RootElement;

            // Clone the shape and update count properties
            var modified = CloneAndUpdateCount(root, itemCount);
            return System.Text.Json.JsonSerializer.Serialize(modified, new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
        }
        catch (System.Text.Json.JsonException)
        {
            // If we can't parse/modify, return original
            return originalShape;
        }
    }

    private System.Text.Json.JsonElement CloneAndUpdateCount(System.Text.Json.JsonElement element, int newCount)
    {
        using var stream = new System.IO.MemoryStream();
        using (var writer = new System.Text.Json.Utf8JsonWriter(stream))
        {
            WriteElementWithUpdatedCount(writer, element, newCount);
        }
        stream.Position = 0;
        using var doc = System.Text.Json.JsonDocument.Parse(stream);
        return doc.RootElement.Clone();
    }

    private void WriteElementWithUpdatedCount(System.Text.Json.Utf8JsonWriter writer, System.Text.Json.JsonElement element, int newCount)
    {
        switch (element.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var prop in element.EnumerateObject())
                {
                    writer.WritePropertyName(prop.Name);

                    // Update count-related properties
                    var lowerName = prop.Name.ToLowerInvariant();
                    if ((lowerName == "count" || lowerName == "limit" || lowerName == "size" || lowerName == "length") &&
                        prop.Value.ValueKind == System.Text.Json.JsonValueKind.Number)
                    {
                        writer.WriteNumberValue(newCount);
                    }
                    else
                    {
                        WriteElementWithUpdatedCount(writer, prop.Value, newCount);
                    }
                }
                writer.WriteEndObject();
                break;

            case System.Text.Json.JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteElementWithUpdatedCount(writer, item, newCount);
                }
                writer.WriteEndArray();
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }

    private void TryAddSchemaHeader(HttpContext context, HttpRequest request, string? shape)
    {
        try
        {
            if (!ShouldIncludeSchema(request)) return;
            if (string.IsNullOrWhiteSpace(shape)) return;

            // Only add header if shape is within limit
            if (shape.Length <= MaxSchemaHeaderLength)
            {
                context.Response.Headers["X-Response-Schema"] = shape;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add X-Response-Schema header");
            // Swallow errors to avoid impacting response
        }
    }

    private bool ShouldIncludeSchema(HttpRequest request)
    {
        if (request.Query.TryGetValue("includeSchema", out var includeParam) && includeParam.Count > 0)
        {
            var val = includeParam[0];
            return string.Equals(val, "true", StringComparison.OrdinalIgnoreCase) || val == "1";
        }
        return _options.IncludeShapeInResponse;
    }

    /// <summary>
    /// Extracts n-completions parameter from query string
    /// </summary>
    private int GetNCompletionsParameter(HttpRequest request)
    {
        if (request.Query.TryGetValue("n", out var nParam) && nParam.Count > 0)
        {
            if (int.TryParse(nParam[0], out var n) && n > 0)
            {
                return n;
            }
        }
        return 1;
    }

    /// <summary>
    /// Extracts rate limit delay parameter from query string or header
    /// </summary>
    private string? GetRateLimitDelayParameter(HttpRequest request)
    {
        // Check query parameter first
        if (request.Query.TryGetValue("rateLimit", out var queryParam) && queryParam.Count > 0)
        {
            return queryParam[0];
        }

        // Check header
        if (request.Headers.TryGetValue("X-Rate-Limit-Delay", out var headerParam))
        {
            return headerParam.FirstOrDefault();
        }

        // Fall back to config
        return _options.RateLimitDelayRange;
    }

    /// <summary>
    /// Extracts rate limit strategy parameter from query string or header
    /// </summary>
    private RateLimitStrategy GetRateLimitStrategyParameter(HttpRequest request)
    {
        // Check query parameter first
        if (request.Query.TryGetValue("strategy", out var queryParam) && queryParam.Count > 0)
        {
            if (Enum.TryParse<RateLimitStrategy>(queryParam[0], true, out var strategy))
            {
                return strategy;
            }
        }

        // Check header
        if (request.Headers.TryGetValue("X-Rate-Limit-Strategy", out var headerParam))
        {
            var headerValue = headerParam.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(headerValue) &&
                Enum.TryParse<RateLimitStrategy>(headerValue, true, out var strategy))
            {
                return strategy;
            }
        }

        // Fall back to config
        return _options.RateLimitStrategy;
    }

    /// <summary>
    /// Handles a batched request with n-completions and rate limiting
    /// </summary>
    private async Task<string> HandleBatchedRequestAsync(
        string method,
        string fullPathWithQuery,
        string? body,
        HttpRequest request,
        HttpContext context,
        int nCompletions,
        string? rateLimitDelay,
        RateLimitStrategy strategy,
        Stopwatch overallStopwatch,
        CancellationToken cancellationToken)
    {
        // Extract shape info for prompt building
        var shapeInfo = _shapeExtractor.ExtractShapeInfo(request, body);

        // Build prompt
        var prompt = _promptBuilder.BuildPrompt(method, fullPathWithQuery, body, shapeInfo, streaming: false);

        // Execute batched completions
        var result = await _batchingCoordinator.GetBatchedCompletionsAsync(
            prompt,
            nCompletions,
            strategy,
            rateLimitDelay,
            request.Path,
            request,
            cancellationToken);

        overallStopwatch.Stop();

        // Add rate limiting headers
        AddRateLimitHeaders(context, request.Path, result);

        // Return JSON array of completions
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            completions = result.Completions.Select(c => new
            {
                index = c.Index,
                content = System.Text.Json.JsonSerializer.Deserialize<object>(c.Content),
                timing = new
                {
                    requestTimeMs = c.RequestTimeMs,
                    delayAppliedMs = c.DelayAppliedMs
                }
            }),
            meta = new
            {
                strategy = result.Strategy.ToString(),
                totalRequestTimeMs = result.TotalRequestTimeMs,
                totalDelayMs = result.TotalDelayMs,
                totalElapsedMs = result.TotalElapsedMs,
                averageRequestTimeMs = result.AverageRequestTimeMs
            }
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Adds rate limiting headers to the response
    /// </summary>
    private void AddRateLimitHeaders(HttpContext context, string endpointPath, BatchCompletionResult? batchResult = null)
    {
        try
        {
            if (!_options.EnableRateLimitStatistics && batchResult == null)
                return;

            var headers = _rateLimitService.CalculateRateLimitHeaders(
                endpointPath,
                batchResult?.AverageRequestTimeMs);

            context.Response.Headers["X-RateLimit-Limit"] = headers.Limit.ToString();
            context.Response.Headers["X-RateLimit-Remaining"] = headers.Remaining.ToString();
            context.Response.Headers["X-RateLimit-Reset"] = headers.Reset.ToString();

            if (batchResult != null)
            {
                context.Response.Headers["X-LLMApi-Request-Time"] = batchResult.AverageRequestTimeMs.ToString();
                context.Response.Headers["X-LLMApi-Total-Elapsed"] = batchResult.TotalElapsedMs.ToString();

                if (batchResult.TotalDelayMs > 0)
                {
                    context.Response.Headers["X-LLMApi-Delay-Applied"] = batchResult.TotalDelayMs.ToString();
                }
            }

            // Add endpoint average if available
            var stats = _rateLimitService.GetEndpointStats(endpointPath);
            if (stats != null)
            {
                context.Response.Headers["X-LLMApi-Avg-Time"] = stats.AverageResponseTimeMs.ToString();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add rate limit headers");
            // Swallow errors to avoid impacting response
        }
    }
}
