using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using mostlylucid.mockllmapi.Models;
using mostlylucid.mockllmapi.Services;

namespace mostlylucid.mockllmapi.RequestHandlers;

/// <summary>
/// Handles non-streaming mock API requests with automatic chunking support
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
        _logger = logger;
    }

    /// <summary>
    /// Handles a regular (non-streaming) request with automatic chunking support
    /// </summary>
    public async Task<string> HandleRequestAsync(
        string method,
        string fullPathWithQuery,
        string? body,
        HttpRequest request,
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
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
}
