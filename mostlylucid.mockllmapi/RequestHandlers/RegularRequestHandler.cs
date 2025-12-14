using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using mostlylucid.mockllmapi.Models;
using mostlylucid.mockllmapi.Services;
using mostlylucid.mockllmapi.Services.Tools;

namespace mostlylucid.mockllmapi.RequestHandlers;

/// <summary>
///     Handles non-streaming mock API requests with automatic chunking, rate limiting, and tool execution support
/// </summary>
[SuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "Mock API library uses dynamic JSON serialization by design")]
public class RegularRequestHandler
{
    private const int MaxSchemaHeaderLength = 4000;
    private readonly AutoShapeManager _autoShapeManager;
    private readonly BatchingCoordinator _batchingCoordinator;
    private readonly CacheManager _cacheManager;
    private readonly ChunkingCoordinator _chunkingCoordinator;
    private readonly ContextExtractor _contextExtractor;
    private readonly OpenApiContextManager _contextManager;
    private readonly DelayHelper _delayHelper;
    private readonly JourneyExtractor _journeyExtractor;
    private readonly JourneyPromptInfluencer _journeyPromptInfluencer;
    private readonly JourneySessionManager _journeySessionManager;
    private readonly LlmClient _llmClient;
    private readonly ILogger<RegularRequestHandler> _logger;
    private readonly LLMockApiOptions _options;
    private readonly PromptBuilder _promptBuilder;
    private readonly RateLimitService _rateLimitService;
    private readonly ShapeExtractor _shapeExtractor;
    private readonly ToolOrchestrator _toolOrchestrator;

    public RegularRequestHandler(
        IOptions<LLMockApiOptions> options,
        ShapeExtractor shapeExtractor,
        ContextExtractor contextExtractor,
        JourneyExtractor journeyExtractor,
        OpenApiContextManager contextManager,
        JourneySessionManager journeySessionManager,
        JourneyPromptInfluencer journeyPromptInfluencer,
        PromptBuilder promptBuilder,
        LlmClient llmClient,
        CacheManager cacheManager,
        DelayHelper delayHelper,
        ChunkingCoordinator chunkingCoordinator,
        RateLimitService rateLimitService,
        BatchingCoordinator batchingCoordinator,
        ToolOrchestrator toolOrchestrator,
        AutoShapeManager autoShapeManager,
        ILogger<RegularRequestHandler> logger)
    {
        _options = options.Value;
        _shapeExtractor = shapeExtractor;
        _contextExtractor = contextExtractor;
        _journeyExtractor = journeyExtractor;
        _contextManager = contextManager;
        _journeySessionManager = journeySessionManager;
        _journeyPromptInfluencer = journeyPromptInfluencer;
        _promptBuilder = promptBuilder;
        _llmClient = llmClient;
        _cacheManager = cacheManager;
        _delayHelper = delayHelper;
        _chunkingCoordinator = chunkingCoordinator;
        _rateLimitService = rateLimitService;
        _batchingCoordinator = batchingCoordinator;
        _toolOrchestrator = toolOrchestrator;
        _autoShapeManager = autoShapeManager;
        _logger = logger;
    }

    /// <summary>
    ///     Handles a regular (non-streaming) request with automatic chunking and rate limiting support
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

        // Apply autoshape if no explicit shape is provided
        if (string.IsNullOrWhiteSpace(shapeInfo.Shape))
        {
            var autoShape = _autoShapeManager.GetShapeForRequest(request, shapeInfo);
            if (!string.IsNullOrWhiteSpace(autoShape))
                shapeInfo = new ShapeInfo
                {
                    Shape = autoShape,
                    CacheCount = shapeInfo.CacheCount,
                    IsJsonSchema = shapeInfo.IsJsonSchema,
                    ErrorConfig = shapeInfo.ErrorConfig
                };
        }

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
            return await HandleBatchedRequestAsync(
                method, fullPathWithQuery, body, request, context,
                nCompletions, rateLimitDelay, rateLimitStrategy,
                overallStopwatch, cancellationToken);

        // Extract context name
        var contextName = _contextExtractor.ExtractContextName(request, body);

        // Get context history if context is specified
        var contextHistory = !string.IsNullOrWhiteSpace(contextName)
            ? _contextManager.GetContextForPrompt(contextName)
            : null;

        // Extract journey parameters
        var journeyName = _journeyExtractor.ExtractJourneyName(request, body);
        var journeyIdFromRequest = _journeyExtractor.ExtractJourneyId(request, body);
        var journeyRandom = _journeyExtractor.ExtractJourneyRandom(request);
        var journeyModalityStr = _journeyExtractor.ExtractJourneyModality(request);
        JourneyModality? journeyModality = null;
        if (!string.IsNullOrWhiteSpace(journeyModalityStr) &&
            Enum.TryParse<JourneyModality>(journeyModalityStr, true, out var parsedModality))
            journeyModality = parsedModality;

        // Get journey instance if applicable
        // Multiple journeys can run concurrently using different journeyIds
        JourneyInstance? journeyInstance = null;
        string? journeyInfluenceText = null;
        string? journeyId = null;

        if (!string.IsNullOrWhiteSpace(journeyName) || !string.IsNullOrWhiteSpace(journeyIdFromRequest) ||
            journeyRandom)
        {
            // Determine journey ID:
            // 1. Use explicit journeyId from request if provided
            // 2. Generate new ID if starting a new journey
            // 3. Try to restore from context if journeyId matches stored journey
            var contextSharedData = !string.IsNullOrWhiteSpace(contextName)
                ? _contextManager.GetSharedData(contextName)
                : null;

            journeyId = journeyIdFromRequest;

            // If no explicit ID but we have a journey name, check if there's an existing journey for this context
            if (string.IsNullOrWhiteSpace(journeyId) && contextSharedData != null)
                // Try to find existing journey ID in context
                contextSharedData.TryGetValue("journey.id", out journeyId);

            // If starting a new journey (name specified but no existing ID), generate new ID
            if (!string.IsNullOrWhiteSpace(journeyName) && string.IsNullOrWhiteSpace(journeyId))
                journeyId = JourneyExtractor.GenerateJourneyId();
            // If random journey requested and no ID, generate new ID
            else if (journeyRandom && string.IsNullOrWhiteSpace(journeyId))
                journeyId = JourneyExtractor.GenerateJourneyId();

            if (!string.IsNullOrWhiteSpace(journeyId))
                journeyInstance = _journeySessionManager.GetOrCreateJourney(
                    journeyId,
                    journeyName,
                    journeyRandom,
                    journeyModality,
                    contextSharedData);

            if (journeyInstance != null)
            {
                // Try to resolve step for this request
                var pathOnly = fullPathWithQuery.Split('?')[0];
                var matchingStep = _journeySessionManager.ResolveStepForRequest(journeyInstance, method, pathOnly);

                if (matchingStep != null)
                {
                    // Build prompt influence from journey
                    var contextSnapshot = JourneyPromptInfluencer.BuildContextSnapshot(
                        contextSharedData,
                        matchingStep.PromptHints?.PromoteKeys,
                        matchingStep.PromptHints?.DemoteKeys);

                    var fallbackSeed = JourneyPromptInfluencer.GenerateRandomnessSeed(
                        journeyInstance.SessionId, method, pathOnly, journeyInstance.CurrentStepIndex);

                    var influence = _journeyPromptInfluencer.BuildJourneyPromptInfluence(
                        journeyInstance, matchingStep, contextSnapshot, fallbackSeed);

                    journeyInfluenceText = JourneyPromptInfluencer.FormatInfluenceForPrompt(influence);

                    // Use step's shape if provided and no shape specified in request
                    if (string.IsNullOrWhiteSpace(shapeInfo.Shape) &&
                        !string.IsNullOrWhiteSpace(matchingStep.ShapeJson)) shapeInfo.Shape = matchingStep.ShapeJson;

                    _logger.LogDebug("Journey '{Journey}' (ID: {JourneyId}) step {Step} matched for {Method} {Path}",
                        journeyInstance.Template.Name, journeyInstance.SessionId, journeyInstance.CurrentStepIndex,
                        method, pathOnly);
                }

                // Store journey state in context for persistence
                if (!string.IsNullOrWhiteSpace(contextName))
                {
                    var journeyState = _journeySessionManager.GetJourneyStateForContext(journeyInstance);
                    _contextManager.AddJourneyState(contextName, journeyState);
                }

                // Add journey info to response headers (including unique ID for tracking)
                context.Response.Headers["X-Journey-Id"] = journeyInstance.SessionId;
                context.Response.Headers["X-Journey-Name"] = journeyInstance.Template.Name;
                context.Response.Headers["X-Journey-Step"] = journeyInstance.CurrentStepIndex.ToString();
                context.Response.Headers["X-Journey-TotalSteps"] = journeyInstance.ResolvedSteps.Count.ToString();
                context.Response.Headers["X-Journey-Complete"] =
                    journeyInstance.IsComplete.ToString().ToLowerInvariant();
            }
        }

        // Combine journey influence with context history
        if (!string.IsNullOrWhiteSpace(journeyInfluenceText))
            contextHistory = string.IsNullOrWhiteSpace(contextHistory)
                ? $"Journey Guidance:\n{journeyInfluenceText}"
                : $"{contextHistory}\n\nJourney Guidance:\n{journeyInfluenceText}";

        // Execute tools if requested (Phase 1: Explicit mode)
        List<ToolResult>? toolResults = null;
        var requestId = Guid.NewGuid().ToString();

        if (_options.ToolExecutionMode == ToolExecutionMode.Explicit)
        {
            var requestedTools = GetRequestedTools(request);
            if (requestedTools.Count > 0)
            {
                _logger.LogInformation("Executing {Count} tools for request: {Tools}",
                    requestedTools.Count, string.Join(", ", requestedTools));

                var toolParams = ExtractToolParameters(request, body);
                toolResults = await _toolOrchestrator.ExecuteToolsAsync(
                    requestedTools, toolParams, requestId, cancellationToken);

                // Merge tool results into context
                if (toolResults.Count > 0)
                {
                    var toolContext = _toolOrchestrator.FormatToolResultsForContext(toolResults);
                    contextHistory = string.IsNullOrWhiteSpace(contextHistory)
                        ? toolContext
                        : contextHistory + "\n\n" + toolContext;
                }
            }
        }

        // Execute with automatic chunking if needed
        var content = await _chunkingCoordinator.ExecuteWithChunkingAsync(
            request,
            shapeInfo.Shape,
            async (chunkShape, chunkContext) =>
            {
                // Combine context history with chunk context
                var fullContext = contextHistory;
                if (!string.IsNullOrWhiteSpace(chunkContext))
                    fullContext = string.IsNullOrWhiteSpace(fullContext)
                        ? chunkContext
                        : fullContext + "\n" + chunkContext;

                // Get response (with caching if requested and not chunking)
                // Note: Caching is disabled during chunking to avoid cache pollution
                if (string.IsNullOrWhiteSpace(chunkContext))
                    // Single request - use cache
                    return await _cacheManager.GetOrFetchAsync(
                        method,
                        fullPathWithQuery,
                        body,
                        chunkShape,
                        shapeInfo.CacheCount,
                        async () => await ExecuteSingleRequestAsync(method, fullPathWithQuery, body, chunkShape,
                            fullContext, cancellationToken));

                // Chunked request - bypass cache
                return await ExecuteSingleRequestAsync(method, fullPathWithQuery, body, chunkShape, fullContext,
                    cancellationToken);
            },
            (originalShape, itemCount) => ModifyShapeForChunk(originalShape, itemCount),
            cancellationToken);

        // Store in context if context name was provided
        if (!string.IsNullOrWhiteSpace(contextName))
            _contextManager.AddToContext(contextName, method, fullPathWithQuery, body, content);

        // Advance journey if a step was matched
        if (journeyInstance != null && !journeyInstance.IsComplete)
        {
            var pathOnly = fullPathWithQuery.Split('?')[0];
            var matchingStep = _journeySessionManager.ResolveStepForRequest(journeyInstance, method, pathOnly);
            if (matchingStep != null)
            {
                var advanced = _journeySessionManager.AdvanceJourney(journeyInstance.SessionId);
                if (advanced != null && !string.IsNullOrWhiteSpace(contextName))
                {
                    // Update journey state in context after advancing
                    var updatedState = _journeySessionManager.GetJourneyStateForContext(advanced);
                    _contextManager.AddJourneyState(contextName, updatedState);

                    // Update response headers with new step
                    context.Response.Headers["X-Journey-Step"] = advanced.CurrentStepIndex.ToString();
                    context.Response.Headers["X-Journey-Complete"] = advanced.IsComplete.ToString().ToLowerInvariant();
                }
            }
        }

        // Optionally include schema in header
        TryAddSchemaHeader(context, request, shapeInfo.Shape);

        // Optionally include tool results in response
        if (_options.IncludeToolResultsInResponse && toolResults != null && toolResults.Count > 0)
        {
            var contentJson = JsonDocument.Parse(content);
            var wrappedResponse = new
            {
                data = contentJson.RootElement,
                toolResults = toolResults.Select(r => new
                {
                    toolName = r.ToolName,
                    success = r.Success,
                    data = r.Data != null ? JsonDocument.Parse(r.Data).RootElement : (object?)null,
                    error = r.Error,
                    executionTimeMs = r.ExecutionTimeMs,
                    metadata = r.Metadata
                })
            };
            var wrappedContent =
                JsonSerializer.Serialize(wrappedResponse, new JsonSerializerOptions { WriteIndented = true });

            // Store shape from response if autoshape is enabled
            _autoShapeManager.StoreShapeFromResponse(request, wrappedContent);

            return wrappedContent;
        }

        // Store shape from response if autoshape is enabled
        _autoShapeManager.StoreShapeFromResponse(request, content);

        return content;
    }

    /// <summary>
    ///     Executes a single LLM request (used by both cached and chunked requests)
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
        var prompt = _promptBuilder.BuildPrompt(method, fullPathWithQuery, body, shapeInfo, false,
            contextHistory: contextHistory);
        var rawResponse = await _llmClient.GetCompletionAsync(prompt, cancellationToken);
        // Extract clean JSON from LLM response (might include markdown or explanatory text)
        return JsonExtractor.ExtractJson(rawResponse);
    }

    /// <summary>
    ///     Modifies shape JSON to update count for a specific chunk
    /// </summary>
    private string? ModifyShapeForChunk(string? originalShape, int itemCount)
    {
        if (string.IsNullOrWhiteSpace(originalShape))
            return originalShape;

        try
        {
            using var doc = JsonDocument.Parse(originalShape);
            var root = doc.RootElement;

            // Clone the shape and update count properties
            var modified = CloneAndUpdateCount(root, itemCount);
            return JsonSerializer.Serialize(modified, new JsonSerializerOptions { WriteIndented = false });
        }
        catch (JsonException)
        {
            // If we can't parse/modify, return original
            return originalShape;
        }
    }

    private JsonElement CloneAndUpdateCount(JsonElement element, int newCount)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteElementWithUpdatedCount(writer, element, newCount);
        }

        stream.Position = 0;
        using var doc = JsonDocument.Parse(stream);
        return doc.RootElement.Clone();
    }

    private void WriteElementWithUpdatedCount(Utf8JsonWriter writer, JsonElement element, int newCount)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var prop in element.EnumerateObject())
                {
                    writer.WritePropertyName(prop.Name);

                    // Update count-related properties
                    var lowerName = prop.Name.ToLowerInvariant();
                    if ((lowerName == "count" || lowerName == "limit" || lowerName == "size" ||
                         lowerName == "length") &&
                        prop.Value.ValueKind == JsonValueKind.Number)
                        writer.WriteNumberValue(newCount);
                    else
                        WriteElementWithUpdatedCount(writer, prop.Value, newCount);
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray()) WriteElementWithUpdatedCount(writer, item, newCount);
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
            if (shape.Length <= MaxSchemaHeaderLength) context.Response.Headers["X-Response-Schema"] = shape;
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
    ///     Extracts n-completions parameter from query string
    /// </summary>
    private int GetNCompletionsParameter(HttpRequest request)
    {
        if (request.Query.TryGetValue("n", out var nParam) && nParam.Count > 0)
            if (int.TryParse(nParam[0], out var n) && n > 0)
                return n;

        return 1;
    }

    /// <summary>
    ///     Extracts rate limit delay parameter from query string or header
    /// </summary>
    private string? GetRateLimitDelayParameter(HttpRequest request)
    {
        // Check query parameter first
        if (request.Query.TryGetValue("rateLimit", out var queryParam) && queryParam.Count > 0) return queryParam[0];

        // Check header
        if (request.Headers.TryGetValue("X-Rate-Limit-Delay", out var headerParam)) return headerParam.FirstOrDefault();

        // Fall back to config
        return _options.RateLimitDelayRange;
    }

    /// <summary>
    ///     Extracts rate limit strategy parameter from query string or header
    /// </summary>
    private RateLimitStrategy GetRateLimitStrategyParameter(HttpRequest request)
    {
        // Check query parameter first
        if (request.Query.TryGetValue("strategy", out var queryParam) && queryParam.Count > 0)
            if (Enum.TryParse<RateLimitStrategy>(queryParam[0], true, out var strategy))
                return strategy;

        // Check header
        if (request.Headers.TryGetValue("X-Rate-Limit-Strategy", out var headerParam))
        {
            var headerValue = headerParam.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(headerValue) &&
                Enum.TryParse<RateLimitStrategy>(headerValue, true, out var strategy))
                return strategy;
        }

        // Fall back to config
        return _options.RateLimitStrategy;
    }

    /// <summary>
    ///     Handles a batched request with n-completions and rate limiting
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
        var prompt = _promptBuilder.BuildPrompt(method, fullPathWithQuery, body, shapeInfo, false);

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
        return JsonSerializer.Serialize(new
        {
            completions = result.Completions.Select(c => new
            {
                index = c.Index,
                content = JsonSerializer.Deserialize<object>(c.Content),
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
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    ///     Adds rate limiting headers to the response
    /// </summary>
    private void AddRateLimitHeaders(HttpContext context, string endpointPath,
        BatchCompletionResult? batchResult = null)
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
                    context.Response.Headers["X-LLMApi-Delay-Applied"] = batchResult.TotalDelayMs.ToString();
            }

            // Add endpoint average if available
            var stats = _rateLimitService.GetEndpointStats(endpointPath);
            if (stats != null) context.Response.Headers["X-LLMApi-Avg-Time"] = stats.AverageResponseTimeMs.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add rate limit headers");
            // Swallow errors to avoid impacting response
        }
    }

    /// <summary>
    ///     Get list of requested tools from query parameters or headers
    ///     Supports: ?useTool=tool1,tool2 or X-Use-Tool: tool1,tool2
    /// </summary>
    private List<string> GetRequestedTools(HttpRequest request)
    {
        var tools = new List<string>();

        // Check query parameter first
        if (request.Query.TryGetValue("useTool", out var queryParam) && queryParam.Count > 0)
        {
            var toolNames = queryParam[0]
                ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (toolNames != null) tools.AddRange(toolNames);
        }

        // Check header
        if (request.Headers.TryGetValue("X-Use-Tool", out var headerParam))
        {
            var headerValue = headerParam.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(headerValue))
            {
                var toolNames = headerValue.Split(',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                tools.AddRange(toolNames);
            }
        }

        return tools.Distinct().ToList();
    }

    /// <summary>
    ///     Extract tool parameters from query string and request body
    /// </summary>
    private Dictionary<string, object> ExtractToolParameters(HttpRequest request, string? body)
    {
        var parameters = new Dictionary<string, object>();

        // Add all query parameters
        foreach (var (key, value) in request.Query)
            if (key != "useTool" && value.Count > 0)
                parameters[key] = value[0] ?? string.Empty;

        // Parse body as JSON and add fields as parameters
        if (!string.IsNullOrWhiteSpace(body))
            try
            {
                var jsonDoc = JsonDocument.Parse(body);
                foreach (var property in jsonDoc.RootElement.EnumerateObject())
                    parameters[property.Name] = property.Value.ValueKind switch
                    {
                        JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                        JsonValueKind.Number => property.Value.GetDouble(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        _ => property.Value.GetRawText()
                    };
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse request body as JSON for tool parameters");
            }

        return parameters;
    }
}