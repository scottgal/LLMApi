using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using mostlylucid.mockllmapi.Models;
using mostlylucid.mockllmapi.Services;

namespace mostlylucid.mockllmapi.RequestHandlers;

/// <summary>
/// Handles non-streaming mock API requests
/// </summary>
public class RegularRequestHandler
{
    private readonly LLMockApiOptions _options;
    private readonly ShapeExtractor _shapeExtractor;
    private readonly PromptBuilder _promptBuilder;
    private readonly LlmClient _llmClient;
    private readonly CacheManager _cacheManager;
    private readonly DelayHelper _delayHelper;
    private readonly ILogger<RegularRequestHandler> _logger;

    private const int MaxSchemaHeaderLength = 4000;

    public RegularRequestHandler(
        IOptions<LLMockApiOptions> options,
        ShapeExtractor shapeExtractor,
        PromptBuilder promptBuilder,
        LlmClient llmClient,
        CacheManager cacheManager,
        DelayHelper delayHelper,
        ILogger<RegularRequestHandler> logger)
    {
        _options = options.Value;
        _shapeExtractor = shapeExtractor;
        _promptBuilder = promptBuilder;
        _llmClient = llmClient;
        _cacheManager = cacheManager;
        _delayHelper = delayHelper;
        _logger = logger;
    }

    /// <summary>
    /// Handles a regular (non-streaming) request
    /// </summary>
    public async Task<string> HandleRequestAsync(
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

        // Get response (with caching if requested)
        var content = await _cacheManager.GetOrFetchAsync(
            method,
            fullPathWithQuery,
            body,
            shapeInfo.Shape,
            shapeInfo.CacheCount,
            async () =>
            {
                var prompt = _promptBuilder.BuildPrompt(method, fullPathWithQuery, body, shapeInfo, streaming: false);
                var rawResponse = await _llmClient.GetCompletionAsync(prompt, cancellationToken);
                // Extract clean JSON from LLM response (might include markdown or explanatory text)
                return ExtractJson(rawResponse);
            });

        // Optionally include schema in header
        TryAddSchemaHeader(context, request, shapeInfo.Shape);

        return content;
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
