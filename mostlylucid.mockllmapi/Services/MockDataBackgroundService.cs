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
                foreach (var contextConfig in _options.HubContexts)
                {
                    await GenerateAndPushDataAsync(contextConfig, stoppingToken);
                }

                // Generate data for each dynamic context
                var dynamicContexts = dynamicContextManager.GetAllContexts();
                foreach (var contextConfig in dynamicContexts)
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
        if (string.IsNullOrWhiteSpace(response))
            return response;

        var trimmed = response.Trim();

        // Check if it's already valid JSON
        if ((trimmed.StartsWith("{") && trimmed.EndsWith("}")) ||
            (trimmed.StartsWith("[") && trimmed.EndsWith("]")))
        {
            // Try to parse it as-is first
            try
            {
                System.Text.Json.JsonDocument.Parse(trimmed);
                return trimmed;
            }
            catch
            {
                // If parsing fails, continue with extraction logic
            }
        }

        // Remove markdown code blocks
        var jsonPattern = @"```(?:json)?\s*(\{[\s\S]*?\}|\[[\s\S]*?\])\s*```";
        var match = System.Text.RegularExpressions.Regex.Match(response, jsonPattern);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        // Try to find JSON object or array in the text
        var jsonObjectPattern = @"\{[\s\S]*\}";
        var objectMatch = System.Text.RegularExpressions.Regex.Match(response, jsonObjectPattern);
        if (objectMatch.Success)
        {
            return objectMatch.Value.Trim();
        }

        var jsonArrayPattern = @"\[[\s\S]*\]";
        var arrayMatch = System.Text.RegularExpressions.Regex.Match(response, jsonArrayPattern);
        if (arrayMatch.Success)
        {
            return arrayMatch.Value.Trim();
        }

        // Return as-is if no patterns matched
        return trimmed;
    }
}
