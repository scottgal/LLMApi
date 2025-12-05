namespace mostlylucid.mockllmapi.Models;

/// <summary>
/// Pre-configured REST API definition with complete settings
/// </summary>
public class RestApiConfig
{
    /// <summary>
    /// Unique name/identifier for this API configuration
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable description of what this API does
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// HTTP method (GET, POST, PUT, DELETE, PATCH)
    /// </summary>
    public string Method { get; set; } = "GET";

    /// <summary>
    /// Endpoint path (can include path parameters like /users/{userId})
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// JSON shape specification for response structure
    /// </summary>
    public string? Shape { get; set; }

    /// <summary>
    /// Reference to an OpenAPI spec name (loaded via OpenAPI management)
    /// If specified, shape will be derived from the spec's schema
    /// </summary>
    public string? OpenApiSpec { get; set; }

    /// <summary>
    /// Specific operation ID from the OpenAPI spec to use for shape
    /// </summary>
    public string? OpenApiOperationId { get; set; }

    /// <summary>
    /// Shared context name for maintaining consistency across requests
    /// </summary>
    public string? ContextName { get; set; }

    /// <summary>
    /// Custom description to add to the LLM prompt
    /// </summary>
    public string? CustomDescription { get; set; }

    /// <summary>
    /// Number of cached response variants to pre-generate
    /// </summary>
    public int? CacheCount { get; set; }

    /// <summary>
    /// Tools to execute before generating the response
    /// </summary>
    public List<string> Tools { get; set; } = new();

    /// <summary>
    /// Default query parameters to include
    /// </summary>
    public Dictionary<string, string> DefaultQueryParams { get; set; } = new();

    /// <summary>
    /// Default headers to include
    /// </summary>
    public Dictionary<string, string> DefaultHeaders { get; set; } = new();

    /// <summary>
    /// Tags/groups for organizing APIs
    /// </summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// Whether this API is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether to use streaming (SSE) for this API
    /// </summary>
    public bool UseStreaming { get; set; } = false;

    /// <summary>
    /// Rate limiting delay range (e.g., "500-2000" or "max")
    /// </summary>
    public string? RateLimitDelay { get; set; }

    /// <summary>
    /// Number of completions to generate (n-completions)
    /// </summary>
    public int? NCompletions { get; set; }

    /// <summary>
    /// Error simulation configuration
    /// </summary>
    public ErrorConfig? ErrorConfig { get; set; }
}

/// <summary>
/// Extension methods for RestApiConfig
/// </summary>
public static class RestApiConfigExtensions
{
    /// <summary>
    /// Get the full URL pattern for this API
    /// </summary>
    public static string GetUrlPattern(this RestApiConfig config, string basePath = "/api/mock")
    {
        var path = config.Path.TrimStart('/');
        return $"{basePath}/{path}";
    }

    /// <summary>
    /// Check if this API matches any of the given tags
    /// </summary>
    public static bool HasAnyTag(this RestApiConfig config, params string[] tags)
    {
        if (tags == null || tags.Length == 0)
            return true;

        return config.Tags.Any(t => tags.Contains(t, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Get a summary of this API for display purposes
    /// </summary>
    public static object ToSummary(this RestApiConfig config)
    {
        return new
        {
            name = config.Name,
            method = config.Method,
            path = config.Path,
            description = config.Description,
            hasShape = !string.IsNullOrEmpty(config.Shape),
            hasOpenApiSpec = !string.IsNullOrEmpty(config.OpenApiSpec),
            contextName = config.ContextName,
            tools = config.Tools,
            tags = config.Tags,
            enabled = config.Enabled,
            useStreaming = config.UseStreaming,
            cacheCount = config.CacheCount
        };
    }
}
