using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using mostlylucid.mockllmapi.Services;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace mostlylucid.mockllmapi.RequestHandlers;

/// <summary>
/// Handles requests for OpenAPI-based mock endpoints.
/// Matches incoming requests to OpenAPI operations and generates responses based on the spec.
/// </summary>
public class OpenApiRequestHandler
{
    private readonly ILogger<OpenApiRequestHandler> _logger;
    private readonly LlmClient _llmClient;
    private readonly OpenApiSchemaConverter _schemaConverter;
    private readonly OpenApiContextManager _contextManager;

    public OpenApiRequestHandler(
        ILogger<OpenApiRequestHandler> logger,
        LlmClient llmClient,
        OpenApiSchemaConverter schemaConverter,
        OpenApiContextManager contextManager)
    {
        _logger = logger;
        _llmClient = llmClient;
        _schemaConverter = schemaConverter;
        _contextManager = contextManager;
    }

    /// <summary>
    /// Handles a request using an OpenAPI operation definition.
    /// </summary>
    public async Task<string> HandleRequestAsync(
        HttpContext context,
        OpenApiDocument document,
        string path,
        OperationType method,
        OpenApiOperation operation,
        string? contextName = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Handling OpenAPI request: {Method} {Path} - {OperationId}",
            method, path, operation.OperationId ?? "Unknown");

        try
        {
            // Get the response shape from the OpenAPI schema
            var shape = _schemaConverter.GetResponseShape(operation);
            if (string.IsNullOrEmpty(shape))
            {
                _logger.LogWarning("No response shape found for operation, using generic shape");
                shape = "{\"message\":\"string\",\"data\":{}}";
            }

            // Get operation description for context
            var description = _schemaConverter.GetOperationDescription(operation, path, method);

            // Get API context history if available
            var contextHistory = !string.IsNullOrWhiteSpace(contextName)
                ? _contextManager.GetContextForPrompt(contextName)
                : null;

            // Build the prompt
            var prompt = BuildOpenApiPrompt(description, shape, operation, contextHistory);

            // Get request body for context storage
            string? requestBody = null;
            if (context.Request.ContentLength > 0)
            {
                context.Request.EnableBuffering();
                using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
                requestBody = await reader.ReadToEndAsync(cancellationToken);
                context.Request.Body.Position = 0;
            }

            // Generate the mock response using LLM
            var response = await _llmClient.GetCompletionAsync(prompt, cancellationToken);

            // Extract and validate JSON
            var jsonResponse = JsonExtractor.ExtractJson(response);
            if (string.IsNullOrEmpty(jsonResponse))
            {
                _logger.LogWarning("LLM returned invalid JSON, falling back to shape template");
                jsonResponse = shape;
            }

            // Validate JSON structure
            try
            {
                JsonDocument.Parse(jsonResponse);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Invalid JSON from LLM, returning shape template");
                jsonResponse = shape;
            }

            // Store in context if context name is provided
            if (!string.IsNullOrWhiteSpace(contextName))
            {
                _contextManager.AddToContext(contextName, method.ToString().ToUpper(), path, requestBody, jsonResponse);
            }

            return jsonResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling OpenAPI request: {Method} {Path}", method, path);
            return JsonSerializer.Serialize(new { error = "Failed to generate mock response", message = ex.Message });
        }
    }

    /// <summary>
    /// Handles a streaming request using an OpenAPI operation definition.
    /// </summary>
    public async IAsyncEnumerable<string> HandleStreamingRequestAsync(
        HttpContext context,
        OpenApiDocument document,
        string path,
        OperationType method,
        OpenApiOperation operation,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Handling OpenAPI streaming request: {Method} {Path} - {OperationId}",
            method, path, operation.OperationId ?? "Unknown");

        // Get the response shape
        var shape = _schemaConverter.GetResponseShape(operation);
        if (string.IsNullOrEmpty(shape))
        {
            shape = "{\"message\":\"string\",\"data\":{}}";
        }

        // Get operation description
        var description = _schemaConverter.GetOperationDescription(operation, path, method);

        // Build the prompt (no context for streaming for now - could be added later)
        var prompt = BuildOpenApiPrompt(description, shape, operation, null);

        // Get streaming response from LLM
        using var httpRes = await _llmClient.GetStreamingCompletionAsync(prompt, cancellationToken);
        await using var stream = await httpRes.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        var accumulated = new System.Text.StringBuilder();

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
                    yield return JsonSerializer.Serialize(new { content = finalJson, done = true });
                    yield break;
                }

                // Try to parse the chunk
                var token = TryParseChunk(payload);
                if (token != null)
                {
                    accumulated.Append(token);
                    yield return JsonSerializer.Serialize(new { chunk = token, done = false });
                }
            }
        }
    }

    private string? TryParseChunk(string payload)
    {
        try
        {
            var json = JsonSerializer.Deserialize<JsonDocument>(payload);
            var choices = json?.RootElement.GetProperty("choices");
            if (choices.HasValue && choices.Value.GetArrayLength() > 0)
            {
                var delta = choices.Value[0].GetProperty("delta");
                if (delta.TryGetProperty("content", out var contentProp))
                {
                    return contentProp.GetString();
                }
            }
        }
        catch
        {
            // Skip malformed chunks
        }
        return null;
    }

    private string BuildOpenApiPrompt(string description, string shape, OpenApiOperation operation, string? contextHistory)
    {
        var seed = Guid.NewGuid().ToString()[..8];
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var prompt = $@"Generate realistic mock JSON data for this API endpoint:

{description}

Expected JSON structure:
{shape}";

        // Include context history if available
        if (!string.IsNullOrWhiteSpace(contextHistory))
        {
            prompt += $@"

{contextHistory}";
        }

        prompt += $@"

CRITICAL RULES:
1. Return ONLY valid, complete JSON matching the structure above
2. Use realistic, varied data appropriate for the endpoint
3. Keep arrays small (1-3 items max)
4. Keep nesting shallow (max 2-3 levels)
5. Better to return less complete data than incomplete data
6. Do NOT truncate JSON mid-property
{(string.IsNullOrWhiteSpace(contextHistory) ? "" : "7. IMPORTANT: Maintain consistency with the context above (use same IDs, names, etc.)")}

Seed: {seed}
Timestamp: {timestamp}

Generate the JSON now:";

        return prompt;
    }

    /// <summary>
    /// Finds a matching operation in the OpenAPI document for the given request.
    /// </summary>
    public (OpenApiOperation? Operation, OperationType? Method) FindMatchingOperation(
        OpenApiDocument document,
        string requestPath,
        string requestMethod)
    {
        if (document.Paths == null) return (null, null);

        // First try exact match
        if (document.Paths.TryGetValue(requestPath, out var pathItem))
        {
            var operation = GetOperationFromPathItem(pathItem, requestMethod);
            if (operation != null)
            {
                var method = ParseOperationType(requestMethod);
                return (operation, method);
            }
        }

        // Try to match with path parameters (e.g., /users/{id})
        foreach (var path in document.Paths)
        {
            if (PathMatches(path.Key, requestPath))
            {
                var operation = GetOperationFromPathItem(path.Value, requestMethod);
                if (operation != null)
                {
                    var method = ParseOperationType(requestMethod);
                    return (operation, method);
                }
            }
        }

        return (null, null);
    }

    private OpenApiOperation? GetOperationFromPathItem(OpenApiPathItem pathItem, string method)
    {
        var operationType = ParseOperationType(method);
        if (operationType == null) return null;

        return pathItem.Operations.TryGetValue(operationType.Value, out var operation) ? operation : null;
    }

    private OperationType? ParseOperationType(string method)
    {
        return method.ToUpperInvariant() switch
        {
            "GET" => OperationType.Get,
            "POST" => OperationType.Post,
            "PUT" => OperationType.Put,
            "DELETE" => OperationType.Delete,
            "PATCH" => OperationType.Patch,
            "OPTIONS" => OperationType.Options,
            "HEAD" => OperationType.Head,
            "TRACE" => OperationType.Trace,
            _ => null
        };
    }

    private bool PathMatches(string specPath, string requestPath)
    {
        // Simple pattern matching for path parameters
        // e.g., /users/{id} matches /users/123

        var specSegments = specPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var requestSegments = requestPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (specSegments.Length != requestSegments.Length)
            return false;

        for (int i = 0; i < specSegments.Length; i++)
        {
            var specSegment = specSegments[i];
            var requestSegment = requestSegments[i];

            // Check if it's a parameter (surrounded by curly braces)
            if (specSegment.StartsWith('{') && specSegment.EndsWith('}'))
            {
                // Parameter segment - matches anything
                continue;
            }

            // Regular segment - must match exactly (case-insensitive)
            if (!specSegment.Equals(requestSegment, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
