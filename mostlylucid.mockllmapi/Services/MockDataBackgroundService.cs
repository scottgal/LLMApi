using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using mostlylucid.mockllmapi.Hubs;
using mostlylucid.mockllmapi.Models;

namespace mostlylucid.mockllmapi.Services;

/// <summary>
/// Background service that continuously generates mock data and pushes it to SignalR clients
/// </summary>
public class MockDataBackgroundService(
    IHubContext<MockLlmHub> hubContext,
    IOptions<LLMockApiOptions> options,
    IServiceScopeFactory serviceScopeFactory,
    DynamicHubContextManager dynamicContextManager,
    OpenApiContextManager apiContextManager,
    ILogger<MockDataBackgroundService> logger)
    : BackgroundService
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _learnedShapes = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Concurrent.ConcurrentQueue<string>> _contextCaches = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _optimalBatchSizes = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _initialPrefillComplete = new();
    private readonly LLMockApiOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("MockData Background Service started");
        logger.LogInformation("Generating data for {Count} configured contexts", _options.HubContexts.Count);

        // Register all configured contexts with DynamicHubContextManager so they appear in the UI
        foreach (var ctx in _options.HubContexts)
        {
            logger.LogInformation("  - Configured context: {Name} ({Method} {Path})", ctx.Name, ctx.Method, ctx.Path);
            dynamicContextManager.RegisterContext(ctx);
        }

        // Pre-fill cache for all active contexts on startup for instant first messages
        logger.LogInformation("Pre-filling caches for instant first messages...");
        await PreFillAllContextCachesAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Generate data for each registered context (includes both appsettings and dynamically created contexts)
                var allContexts = dynamicContextManager.GetAllContexts();
                if (allContexts.Count > 0)
                {
                    logger.LogDebug("Processing {Count} total contexts", allContexts.Count);
                }

                foreach (var contextConfig in allContexts)
                {
                    if (contextConfig.IsActive)
                    {
                        logger.LogDebug("Generating data for context: {Name} (connections: {Count})",
                            contextConfig.Name, contextConfig.ConnectionCount);
                        await GenerateAndPushDataAsync(contextConfig, stoppingToken);
                    }
                    else
                    {
                        logger.LogDebug("Skipping inactive context: {Name}", contextConfig.Name);
                    }
                }

                // Wait for the configured interval
                await Task.Delay(_options.SignalRPushIntervalMs, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error generating mock data");
                // Continue running even if one generation fails
                await Task.Delay(1000, stoppingToken); // Brief delay before retry
            }
        }

        logger.LogInformation("MockData Background Service stopped");
    }

    private async Task GenerateAndPushDataAsync(Models.HubContextConfig contextConfig, CancellationToken cancellationToken)
    {
        try
        {
            // Check if error simulation is requested
            if (contextConfig.ErrorConfig != null)
            {
                // Send error data instead of generating mock data
                await hubContext.Clients.Group(contextConfig.Name).SendAsync("DataUpdate", new
                {
                    context = contextConfig.Name,
                    method = contextConfig.Method,
                    path = contextConfig.Path,
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    error = new
                    {
                        code = contextConfig.ErrorConfig.StatusCode,
                        message = contextConfig.ErrorConfig.GetMessage(),
                        details = contextConfig.ErrorConfig.Details
                    }
                }, cancellationToken);

                logger.LogDebug("Pushed error data to context: {Context} (Code: {StatusCode})",
                    contextConfig.Name, contextConfig.ErrorConfig.StatusCode);
                return;
            }

            // Create a scope to resolve scoped services
            using var scope = serviceScopeFactory.CreateScope();
            var promptBuilder = scope.ServiceProvider.GetRequiredService<PromptBuilder>();
            var llmClient = scope.ServiceProvider.GetRequiredService<LlmClient>();

            // Determine if shape is JSON Schema
            bool isJsonSchema = contextConfig.IsJsonSchema ??
                               (!string.IsNullOrWhiteSpace(contextConfig.Shape) &&
                               (contextConfig.Shape.Contains("\"$schema\"") || contextConfig.Shape.Contains("\"properties\"")));

            var shapeInfo = new ShapeInfo
            {
                Shape = contextConfig.Shape,
                IsJsonSchema = isJsonSchema
            };

            // Prefer a learned shape for this context if no explicit shape is provided
            string? effectiveShape = !string.IsNullOrWhiteSpace(contextConfig.Shape)
                ? contextConfig.Shape
                : (_learnedShapes.TryGetValue(contextConfig.Name, out var learned) ? learned : null);

            shapeInfo.Shape = effectiveShape;
            shapeInfo.IsJsonSchema = shapeInfo.IsJsonSchema && !string.IsNullOrWhiteSpace(effectiveShape);

            // Get API context history if configured
            var contextHistory = !string.IsNullOrWhiteSpace(contextConfig.ApiContextName)
                ? apiContextManager.GetContextForPrompt(contextConfig.ApiContextName)
                : null;

            // Build prompt using the (possibly learned) shape and context history
            var prompt = promptBuilder.BuildPrompt(
                contextConfig.Method,
                contextConfig.Path,
                contextConfig.Body,
                shapeInfo,
                streaming: false,
                description: contextConfig.Description,
                contextHistory: contextHistory);

            // Pull from per-context cache; if empty, prefill with a batch in one upstream call
            var queue = _contextCaches.GetOrAdd(contextConfig.Name, _ => new System.Collections.Concurrent.ConcurrentQueue<string>());
            if (queue.IsEmpty)
            {
                await PrefillContextCacheAsync(contextConfig.Name, llmClient, prompt, cancellationToken);
            }

            if (!queue.TryDequeue(out var cleanJson) || string.IsNullOrWhiteSpace(cleanJson))
            {
                // Fallback to single fetch if still empty
                var single = await llmClient.GetCompletionAsync(prompt, cancellationToken);
                cleanJson = ExtractJson(single);
            }
            else
            {
                // If cache is running low, refill in background using context-specific batch size
                int optimalBatch = GetOptimalBatchSize(contextConfig.Name);
                if (queue.Count < Math.Max(1, optimalBatch / 2))
                {
                    _ = Task.Run(() => PrefillContextCacheAsync(contextConfig.Name, llmClient, prompt, cancellationToken));
                }
            }

            // Skip if we don't have meaningful data yet (empty object or whitespace)
            if (string.IsNullOrWhiteSpace(cleanJson) || cleanJson.Trim() == "{}" || cleanJson.Trim() == "[]")
            {
                logger.LogDebug("Skipping empty data for context: {Context}, waiting for LLM response", contextConfig.Name);
                return;
            }

            // Parse JSON to verify it's valid
            var jsonDoc = System.Text.Json.JsonDocument.Parse(cleanJson);

            // Learn and persist a stable shape from the first successful sample when not explicitly provided
            if (string.IsNullOrWhiteSpace(contextConfig.Shape) && !_learnedShapes.ContainsKey(contextConfig.Name))
            {
                try
                {
                    var derived = DeriveCanonicalShape(jsonDoc.RootElement);
                    if (!string.IsNullOrWhiteSpace(derived))
                    {
                        _learnedShapes.TryAdd(contextConfig.Name, derived);
                    }
                }
                catch { /* ignore shape derivation errors */ }
            }

            // Store in API context if configured
            if (!string.IsNullOrWhiteSpace(contextConfig.ApiContextName))
            {
                apiContextManager.AddToContext(
                    contextConfig.ApiContextName,
                    contextConfig.Method,
                    contextConfig.Path,
                    contextConfig.Body,
                    cleanJson);
            }

            // Send to all clients in this context group
            await hubContext.Clients.Group(contextConfig.Name).SendAsync("DataUpdate", new
            {
                context = contextConfig.Name,
                method = contextConfig.Method,
                path = contextConfig.Path,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                data = jsonDoc.RootElement
            }, cancellationToken);

            logger.LogDebug("Pushed data to context: {Context} ({Method} {Path})",
                contextConfig.Name, contextConfig.Method, contextConfig.Path);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to generate/push data for context: {Context}", contextConfig.Name);
        }
    }

    /// <summary>
    /// Pre-fills cache for all active contexts on startup
    /// </summary>
    private async Task PreFillAllContextCachesAsync(CancellationToken cancellationToken)
    {
        var allContexts = dynamicContextManager.GetAllContexts();
        var tasks = new List<Task>();

        foreach (var contextConfig in allContexts.Where(c => c.IsActive))
        {
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    using var scope = serviceScopeFactory.CreateScope();
                    var promptBuilder = scope.ServiceProvider.GetRequiredService<PromptBuilder>();
                    var llmClient = scope.ServiceProvider.GetRequiredService<LlmClient>();

                    bool isJsonSchema = contextConfig.IsJsonSchema ??
                                       (!string.IsNullOrWhiteSpace(contextConfig.Shape) &&
                                       (contextConfig.Shape.Contains("\"$schema\"") || contextConfig.Shape.Contains("\"properties\"")));

                    var shapeInfo = new ShapeInfo
                    {
                        Shape = contextConfig.Shape,
                        IsJsonSchema = isJsonSchema
                    };

                    // Get API context history if configured
                    var contextHistory = !string.IsNullOrWhiteSpace(contextConfig.ApiContextName)
                        ? apiContextManager.GetContextForPrompt(contextConfig.ApiContextName)
                        : null;

                    var prompt = promptBuilder.BuildPrompt(
                        contextConfig.Method,
                        contextConfig.Path,
                        contextConfig.Body,
                        shapeInfo,
                        streaming: false,
                        description: contextConfig.Description,
                        contextHistory: contextHistory);

                    await PrefillContextCacheAsync(contextConfig.Name, llmClient, prompt, cancellationToken);
                    _initialPrefillComplete.TryAdd(contextConfig.Name, true);
                    logger.LogInformation("Pre-filled cache for context: {Context}", contextConfig.Name);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to pre-fill cache for context: {Context}", contextConfig.Name);
                }
            }, cancellationToken));
        }

        await Task.WhenAll(tasks);
        logger.LogInformation("Cache pre-fill complete");
    }

    /// <summary>
    /// Extracts clean JSON from LLM response that might include markdown or explanatory text
    /// Measures generation time on first call to calculate optimal batch size
    /// </summary>
    private async Task PrefillContextCacheAsync(string contextName, LlmClient llmClient, string prompt, CancellationToken cancellationToken)
    {
        try
        {
            // If this is the first time, measure generation time with a single response
            if (!_optimalBatchSizes.ContainsKey(contextName))
            {
                logger.LogDebug("Measuring generation time for context: {Context}", contextName);
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                var singleResult = await llmClient.GetCompletionAsync(prompt, cancellationToken);
                var cleanJson = ExtractJson(singleResult);

                stopwatch.Stop();
                var generationTimeMs = stopwatch.ElapsedMilliseconds;

                // Calculate optimal batch size: how many can we generate before next push interval?
                int pushIntervalMs = _options.SignalRPushIntervalMs;
                int optimalBatch = generationTimeMs > 0
                    ? Math.Max(1, Math.Min(20, pushIntervalMs / (int)generationTimeMs))
                    : 5; // Default to 5 if timing is zero

                _optimalBatchSizes.TryAdd(contextName, optimalBatch);

                logger.LogInformation(
                    "Context {Context}: generation time={GenerationMs}ms, push interval={PushMs}ms, optimal batch size={BatchSize}",
                    contextName, generationTimeMs, pushIntervalMs, optimalBatch);

                // Add the first generated response to cache
                var queue = _contextCaches.GetOrAdd(contextName, _ => new System.Collections.Concurrent.ConcurrentQueue<string>());
                if (!string.IsNullOrWhiteSpace(cleanJson))
                {
                    queue.Enqueue(cleanJson);
                }

                // Now generate the rest of the batch
                if (optimalBatch > 1)
                {
                    var results = await llmClient.GetNCompletionsAsync(prompt, optimalBatch - 1, cancellationToken);
                    foreach (var r in results)
                    {
                        var json = ExtractJson(r);
                        if (!string.IsNullOrWhiteSpace(json))
                        {
                            queue.Enqueue(json);
                        }
                    }
                }
            }
            else
            {
                // Use the previously calculated optimal batch size
                int batch = GetOptimalBatchSize(contextName);
                var results = await llmClient.GetNCompletionsAsync(prompt, batch, cancellationToken);
                var queue = _contextCaches.GetOrAdd(contextName, _ => new System.Collections.Concurrent.ConcurrentQueue<string>());
                foreach (var r in results)
                {
                    var json = ExtractJson(r);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        queue.Enqueue(json);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to prefill cache for context {Context}", contextName);
        }
    }

    private int GetOptimalBatchSize(string contextName)
    {
        if (_optimalBatchSizes.TryGetValue(contextName, out var cached))
        {
            return cached;
        }

        // Fallback to static calculation if not yet measured
        var max = Math.Max(1, _options.MaxCachePerKey);
        return Math.Min(10, Math.Max(5, max));
    }

    private static string DeriveCanonicalShape(System.Text.Json.JsonElement element)
    {
        // Build a simple shape example by preserving property names and emitting example types/structure
        var sb = new System.Text.StringBuilder();
        WriteShape(element, sb);
        return sb.ToString();

        static void WriteShape(System.Text.Json.JsonElement el, System.Text.StringBuilder sb)
        {
            switch (el.ValueKind)
            {
                case System.Text.Json.JsonValueKind.Object:
                    sb.Append('{');
                    bool first = true;
                    foreach (var prop in el.EnumerateObject())
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        sb.Append('"').Append(prop.Name).Append('"').Append(':');
                        WriteShape(prop.Value, sb);
                    }
                    sb.Append('}');
                    break;
                case System.Text.Json.JsonValueKind.Array:
                    sb.Append('[');
                    if (el.GetArrayLength() > 0)
                    {
                        WriteShape(el[0], sb);
                    }
                    sb.Append(']');
                    break;
                case System.Text.Json.JsonValueKind.String:
                    sb.Append("\"string\"");
                    break;
                case System.Text.Json.JsonValueKind.Number:
                    sb.Append('0');
                    break;
                case System.Text.Json.JsonValueKind.True:
                case System.Text.Json.JsonValueKind.False:
                    sb.Append("true");
                    break;
                case System.Text.Json.JsonValueKind.Null:
                default:
                    sb.Append("null");
                    break;
            }
        }
    }

    private static string ExtractJson(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return "{}"; // ensure valid JSON is returned

        var trimmed = response.Trim();

        // If it already looks like a single JSON value, validate it
        if ((trimmed.StartsWith("{") && trimmed.EndsWith("}")) ||
            (trimmed.StartsWith("[") && trimmed.EndsWith("]")))
        {
            if (IsValidJson(trimmed))
                return trimmed;
            // fall through to extraction if invalid
        }

        // Extract from fenced code blocks first
        var jsonPattern = @"```(?:json)?\s*(\{[\s\S]*?\}|\[[\s\S]*?\])\s*```";
        var match = System.Text.RegularExpressions.Regex.Match(response, jsonPattern);
        if (match.Success)
        {
            var candidate = match.Groups[1].Value.Trim();
            if (IsValidJson(candidate)) return candidate;
        }

        // Robust balanced scan: find the first complete JSON value (object or array)
        var balanced = ExtractFirstBalancedJsonValue(response);
        if (!string.IsNullOrEmpty(balanced) && IsValidJson(balanced))
            return balanced;

        // As a last resort, try loose regex matches
        var jsonObjectPattern = @"\{[\s\S]*\}";
        var objectMatch = System.Text.RegularExpressions.Regex.Match(response, jsonObjectPattern);
        if (objectMatch.Success && IsValidJson(objectMatch.Value.Trim()))
        {
            return objectMatch.Value.Trim();
        }

        var jsonArrayPattern = @"\[[\s\S]*\]";
        var arrayMatch = System.Text.RegularExpressions.Regex.Match(response, jsonArrayPattern);
        if (arrayMatch.Success && IsValidJson(arrayMatch.Value.Trim()))
        {
            return arrayMatch.Value.Trim();
        }

        // If nothing valid found, return empty object to avoid parse failures upstream
        return "{}";
    }

    private static bool IsValidJson(string text)
    {
        try
        {
            System.Text.Json.JsonDocument.Parse(text);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? ExtractFirstBalancedJsonValue(string text)
    {
        int length = text.Length;
        bool inString = false;
        bool escape = false;
        int depth = 0;
        char? open = null; // '{' or '['
        int start = -1;

        for (int i = 0; i < length; i++)
        {
            char c = text[i];

            if (inString)
            {
                if (escape)
                {
                    escape = false;
                }
                else if (c == '\\')
                {
                    escape = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }
                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c == '{' || c == '[')
            {
                if (depth == 0)
                {
                    open = c;
                    start = i;
                }
                depth++;
                continue;
            }

            if (c == '}' || c == ']')
            {
                if (depth > 0)
                {
                    depth--;
                    if (depth == 0 && start >= 0)
                    {
                        // Ensure matching type
                        if ((open == '{' && c == '}') || (open == '[' && c == ']'))
                        {
                            var candidate = text.Substring(start, i - start + 1).Trim();
                            return candidate;
                        }
                        else
                        {
                            // mismatch; reset and continue searching
                            start = -1;
                            open = null;
                        }
                    }
                }
                continue;
            }
        }

        return null;
    }
}
