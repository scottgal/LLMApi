namespace mostlylucid.mockllmapi.Testing;

/// <summary>
///     Configuration for a mock endpoint response
/// </summary>
public class MockEndpointConfig
{
    /// <summary>
    ///     The endpoint path pattern to match (e.g., "/api/users", "/api/posts/*")
    /// </summary>
    public string PathPattern { get; set; } = string.Empty;

    /// <summary>
    ///     HTTP method to match (optional, defaults to any method)
    /// </summary>
    public string? Method { get; set; }

    /// <summary>
    ///     JSON shape/schema for the response
    /// </summary>
    public string? Shape { get; set; }

    /// <summary>
    ///     Error configuration (status code, message, details)
    /// </summary>
    public ErrorConfiguration? Error { get; set; }

    /// <summary>
    ///     Number of cached variants to generate (default: 1)
    /// </summary>
    public int? CacheSize { get; set; }

    /// <summary>
    ///     Backend to use for this request (e.g., "ollama", "openai")
    /// </summary>
    public string? Backend { get; set; }

    /// <summary>
    ///     Custom headers to add to the request
    /// </summary>
    public Dictionary<string, string> Headers { get; } = new();

    /// <summary>
    ///     Query parameters to add to the request
    /// </summary>
    public Dictionary<string, string> QueryParameters { get; } = new();

    /// <summary>
    ///     Whether to use streaming endpoint
    /// </summary>
    public bool UseStreaming { get; set; }

    /// <summary>
    ///     SSE mode for streaming (LlmTokens, CompleteObjects, ArrayItems)
    /// </summary>
    public string? SseMode { get; set; }

    /// <summary>
    ///     Enable continuous streaming
    /// </summary>
    public bool? Continuous { get; set; }

    /// <summary>
    ///     Interval for continuous streaming in milliseconds
    /// </summary>
    public int? ContinuousInterval { get; set; }

    /// <summary>
    ///     Auto-chunking enabled
    /// </summary>
    public bool? AutoChunk { get; set; }

    /// <summary>
    ///     Maximum items per response
    /// </summary>
    public int? MaxItems { get; set; }

    internal bool Matches(HttpRequestMessage request)
    {
        // Check method if specified
        if (Method != null && !string.Equals(request.Method.Method, Method, StringComparison.OrdinalIgnoreCase))
            return false;

        // Check path pattern
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;

        // Simple wildcard matching
        if (PathPattern.EndsWith("*"))
        {
            var prefix = PathPattern.TrimEnd('*');
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(path, PathPattern, StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(PathPattern, StringComparison.OrdinalIgnoreCase);
    }

    internal void ApplyToRequest(HttpRequestMessage request)
    {
        // Apply headers
        foreach (var header in Headers) request.Headers.TryAddWithoutValidation(header.Key, header.Value);

        // Apply backend selection
        if (Backend != null) request.Headers.TryAddWithoutValidation("X-LLM-Backend", Backend);

        // Apply error configuration
        if (Error != null)
        {
            request.Headers.TryAddWithoutValidation("X-Error-Code", Error.StatusCode.ToString());
            if (Error.Message != null) request.Headers.TryAddWithoutValidation("X-Error-Message", Error.Message);
            if (Error.Details != null) request.Headers.TryAddWithoutValidation("X-Error-Details", Error.Details);
        }

        // Build query string
        var queryParams = new List<string>();

        if (Shape != null) queryParams.Add($"shape={Uri.EscapeDataString(Shape)}");

        if (CacheSize.HasValue) queryParams.Add($"cache={CacheSize.Value}");

        if (Backend != null) queryParams.Add($"backend={Uri.EscapeDataString(Backend)}");

        if (SseMode != null) queryParams.Add($"sseMode={Uri.EscapeDataString(SseMode)}");

        if (Continuous.HasValue) queryParams.Add($"continuous={Continuous.Value.ToString().ToLower()}");

        if (ContinuousInterval.HasValue) queryParams.Add($"interval={ContinuousInterval.Value}");

        if (AutoChunk.HasValue) queryParams.Add($"autoChunk={AutoChunk.Value.ToString().ToLower()}");

        if (MaxItems.HasValue) queryParams.Add($"maxItems={MaxItems.Value}");

        // Add custom query parameters
        foreach (var param in QueryParameters)
            queryParams.Add($"{Uri.EscapeDataString(param.Key)}={Uri.EscapeDataString(param.Value)}");

        // Apply query string to request
        if (queryParams.Count > 0 && request.RequestUri != null)
        {
            var uriBuilder = new UriBuilder(request.RequestUri);
            var existingQuery = uriBuilder.Query.TrimStart('?');
            var newQuery = string.Join("&", queryParams);

            uriBuilder.Query = string.IsNullOrEmpty(existingQuery)
                ? newQuery
                : $"{existingQuery}&{newQuery}";

            request.RequestUri = uriBuilder.Uri;
        }
    }
}

/// <summary>
///     Error configuration for mock responses
/// </summary>
public class ErrorConfiguration
{
    public ErrorConfiguration(int statusCode, string? message = null, string? details = null)
    {
        StatusCode = statusCode;
        Message = message;
        Details = details;
    }

    /// <summary>
    ///     HTTP status code
    /// </summary>
    public int StatusCode { get; set; }

    /// <summary>
    ///     Error message (optional)
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    ///     Error details (optional)
    /// </summary>
    public string? Details { get; set; }
}