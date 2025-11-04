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
    ILogger<MockDataBackgroundService> logger)
    : BackgroundService
{
    private readonly LLMockApiOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("MockData Background Service started");
        logger.LogInformation("Generating data for {Count} configured contexts", _options.HubContexts.Count);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Generate data for each configured context
                foreach (var contextConfig in _options.HubContexts.Where(c => c.IsActive))
                {
                    await GenerateAndPushDataAsync(contextConfig, stoppingToken);
                }

                // Generate data for each dynamic context
                var dynamicContexts = dynamicContextManager.GetAllContexts();
                foreach (var contextConfig in dynamicContexts.Where(c => c.IsActive))
                {
                    await GenerateAndPushDataAsync(contextConfig, stoppingToken);
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

            // Build prompt using the configured request parameters
            var prompt = promptBuilder.BuildPrompt(
                contextConfig.Method,
                contextConfig.Path,
                contextConfig.Body,
                shapeInfo,
                streaming: false);

            // Generate data using LLM
            var data = await llmClient.GetCompletionAsync(prompt, cancellationToken);

            // Extract clean JSON from response (LLM might add markdown or explanatory text)
            var cleanJson = ExtractJson(data);

            // If shape is provided (and not a JSON Schema), ensure we don't just echo the shape back
            if (!string.IsNullOrWhiteSpace(contextConfig.Shape) && !isJsonSchema)
            {
                if (TryNormalizeJson(cleanJson, out var normResponse) && TryNormalizeJson(contextConfig.Shape!, out var normShape)
                    && normResponse == normShape)
                {
                    logger.LogWarning("LLM returned data identical to configured shape for context: {Context}. Retrying generation to avoid echoing shape.", contextConfig.Name);

                    // Strengthen the instruction to avoid echoing the sample shape
                    var retryPrompt = prompt + "\nDO NOT return the provided sample shape verbatim. Replace placeholder values (e.g., 0, \\\"string\\\") with realistic, varied values. Output only JSON.";
                    var retryData = await llmClient.GetCompletionAsync(retryPrompt, cancellationToken);
                    cleanJson = ExtractJson(retryData);

                    if (TryNormalizeJson(cleanJson, out normResponse) && normResponse == normShape)
                    {
                        logger.LogWarning("Second attempt still matched the shape for context: {Context}. Falling back to empty object.", contextConfig.Name);
                        cleanJson = "{}";
                    }
                }
            }

            // Parse JSON to verify it's valid
            var jsonDoc = System.Text.Json.JsonDocument.Parse(cleanJson);

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
    /// Extracts clean JSON from LLM response that might include markdown or explanatory text
    /// </summary>
    private static string ExtractJson(string response)
    {
        // Always return something that can be parsed as JSON to avoid downstream exceptions
        if (string.IsNullOrWhiteSpace(response))
            return "{}";

        var trimmed = response.Trim();

        // Fast-path: looks like JSON already
        if ((trimmed.StartsWith("{") && trimmed.EndsWith("}")) ||
            (trimmed.StartsWith("[") && trimmed.EndsWith("]")))
        {
            try
            {
                System.Text.Json.JsonDocument.Parse(trimmed);
                return trimmed;
            }
            catch
            {
                // fall through to extraction
            }
        }

        // 1) Prefer fenced code blocks with optional json hint
        var jsonPattern = @"```(?:json)?\s*(\{[\s\S]*?\}|\[[\s\S]*?\])\s*```";
        var match = System.Text.RegularExpressions.Regex.Match(response, jsonPattern);
        if (match.Success)
        {
            var candidate = match.Groups[1].Value.Trim();
            try { System.Text.Json.JsonDocument.Parse(candidate); return candidate; } catch { /* continue */ }
        }

        // 2) Scan for the first balanced JSON object/array respecting strings/escapes
        string? balanced = TryExtractBalancedJson(response);
        if (balanced != null)
        {
            return balanced;
        }

        // 3) Last resort: try greedy object/array regex and validate
        var jsonObjectPattern = @"\{[\s\S]*\}";
        var objectMatch = System.Text.RegularExpressions.Regex.Match(response, jsonObjectPattern);
        if (objectMatch.Success)
        {
            var candidate = objectMatch.Value.Trim();
            try { System.Text.Json.JsonDocument.Parse(candidate); return candidate; } catch { /* ignore */ }
        }

        var jsonArrayPattern = @"\[[\s\S]*\]";
        var arrayMatch = System.Text.RegularExpressions.Regex.Match(response, jsonArrayPattern);
        if (arrayMatch.Success)
        {
            var candidate = arrayMatch.Value.Trim();
            try { System.Text.Json.JsonDocument.Parse(candidate); return candidate; } catch { /* ignore */ }
        }

        // 4) Fallback to an empty object to keep pipeline healthy
        return "{}";
    }

    private static string? TryExtractBalancedJson(string input)
    {
        int idx = 0;
        while (idx < input.Length)
        {
            int start = input.IndexOfAny(new[] { '{', '[' }, idx);
            if (start == -1) break;

            bool inString = false;
            char stringQuote = '\0';
            bool escape = false;
            int brace = 0;
            int bracket = 0;

            for (int i = start; i < input.Length; i++)
            {
                char c = input[i];
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
                    else if (c == stringQuote)
                    {
                        inString = false;
                    }
                    continue;
                }

                if (c == '"' || c == '\'')
                {
                    inString = true;
                    stringQuote = c;
                    continue;
                }

                if (c == '{') brace++;
                else if (c == '}') brace--;
                else if (c == '[') bracket++;
                else if (c == ']') bracket--;

                // If we ever go negative, this candidate is invalid
                if (brace < 0 || bracket < 0) break;

                // Completed a top-level balanced block
                if (start > -1 && brace == 0 && bracket == 0)
                {
                    var candidate = input.Substring(start, i - start + 1).Trim();
                    try
                    {
                        System.Text.Json.JsonDocument.Parse(candidate);
                        return candidate;
                    }
                    catch
                    {
                        break; // stop scanning this start; try next
                    }
                }
            }

            idx = start + 1;
        }

        return null;
    }

    private static bool TryNormalizeJson(string json, out string normalized)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            normalized = System.Text.Json.JsonSerializer.Serialize(doc.RootElement);
            return true;
        }
        catch
        {
            normalized = string.Empty;
            return false;
        }
    }
}
