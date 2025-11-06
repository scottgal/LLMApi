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
    /// Checks if continuous streaming is enabled (global config or per-request)
    /// </summary>
    private bool IsContinuousMode(HttpRequest request)
    {
        // Check query parameter first
        if (request.Query.TryGetValue("continuous", out var continuousParam))
        {
            if (bool.TryParse(continuousParam, out var isContinuous))
            {
                return isContinuous;
            }
        }

        // Check header
        if (request.Headers.TryGetValue("X-Continuous-Streaming", out var headerValue))
        {
            if (bool.TryParse(headerValue, out var isContinuous))
            {
                return isContinuous;
            }
        }

        // Check shape JSON for $continuous property
        if (request.Query.TryGetValue("shape", out var shapeParam))
        {
            try
            {
                var shapeDoc = JsonDocument.Parse(shapeParam.ToString());
                if (shapeDoc.RootElement.TryGetProperty("$continuous", out var continuousProp))
                {
                    if (continuousProp.ValueKind == JsonValueKind.True)
                    {
                        return true;
                    }
                }
            }
            catch { /* Invalid JSON, ignore */ }
        }

        return _options.EnableContinuousStreaming;
    }

    /// <summary>
    /// Gets the interval (in milliseconds) between continuous streaming events
    /// </summary>
    private int GetContinuousInterval(HttpRequest request)
    {
        if (request.Query.TryGetValue("interval", out var intervalParam))
        {
            if (int.TryParse(intervalParam, out var interval) && interval > 0)
            {
                return interval;
            }
        }
        return _options.ContinuousStreamingIntervalMs;
    }

    /// <summary>
    /// Gets the maximum duration (in seconds) for continuous streaming
    /// </summary>
    private int GetMaxDuration(HttpRequest request)
    {
        if (request.Query.TryGetValue("maxDuration", out var durationParam))
        {
            if (int.TryParse(durationParam, out var duration) && duration >= 0)
            {
                return duration;
            }
        }
        return _options.ContinuousStreamingMaxDurationSeconds;
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

        // Check if continuous streaming is enabled
        var isContinuous = IsContinuousMode(request);

        if (isContinuous)
        {
            // Continuous streaming mode - keeps connection open and generates new data periodically
            await HandleContinuousStreamingAsync(context, request, sseMode, prompt, shapeInfo, contextName, method, fullPathWithQuery, body, cancellationToken);
        }
        else
        {
            // Single-shot streaming mode - generate once and close
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

    /// <summary>
    /// Handles continuous SSE streaming - keeps connection open and periodically generates new data
    /// Similar to SignalR's continuous data generation but using SSE
    /// </summary>
    private async Task HandleContinuousStreamingAsync(
        HttpContext context,
        HttpRequest request,
        SseMode sseMode,
        string basePrompt,
        ShapeInfo shapeInfo,
        string? contextName,
        string method,
        string fullPathWithQuery,
        string? body,
        CancellationToken cancellationToken)
    {
        var interval = GetContinuousInterval(request);
        var maxDurationSeconds = GetMaxDuration(request);
        var startTime = DateTime.UtcNow;
        var eventCount = 0;

        _logger.LogInformation("Starting continuous SSE streaming - Mode: {Mode}, Interval: {Interval}ms, MaxDuration: {Duration}s",
            sseMode, interval, maxDurationSeconds);

        try
        {
            // Send initial heartbeat/info event
            var infoEvent = new
            {
                type = "info",
                message = "Continuous streaming started",
                mode = sseMode.ToString(),
                intervalMs = interval,
                maxDurationSeconds = maxDurationSeconds
            };
            await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(infoEvent)}\n\n");
            await context.Response.Body.FlushAsync(cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                // Check if max duration exceeded
                if (maxDurationSeconds > 0)
                {
                    var elapsed = (DateTime.UtcNow - startTime).TotalSeconds;
                    if (elapsed >= maxDurationSeconds)
                    {
                        _logger.LogInformation("Continuous streaming max duration reached: {Elapsed}s", elapsed);
                        var endEvent = new { type = "end", message = "Max duration reached", eventCount, done = true };
                        await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(endEvent)}\n\n");
                        await context.Response.Body.FlushAsync(cancellationToken);
                        break;
                    }
                }

                // Generate new data using appropriate mode
                try
                {
                    // Add timestamp and event count to prompt for variation
                    var continuousPrompt = $"{basePrompt}\n\nIMPORTANT: Generate DIFFERENT data than previous events. Event #{eventCount + 1} at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}. Vary values to simulate real-time changes.";

                    switch (sseMode)
                    {
                        case SseMode.CompleteObjects:
                            await GenerateContinuousCompleteObject(context, request, continuousPrompt, contextName, method, fullPathWithQuery, body, eventCount, cancellationToken);
                            break;
                        case SseMode.ArrayItems:
                            await GenerateContinuousArrayItems(context, request, continuousPrompt, contextName, method, fullPathWithQuery, body, eventCount, cancellationToken);
                            break;
                        case SseMode.LlmTokens:
                        default:
                            await GenerateContinuousLlmTokens(context, request, continuousPrompt, contextName, method, fullPathWithQuery, body, eventCount, cancellationToken);
                            break;
                    }

                    eventCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error generating continuous SSE event #{EventCount}", eventCount);
                    var errorEvent = new { type = "error", message = ex.Message, eventCount };
                    await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(errorEvent)}\n\n");
                    await context.Response.Body.FlushAsync(cancellationToken);
                }

                // Wait for next interval
                await Task.Delay(interval, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Continuous streaming cancelled by client after {EventCount} events", eventCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in continuous streaming loop");
        }
    }

    /// <summary>
    /// Generates a single CompleteObjects event for continuous streaming
    /// </summary>
    private async Task GenerateContinuousCompleteObject(
        HttpContext context,
        HttpRequest request,
        string prompt,
        string? contextName,
        string method,
        string fullPathWithQuery,
        string? body,
        int eventCount,
        CancellationToken cancellationToken)
    {
        var completion = await _llmClient.GetCompletionAsync(prompt, cancellationToken);
        var cleanJson = JsonExtractor.ExtractJson(completion);

        if (!string.IsNullOrWhiteSpace(contextName))
        {
            _contextManager.AddToContext(contextName, method, fullPathWithQuery, body, cleanJson);
        }

        try
        {
            using var doc = JsonDocument.Parse(cleanJson);
            var root = doc.RootElement;

            // Send as single complete object event
            var eventData = new
            {
                data = root,
                index = eventCount,
                timestamp = DateTime.UtcNow,
                done = false
            };

            await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(eventData)}\n\n");
            await context.Response.Body.FlushAsync(cancellationToken);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse JSON in continuous CompleteObjects mode");
        }
    }

    /// <summary>
    /// Generates ArrayItems events for continuous streaming
    /// </summary>
    private async Task GenerateContinuousArrayItems(
        HttpContext context,
        HttpRequest request,
        string prompt,
        string? contextName,
        string method,
        string fullPathWithQuery,
        string? body,
        int eventCount,
        CancellationToken cancellationToken)
    {
        var completion = await _llmClient.GetCompletionAsync(prompt, cancellationToken);
        var cleanJson = JsonExtractor.ExtractJson(completion);

        if (!string.IsNullOrWhiteSpace(contextName))
        {
            _contextManager.AddToContext(contextName, method, fullPathWithQuery, body, cleanJson);
        }

        try
        {
            using var doc = JsonDocument.Parse(cleanJson);
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
                var arrayProp = root.EnumerateObject()
                    .FirstOrDefault(p => p.Value.ValueKind == JsonValueKind.Array);

                if (arrayProp.Value.ValueKind == JsonValueKind.Array)
                {
                    arrayName = arrayProp.Name;
                    items = arrayProp.Value.EnumerateArray().ToArray();
                }
                else
                {
                    items = new[] { root };
                }
            }
            else
            {
                items = new[] { root };
            }

            // Send each item as a separate event
            for (int i = 0; i < items.Length; i++)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var eventData = new
                {
                    item = items[i],
                    index = i,
                    total = items.Length,
                    arrayName,
                    batchNumber = eventCount,
                    timestamp = DateTime.UtcNow,
                    hasMore = i < items.Length - 1,
                    done = false
                };

                await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(eventData)}\n\n");
                await context.Response.Body.FlushAsync(cancellationToken);

                // Small delay between array items
                if (i < items.Length - 1)
                {
                    await Task.Delay(50, cancellationToken);
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse JSON in continuous ArrayItems mode");
        }
    }

    /// <summary>
    /// Generates LlmTokens events for continuous streaming (simplified version)
    /// </summary>
    private async Task GenerateContinuousLlmTokens(
        HttpContext context,
        HttpRequest request,
        string prompt,
        string? contextName,
        string method,
        string fullPathWithQuery,
        string? body,
        int eventCount,
        CancellationToken cancellationToken)
    {
        // For continuous mode, get complete text and send as chunks
        var completion = await _llmClient.GetCompletionAsync(prompt, cancellationToken);
        var cleanJson = JsonExtractor.ExtractJson(completion);

        if (!string.IsNullOrWhiteSpace(contextName))
        {
            _contextManager.AddToContext(contextName, method, fullPathWithQuery, body, cleanJson);
        }

        // Simulate token-by-token streaming by chunking the complete response
        var chunkSize = 10; // characters per chunk
        var accumulated = new StringBuilder();

        for (int i = 0; i < cleanJson.Length; i += chunkSize)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var chunk = cleanJson.Substring(i, Math.Min(chunkSize, cleanJson.Length - i));
            accumulated.Append(chunk);

            var eventData = new
            {
                chunk,
                accumulated = accumulated.ToString(),
                batchNumber = eventCount,
                done = false
            };

            await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(eventData)}\n\n");
            await context.Response.Body.FlushAsync(cancellationToken);

            await Task.Delay(20, cancellationToken); // Small delay between tokens
        }

        // Send final event for this batch
        var finalEvent = new
        {
            content = cleanJson,
            batchNumber = eventCount,
            timestamp = DateTime.UtcNow,
            done = false // Not done with continuous stream, just this batch
        };
        await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(finalEvent)}\n\n");
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
