using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using mostlylucid.mockllmapi.Models;
using mostlylucid.mockllmapi.Services;

namespace mostlylucid.mockllmapi.RequestHandlers;

/// <summary>
/// Handles streaming mock API requests with Server-Sent Events
/// </summary>
public class StreamingRequestHandler
{
    private readonly LLMockApiOptions _options;
    private readonly ShapeExtractor _shapeExtractor;
    private readonly ContextExtractor _contextExtractor;
    private readonly OpenApiContextManager _contextManager;
    private readonly PromptBuilder _promptBuilder;
    private readonly LlmClient _llmClient;
    private readonly DelayHelper _delayHelper;
    private readonly ChunkingCoordinator _chunkingCoordinator;
    private readonly ILogger<StreamingRequestHandler> _logger;

    private const int MaxSchemaHeaderLength = 4000;

    public StreamingRequestHandler(
        IOptions<LLMockApiOptions> options,
        ShapeExtractor shapeExtractor,
        ContextExtractor contextExtractor,
        OpenApiContextManager contextManager,
        PromptBuilder promptBuilder,
        LlmClient llmClient,
        DelayHelper delayHelper,
        ChunkingCoordinator chunkingCoordinator,
        ILogger<StreamingRequestHandler> logger)
    {
        _options = options.Value;
        _shapeExtractor = shapeExtractor;
        _contextExtractor = contextExtractor;
        _contextManager = contextManager;
        _promptBuilder = promptBuilder;
        _llmClient = llmClient;
        _delayHelper = delayHelper;
        _chunkingCoordinator = chunkingCoordinator;
        _logger = logger;
    }

    /// <summary>
    /// Extracts SSE mode from request query parameter or uses configured default
    /// </summary>
    private SseMode GetSseMode(HttpRequest request)
    {
        if (request.Query.TryGetValue("sseMode", out var modeParam) && !string.IsNullOrWhiteSpace(modeParam))
        {
            if (Enum.TryParse<SseMode>(modeParam, ignoreCase: true, out var parsedMode))
            {
                return parsedMode;
            }
        }
        return _options.SseMode;
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

        // Determine SSE mode
        var sseMode = GetSseMode(request);

        // Check if error simulation is requested
        if (shapeInfo.ErrorConfig != null)
        {
            context.Response.StatusCode = shapeInfo.ErrorConfig.StatusCode;
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";

            _logger.LogDebug("Returning simulated error in SSE stream: {StatusCode} - {Message}",
                shapeInfo.ErrorConfig.StatusCode, shapeInfo.ErrorConfig.GetMessage());

            // Send error as SSE event and close stream
            var errorJson = shapeInfo.ErrorConfig.ToJson();
            await context.Response.WriteAsync($"data: {errorJson}\n\n", cancellationToken);
            await context.Response.Body.FlushAsync(cancellationToken);
            return;
        }

        // Extract context name
        var contextName = _contextExtractor.ExtractContextName(request, body);

        // Get context history if context is specified
        var contextHistory = !string.IsNullOrWhiteSpace(contextName)
            ? _contextManager.GetContextForPrompt(contextName)
            : null;

        // Build prompt
        var prompt = _promptBuilder.BuildPrompt(method, fullPathWithQuery, body, shapeInfo, streaming: true, contextHistory: contextHistory);

        // Set response headers for SSE
        context.Response.StatusCode = 200;
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";
        context.Response.ContentType = "text/event-stream";

        // Optionally include schema in header before any writes
        TryAddSchemaHeader(context, request, shapeInfo.Shape);

        // Route to appropriate streaming mode
        switch (sseMode)
        {
            case SseMode.CompleteObjects:
                await StreamCompleteObjectsAsync(context, request, prompt, shapeInfo, contextName, method, fullPathWithQuery, body, cancellationToken);
                break;
            case SseMode.ArrayItems:
                await StreamArrayItemsAsync(context, request, prompt, shapeInfo, contextName, method, fullPathWithQuery, body, cancellationToken);
                break;
            case SseMode.LlmTokens:
            default:
                await StreamLlmTokensAsync(context, request, prompt, shapeInfo, contextName, method, fullPathWithQuery, body, cancellationToken);
                break;
        }
    }

    /// <summary>
    /// Stream LLM generation token-by-token (original behavior)
    /// </summary>
    private async Task StreamLlmTokensAsync(
        HttpContext context,
        HttpRequest request,
        string prompt,
        ShapeInfo shapeInfo,
        string? contextName,
        string method,
        string fullPathWithQuery,
        string? body,
        CancellationToken cancellationToken)
    {
        // Get streaming response from LLM
        using var httpRes = await _llmClient.GetStreamingCompletionAsync(prompt, cancellationToken);
        await using var stream = await httpRes.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        var accumulated = new StringBuilder();

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync();
            if (line == null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line.Substring(5).Trim();
                if (payload == "[DONE]")
                {
                    var finalJson = accumulated.ToString();

                    // Store in context if context name was provided
                    if (!string.IsNullOrWhiteSpace(contextName))
                    {
                        _contextManager.AddToContext(contextName, method, fullPathWithQuery, body, finalJson);
                    }

                    // Include schema in final event payload if enabled
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
                    await context.Response.Body.FlushAsync();
                    break;
                }

                try
                {
                    using var doc = JsonDocument.Parse(payload);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                    {
                        var choice = choices[0];
                        string? chunk = null;

                        if (choice.TryGetProperty("delta", out var delta) &&
                            delta.TryGetProperty("content", out var deltaContent))
                        {
                            chunk = deltaContent.GetString();
                        }
                        else if (choice.TryGetProperty("message", out var msg) &&
                                 msg.TryGetProperty("content", out var msgContent))
                        {
                            chunk = msgContent.GetString();
                        }

                        if (!string.IsNullOrEmpty(chunk))
                        {
                            accumulated.Append(chunk);

                            // Apply streaming delay if configured
                            await _delayHelper.ApplyStreamingDelayAsync(cancellationToken);

                            // Send the accumulated content so far (partial JSON building up)
                            await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(new {
                                chunk,
                                accumulated = accumulated.ToString(),
                                done = false
                            })}\n\n");
                            await context.Response.Body.FlushAsync();
                        }
                    }
                }
                catch
                {
                    // Skip malformed chunks
                }
            }
        }
    }

    /// <summary>
    /// Stream complete JSON objects as separate SSE events (realistic REST API mode)
    /// </summary>
    private async Task StreamCompleteObjectsAsync(
        HttpContext context,
        HttpRequest request,
        string prompt,
        ShapeInfo shapeInfo,
        string? contextName,
        string method,
        string fullPathWithQuery,
        string? body,
        CancellationToken cancellationToken)
    {
        // Get non-streaming completion to generate full JSON
        var completion = await _llmClient.GetCompletionAsync(prompt, cancellationToken);
        var cleanJson = JsonExtractor.ExtractJson(completion);

        // Store in context if context name was provided
        if (!string.IsNullOrWhiteSpace(contextName))
        {
            _contextManager.AddToContext(contextName, method, fullPathWithQuery, body, cleanJson);
        }

        // Parse JSON to extract objects
        JsonDocument? doc = null;
        try
        {
            doc = JsonDocument.Parse(cleanJson);
            var root = doc.RootElement;

            // Determine what to stream
            JsonElement[] items;

            if (root.ValueKind == JsonValueKind.Array)
            {
                // Direct array: stream each element
                items = root.EnumerateArray().ToArray();
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                // Try to find array property (users, items, results, data, etc.)
                var arrayProp = root.EnumerateObject()
                    .FirstOrDefault(p => p.Value.ValueKind == JsonValueKind.Array);

                if (arrayProp.Value.ValueKind == JsonValueKind.Array)
                {
                    items = arrayProp.Value.EnumerateArray().ToArray();
                }
                else
                {
                    // Single object: stream as one event
                    items = new[] { root };
                }
            }
            else
            {
                // Primitive value: wrap and stream
                items = new[] { root };
            }

            // Stream each complete object as a separate SSE event
            for (int i = 0; i < items.Length; i++)
            {
                if (cancellationToken.IsCancellationRequested) break;

                // Apply streaming delay between objects
                if (i > 0)
                {
                    await _delayHelper.ApplyStreamingDelayAsync(cancellationToken);
                }

                var itemJson = JsonSerializer.Serialize(items[i]);
                var eventData = new
                {
                    data = items[i],
                    index = i,
                    total = items.Length,
                    done = i == items.Length - 1
                };

                await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(eventData)}\n\n");
                await context.Response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse JSON for CompleteObjects streaming mode");
            // Send error event
            var errorData = new { error = "Failed to parse generated JSON", done = true };
            await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(errorData)}\n\n");
            await context.Response.Body.FlushAsync(cancellationToken);
        }
        finally
        {
            doc?.Dispose();
        }
    }

    /// <summary>
    /// Stream array items individually with metadata (paginated results mode)
    /// </summary>
    private async Task StreamArrayItemsAsync(
        HttpContext context,
        HttpRequest request,
        string prompt,
        ShapeInfo shapeInfo,
        string? contextName,
        string method,
        string fullPathWithQuery,
        string? body,
        CancellationToken cancellationToken)
    {
        // Get non-streaming completion to generate full JSON
        var completion = await _llmClient.GetCompletionAsync(prompt, cancellationToken);
        var cleanJson = JsonExtractor.ExtractJson(completion);

        // Store in context if context name was provided
        if (!string.IsNullOrWhiteSpace(contextName))
        {
            _contextManager.AddToContext(contextName, method, fullPathWithQuery, body, cleanJson);
        }

        // Parse JSON to extract array items
        JsonDocument? doc = null;
        try
        {
            doc = JsonDocument.Parse(cleanJson);
            var root = doc.RootElement;

            // Extract array items
            JsonElement[] items;
            string? arrayName = null;

            if (root.ValueKind == JsonValueKind.Array)
            {
                items = root.EnumerateArray().ToArray();
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                // Find first array property
                var arrayProp = root.EnumerateObject()
                    .FirstOrDefault(p => p.Value.ValueKind == JsonValueKind.Array);

                if (arrayProp.Value.ValueKind == JsonValueKind.Array)
                {
                    arrayName = arrayProp.Name;
                    items = arrayProp.Value.EnumerateArray().ToArray();
                }
                else
                {
                    // No array found, treat as single item
                    items = new[] { root };
                }
            }
            else
            {
                items = new[] { root };
            }

            // Stream each item with rich metadata
            for (int i = 0; i < items.Length; i++)
            {
                if (cancellationToken.IsCancellationRequested) break;

                // Apply streaming delay between items
                if (i > 0)
                {
                    await _delayHelper.ApplyStreamingDelayAsync(cancellationToken);
                }

                var eventData = new
                {
                    item = items[i],
                    index = i,
                    total = items.Length,
                    arrayName,
                    hasMore = i < items.Length - 1,
                    done = i == items.Length - 1
                };

                await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(eventData)}\n\n");
                await context.Response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse JSON for ArrayItems streaming mode");
            // Send error event
            var errorData = new { error = "Failed to parse generated JSON", done = true };
            await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(errorData)}\n\n");
            await context.Response.Body.FlushAsync(cancellationToken);
        }
        finally
        {
            doc?.Dispose();
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
