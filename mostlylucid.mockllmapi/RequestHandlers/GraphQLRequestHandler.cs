using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using mostlylucid.mockllmapi.Models;
using mostlylucid.mockllmapi.Services;
using System.Text.Json;

namespace mostlylucid.mockllmapi.RequestHandlers;

/// <summary>
/// Handles GraphQL mock API requests with automatic chunking support
/// </summary>
public class GraphQLRequestHandler
{
    private readonly LLMockApiOptions _options;
    private readonly ShapeExtractor _shapeExtractor;
    private readonly ContextExtractor _contextExtractor;
    private readonly OpenApiContextManager _contextManager;
    private readonly PromptBuilder _promptBuilder;
    private readonly LlmClient _llmClient;
    private readonly DelayHelper _delayHelper;
    private readonly ChunkingCoordinator _chunkingCoordinator;
    private readonly AutoShapeManager _autoShapeManager;
    private readonly ILogger<GraphQLRequestHandler> _logger;

    public GraphQLRequestHandler(
        IOptions<LLMockApiOptions> options,
        ShapeExtractor shapeExtractor,
        ContextExtractor contextExtractor,
        OpenApiContextManager contextManager,
        PromptBuilder promptBuilder,
        LlmClient llmClient,
        DelayHelper delayHelper,
        ChunkingCoordinator chunkingCoordinator,
        AutoShapeManager autoShapeManager,
        ILogger<GraphQLRequestHandler> logger)
    {
        _options = options.Value;
        _shapeExtractor = shapeExtractor;
        _contextExtractor = contextExtractor;
        _contextManager = contextManager;
        _promptBuilder = promptBuilder;
        _llmClient = llmClient;
        _delayHelper = delayHelper;
        _chunkingCoordinator = chunkingCoordinator;
        _autoShapeManager = autoShapeManager;
        _logger = logger;
    }

    /// <summary>
    /// Handles a GraphQL request
    /// </summary>
    public async Task<string> HandleGraphQLRequestAsync(
        string? body,
        HttpRequest request,
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        // Apply random request delay if configured
        await _delayHelper.ApplyRequestDelayAsync(cancellationToken);

        // Extract shape information (for error config)
        var shapeInfo = _shapeExtractor.ExtractShapeInfo(request, body);

        // Check if error simulation is requested
        if (shapeInfo.ErrorConfig != null)
        {
            context.Response.StatusCode = shapeInfo.ErrorConfig.StatusCode;
            _logger.LogDebug("Returning simulated error for GraphQL: {StatusCode} - {Message}",
                shapeInfo.ErrorConfig.StatusCode, shapeInfo.ErrorConfig.GetMessage());
            return shapeInfo.ErrorConfig.ToGraphQLJson();
        }

        // Extract context name
        var contextName = _contextExtractor.ExtractContextName(request, body);

        // Get context history if context is specified
        var contextHistory = !string.IsNullOrWhiteSpace(contextName)
            ? _contextManager.GetContextForPrompt(contextName)
            : null;

        try
        {
            // Parse GraphQL request
            var graphQLRequest = ParseGraphQLRequest(body);

            // Build prompt specifically for GraphQL
            var prompt = BuildGraphQLPrompt(graphQLRequest, contextHistory);

            // Try to get valid JSON from LLM (with retry)
            const int maxAttempts = 2;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    // Get response from LLM with max_tokens to prevent truncation
                    var rawResponse = await _llmClient.GetCompletionAsync(prompt, cancellationToken, _options.GraphQLMaxTokens);

                    _logger.LogDebug("Attempt {Attempt}/{MaxAttempts}: Raw LLM response (first 500 chars): {Response}",
                        attempt, maxAttempts, rawResponse.Length > 500 ? rawResponse.Substring(0, 500) + "..." : rawResponse);

                    var cleanJson = JsonExtractor.ExtractJson(rawResponse);

                    if (string.IsNullOrWhiteSpace(cleanJson))
                    {
                        _logger.LogWarning("Attempt {Attempt}/{MaxAttempts}: JsonExtractor returned empty result", attempt, maxAttempts);

                        if (attempt < maxAttempts)
                        {
                            // Retry with more explicit prompt
                            _logger.LogInformation("Retrying with more explicit JSON-only prompt");
                            prompt = BuildRetryPrompt(graphQLRequest, contextHistory);
                            continue;
                        }

                        return CreateGraphQLError("LLM did not return valid JSON data after retries");
                    }

                    _logger.LogDebug("Attempt {Attempt}/{MaxAttempts}: Extracted JSON (first 500 chars): {Json}",
                        attempt, maxAttempts, cleanJson.Length > 500 ? cleanJson.Substring(0, 500) + "..." : cleanJson);

                    // Try to wrap in GraphQL response format - this validates the JSON
                    var graphQLResponse = WrapInGraphQLResponse(cleanJson);

                    // Store in context if context name was provided
                    if (!string.IsNullOrWhiteSpace(contextName))
                    {
                        _contextManager.AddToContext(contextName, "POST", "/graphql", body, graphQLResponse);
                    }

                    // Store shape from response if autoshape is enabled
                    _autoShapeManager.StoreShapeFromResponse(request, graphQLResponse);

                    // Success! Return the valid response
                    if (attempt > 1)
                    {
                        _logger.LogInformation("Successfully generated valid GraphQL response on attempt {Attempt}", attempt);
                    }

                    return graphQLResponse;
                }
                catch (JsonException ex) when (attempt < maxAttempts)
                {
                    _logger.LogWarning(ex, "Attempt {Attempt}/{MaxAttempts}: JSON parsing failed, retrying", attempt, maxAttempts);
                    prompt = BuildRetryPrompt(graphQLRequest);
                }
            }

            // If we get here, all attempts failed
            return CreateGraphQLError("Failed to generate valid GraphQL response after multiple attempts");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing GraphQL request");
            return CreateGraphQLError(ex.Message);
        }
    }

    private static GraphQLRequest ParseGraphQLRequest(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new InvalidOperationException("GraphQL request body is empty");
        }

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var request = JsonSerializer.Deserialize<GraphQLRequest>(body, options);

            if (request == null || string.IsNullOrWhiteSpace(request.Query))
            {
                throw new InvalidOperationException("GraphQL query is required");
            }

            return request;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid GraphQL request format: {ex.Message}", ex);
        }
    }

    private static string BuildGraphQLPrompt(GraphQLRequest request, string? contextHistory = null)
    {
        var contextSection = string.IsNullOrWhiteSpace(contextHistory)
            ? ""
            : $"\n{contextHistory}\n";

        var prompt = $@"JSON for: {request.Query}
{contextSection}
CRITICAL: MUST be valid, complete JSON. If it's too complex, return LESS data.
RULES:
- Max 2 items in arrays
- Keep nesting SHALLOW (max 2 levels)
- Use SHORT strings (1-3 words)
- STOP when you run out of space
- Better to return 1 complete item than 10 incomplete ones

GOOD: {{""users"":[{{""id"":1,""name"":""Bob""}}]}}
BAD: {{""users"":[{{""id"":1,""name"":""Bob"",""profile"":{{""address"":{{""street"":""123...";

        if (request.Variables != null && request.Variables.Count > 0)
        {
            prompt += $@"
Vars: {JsonSerializer.Serialize(request.Variables)}";
        }

        return prompt;
    }

    private static string BuildRetryPrompt(GraphQLRequest request, string? contextHistory = null)
    {
        var contextSection = string.IsNullOrWhiteSpace(contextHistory)
            ? ""
            : $"\n{contextHistory}\n";

        var prompt = $@"SIMPLE JSON ONLY: {request.Query}
{contextSection}
MUST be valid, complete JSON. Keep it TINY.
1 item. Flat structure. Short values.
Example: {{""data"":[{{""id"":1}}]}}";

        return prompt;
    }

    private string WrapInGraphQLResponse(string dataJson)
    {
        try
        {
            // Validate that it's valid JSON
            using var doc = JsonDocument.Parse(dataJson);

            // Wrap in GraphQL response format
            return JsonSerializer.Serialize(new
            {
                data = doc.RootElement
            });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Invalid JSON from LLM. Attempted to parse: {Json}",
                dataJson.Length > 200 ? dataJson.Substring(0, 200) + "..." : dataJson);
            return CreateGraphQLError("Failed to generate valid GraphQL response");
        }
    }

    private static string CreateGraphQLError(string message)
    {
        return JsonSerializer.Serialize(new
        {
            data = (object?)null,
            errors = new[]
            {
                new
                {
                    message,
                    extensions = new { code = "INTERNAL_SERVER_ERROR" }
                }
            }
        });
    }

    private class GraphQLRequest
    {
        public string Query { get; set; } = string.Empty;
        public Dictionary<string, object>? Variables { get; set; }
        public string? OperationName { get; set; }
    }
}
