namespace mostlylucid.mockllmapi.Models;

/// <summary>
///     Configuration for an OpenAPI specification to use for mock generation
/// </summary>
public class OpenApiSpecConfig
{
    /// <summary>
    ///     Unique name for this spec configuration (used for logging and management)
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    ///     URL or file path to the OpenAPI spec (YAML or JSON format)
    ///     Examples:
    ///     - "https://petstore3.swagger.io/api/v3/openapi.json"
    ///     - "https://api.example.com/swagger.json"
    ///     - "./specs/my-api.yaml"
    ///     - "C:/specs/api-definition.json"
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    ///     Base path to mount the mock endpoints (e.g., "/api/v1")
    ///     If not specified, uses the servers[0].url from the OpenAPI spec
    /// </summary>
    public string? BasePath { get; set; }

    /// <summary>
    ///     Optional context name for maintaining consistency across related API calls
    ///     Specs sharing the same context will maintain consistent IDs, names, and state
    ///     Example: "user-session-1", "e-commerce-demo"
    /// </summary>
    public string? ContextName { get; set; }

    /// <summary>
    ///     Enable streaming support for this spec (default: false)
    ///     When enabled, adds /stream suffix to all endpoints for SSE streaming
    /// </summary>
    public bool EnableStreaming { get; set; } = false;

    /// <summary>
    ///     If true, only generates endpoints for operations with specific tags
    /// </summary>
    public List<string>? IncludeTags { get; set; }

    /// <summary>
    ///     If true, excludes endpoints for operations with specific tags
    /// </summary>
    public List<string>? ExcludeTags { get; set; }

    /// <summary>
    ///     If true, only generates endpoints for specific paths (supports wildcards)
    ///     Example: ["/users/*", "/products/*"]
    /// </summary>
    public List<string>? IncludePaths { get; set; }

    /// <summary>
    ///     If true, excludes specific paths (supports wildcards)
    ///     Example: ["/admin/*", "/internal/*"]
    /// </summary>
    public List<string>? ExcludePaths { get; set; }
}