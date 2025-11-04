using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using mostlylucid.mockllmapi.Services;

namespace mostlylucid.mockllmapi.RequestHandlers;

/// <summary>
/// Handles streaming mock API requests with Server-Sent Events
/// </summary>
public class StreamingRequestHandler
{
    private readonly LLMockApiOptions _options;
    private readonly ShapeExtractor _shapeExtractor;
    private readonly PromptBuilder _promptBuilder;
    private readonly LlmClient _llmClient;
    private readonly DelayHelper _delayHelper;
    private readonly ILogger<StreamingRequestHandler> _logger;

    private const int MaxSchemaHeaderLength = 4000;

    public StreamingRequestHandler(
        IOptions<LLMockApiOptions> options,
        ShapeExtractor shapeExtractor,
        PromptBuilder promptBuilder,
        LlmClient llmClient,
        DelayHelper delayHelper,
        ILogger<StreamingRequestHandler> logger)
    {
        _options = options.Value;
        _shapeExtractor = shapeExtractor;
        _promptBuilder = promptBuilder;
        _llmClient = llmClient;
        _delayHelper = delayHelper;
        _logger = logger;
    }

    /// <summary>
    /// Handles a streaming request
    /// </summary>
    public async Task HandleStreamingRequestAsync(
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

        // Build prompt
        var prompt = _promptBuilder.BuildPrompt(method, fullPathWithQuery, body, shapeInfo, streaming: true);

        // Set response headers for SSE
        context.Response.StatusCode = 200;
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";
        context.Response.ContentType = "text/event-stream";

        // Optionally include schema in header before any writes
        TryAddSchemaHeader(context, request, shapeInfo.Shape);

        // Get streaming response from LLM using Microsoft.Extensions.AI
        var accumulated = new StringBuilder();

        await foreach (var chunk in _llmClient.GetStreamingCompletionAsync(prompt, cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested) break;
            if (string.IsNullOrEmpty(chunk)) continue;

            accumulated.Append(chunk);

            // Apply streaming delay if configured
            await _delayHelper.ApplyStreamingDelayAsync(cancellationToken);

            await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(new { chunk, done = false })}\n\n");
            await context.Response.Body.FlushAsync(cancellationToken);
        }

        // Send final event with complete content
        var finalJson = accumulated.ToString();
        object finalPayload;
        if (ShouldIncludeSchema(request) && !string.IsNullOrWhiteSpace(shapeInfo.Shape))
        {
            finalPayload = new { content = finalJson, done = true, schema = shapeInfo.Shape };
        }
        else
        {
            finalPayload = new { content = finalJson, done = true };
        }
        await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(finalPayload)}\n\n");
        await context.Response.Body.FlushAsync(cancellationToken);
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
