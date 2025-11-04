using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using mostlylucid.mockllmapi.Models;
using mostlylucid.mockllmapi.Services;
using System.Text.Json;

namespace mostlylucid.mockllmapi.RequestHandlers;

/// <summary>
/// Handles GraphQL mock API requests
/// </summary>
public class GraphQLRequestHandler
{
    private readonly LLMockApiOptions _options;
    private readonly PromptBuilder _promptBuilder;
    private readonly LlmClient _llmClient;
    private readonly DelayHelper _delayHelper;
    private readonly ILogger<GraphQLRequestHandler> _logger;

    public GraphQLRequestHandler(
        IOptions<LLMockApiOptions> options,
        PromptBuilder promptBuilder,
        LlmClient llmClient,
        DelayHelper delayHelper,
        ILogger<GraphQLRequestHandler> logger)
    {
        _options = options.Value;
        _promptBuilder = promptBuilder;
        _llmClient = llmClient;
        _delayHelper = delayHelper;
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

        try
        {
            // Parse GraphQL request
            var graphQLRequest = ParseGraphQLRequest(body);

            // Build prompt specifically for GraphQL
            var prompt = BuildGraphQLPrompt(graphQLRequest);

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
                            prompt = BuildRetryPrompt(graphQLRequest);
                            continue;
                        }

                        return CreateGraphQLError("LLM did not return valid JSON data after retries");
                    }

                    _logger.LogDebug("Attempt {Attempt}/{MaxAttempts}: Extracted JSON (first 500 chars): {Json}",
                        attempt, maxAttempts, cleanJson.Length > 500 ? cleanJson.Substring(0, 500) + "..." : cleanJson);

                    // Try to wrap in GraphQL response format - this validates the JSON
                    var graphQLResponse = WrapInGraphQLResponse(cleanJson);

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

    private static string BuildGraphQLPrompt(GraphQLRequest request)
    {
        var prompt = $@"JSON for: {request.Query}

RULES: 2 items max. Complete values only. NO ... NO dots NO // NO comments.
{{""users"":[{{""id"":1,""name"":""A""}},{{""id"":2,""name"":""B""}}]}}";

        if (request.Variables != null && request.Variables.Count > 0)
        {
            prompt += $@"
Vars: {JsonSerializer.Serialize(request.Variables)}";
        }

        return prompt;
    }

    private static string BuildRetryPrompt(GraphQLRequest request)
    {
        var prompt = $@"JSON: {request.Query}

{{ }} ONLY. 2 items. NO ... NO // NO text.
{{""field"":[{{""id"":1}}]}}";

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
