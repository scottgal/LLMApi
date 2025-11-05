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
    private readonly ContextExtractor _contextExtractor;
    private readonly OpenApiContextManager _contextManager;
    private readonly PromptBuilder _promptBuilder;
    private readonly LlmClient _llmClient;
    private readonly CacheManager _cacheManager;
    private readonly DelayHelper _delayHelper;
    private readonly ILogger<RegularRequestHandler> _logger;

    private const int MaxSchemaHeaderLength = 4000;

    public RegularRequestHandler(
        IOptions<LLMockApiOptions> options,
        ShapeExtractor shapeExtractor,
        ContextExtractor contextExtractor,
        OpenApiContextManager contextManager,
        PromptBuilder promptBuilder,
        LlmClient llmClient,
        CacheManager cacheManager,
        DelayHelper delayHelper,
        ILogger<RegularRequestHandler> logger)
    {
        _options = options.Value;
        _shapeExtractor = shapeExtractor;
        _contextExtractor = contextExtractor;
        _contextManager = contextManager;
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

        // Extract context name
        var contextName = _contextExtractor.ExtractContextName(request, body);

        // Get context history if context is specified
        var contextHistory = !string.IsNullOrWhiteSpace(contextName)
            ? _contextManager.GetContextForPrompt(contextName)
            : null;

        // Get response (with caching if requested)
        var content = await _cacheManager.GetOrFetchAsync(
            method,
            fullPathWithQuery,
            body,
            shapeInfo.Shape,
            shapeInfo.CacheCount,
            async () =>
            {
                var prompt = _promptBuilder.BuildPrompt(method, fullPathWithQuery, body, shapeInfo, streaming: false, contextHistory: contextHistory);
                var rawResponse = await _llmClient.GetCompletionAsync(prompt, cancellationToken);
                // Extract clean JSON from LLM response (might include markdown or explanatory text)
                return JsonExtractor.ExtractJson(rawResponse);
            });

        // Store in context if context name was provided
        if (!string.IsNullOrWhiteSpace(contextName))
        {
            _contextManager.AddToContext(contextName, method, fullPathWithQuery, body, content);
        }

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
}
