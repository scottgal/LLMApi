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

            // Get response from LLM
            var rawResponse = await _llmClient.GetCompletionAsync(prompt, cancellationToken);
            var cleanJson = JsonExtractor.ExtractJson(rawResponse);

            // Wrap in GraphQL response format
            var graphQLResponse = WrapInGraphQLResponse(cleanJson);

            return graphQLResponse;
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
        var prompt = $@"You are a GraphQL API mock server. Generate realistic mock data for the following GraphQL query.

GraphQL Query:
{request.Query}";

        if (request.Variables != null && request.Variables.Count > 0)
        {
            prompt += $@"

Variables:
{JsonSerializer.Serialize(request.Variables, new JsonSerializerOptions { WriteIndented = true })}";
        }

        if (!string.IsNullOrWhiteSpace(request.OperationName))
        {
            prompt += $@"

Operation Name: {request.OperationName}";
        }

        prompt += @"

Instructions:
1. Analyze the GraphQL query to understand what data structure is requested
2. Generate realistic, varied mock data that matches the query structure EXACTLY
3. Include all requested fields in the response
4. Use realistic data types (strings, numbers, booleans, nulls as appropriate)
5. For arrays/lists, return 3-5 items with varied data
6. Return ONLY valid JSON that matches the query structure - no markdown, no explanations
7. DO NOT wrap the response in a 'data' field - return the data structure directly

Generate the mock data now:";

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
            _logger.LogError(ex, "Invalid JSON from LLM, returning error response");
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
