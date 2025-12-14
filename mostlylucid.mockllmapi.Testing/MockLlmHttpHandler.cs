using System.Text.Json;

namespace mostlylucid.mockllmapi.Testing;

/// <summary>
///     HTTP message handler that intercepts requests and applies mock configuration
/// </summary>
public class MockLlmHttpHandler : DelegatingHandler
{
    private readonly string? _baseApiPath;
    private readonly List<MockEndpointConfig> _endpointConfigs = new();

    /// <summary>
    ///     Creates a new MockLlmHttpHandler
    /// </summary>
    /// <param name="baseApiPath">
    ///     Base API path for mock endpoints (e.g., "/api/mock"). If specified, this path will be
    ///     prepended to all requests.
    /// </param>
    /// <param name="innerHandler">Inner HTTP handler (optional, uses HttpClientHandler by default)</param>
    public MockLlmHttpHandler(string? baseApiPath = null, HttpMessageHandler? innerHandler = null)
    {
        _baseApiPath = baseApiPath?.TrimEnd('/');
        InnerHandler = innerHandler ?? new HttpClientHandler();
    }

    /// <summary>
    ///     Adds an endpoint configuration
    /// </summary>
    public MockLlmHttpHandler AddEndpoint(MockEndpointConfig config)
    {
        _endpointConfigs.Add(config);
        return this;
    }

    /// <summary>
    ///     Adds an endpoint configuration using a fluent builder
    /// </summary>
    public MockLlmHttpHandler ForEndpoint(string pathPattern, Action<MockEndpointConfigBuilder> configure)
    {
        var builder = new MockEndpointConfigBuilder(pathPattern);
        configure(builder);
        _endpointConfigs.Add(builder.Build());
        return this;
    }

    /// <summary>
    ///     Removes all endpoint configurations
    /// </summary>
    public MockLlmHttpHandler ClearEndpoints()
    {
        _endpointConfigs.Clear();
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Prepend base API path if configured
        if (_baseApiPath != null && request.RequestUri != null)
        {
            var originalPath = request.RequestUri.AbsolutePath;
            var newPath = $"{_baseApiPath}{originalPath}";

            var uriBuilder = new UriBuilder(request.RequestUri)
            {
                Path = newPath
            };

            request.RequestUri = uriBuilder.Uri;
        }

        // Find matching endpoint configuration
        var matchingConfig = _endpointConfigs.FirstOrDefault(c => c.Matches(request));

        // Apply configuration if found
        if (matchingConfig != null)
        {
            // Modify path for streaming if requested
            if (matchingConfig.UseStreaming && request.RequestUri != null)
            {
                var path = request.RequestUri.AbsolutePath;
                if (!path.Contains("/stream/"))
                {
                    // Insert /stream/ after the base path
                    if (_baseApiPath != null && path.StartsWith(_baseApiPath))
                    {
                        path = path.Insert(_baseApiPath.Length, "/stream");
                    }
                    else
                    {
                        // Find the position after the first path segment
                        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                        if (segments.Length > 0) path = $"/{segments[0]}/stream/{string.Join("/", segments.Skip(1))}";
                    }

                    var uriBuilder = new UriBuilder(request.RequestUri)
                    {
                        Path = path
                    };
                    request.RequestUri = uriBuilder.Uri;
                }
            }

            matchingConfig.ApplyToRequest(request);
        }

        // Continue with the actual HTTP request
        return await base.SendAsync(request, cancellationToken);
    }
}

/// <summary>
///     Fluent builder for MockEndpointConfig
/// </summary>
public class MockEndpointConfigBuilder
{
    private readonly MockEndpointConfig _config;

    internal MockEndpointConfigBuilder(string pathPattern)
    {
        _config = new MockEndpointConfig { PathPattern = pathPattern };
    }

    /// <summary>
    ///     Specifies the HTTP method to match (GET, POST, etc.)
    /// </summary>
    public MockEndpointConfigBuilder WithMethod(string method)
    {
        _config.Method = method;
        return this;
    }

    /// <summary>
    ///     Specifies the JSON shape/schema for the response
    /// </summary>
    public MockEndpointConfigBuilder WithShape(string shape)
    {
        _config.Shape = shape;
        return this;
    }

    /// <summary>
    ///     Specifies the JSON shape/schema using an object that will be serialized
    /// </summary>
    public MockEndpointConfigBuilder WithShape(object shapeObject)
    {
        _config.Shape = JsonSerializer.Serialize(shapeObject);
        return this;
    }

    /// <summary>
    ///     Configures an error response
    /// </summary>
    public MockEndpointConfigBuilder WithError(int statusCode, string? message = null, string? details = null)
    {
        _config.Error = new ErrorConfiguration(statusCode, message, details);
        return this;
    }

    /// <summary>
    ///     Sets the number of cached variants to generate
    /// </summary>
    public MockEndpointConfigBuilder WithCache(int cacheSize)
    {
        _config.CacheSize = cacheSize;
        return this;
    }

    /// <summary>
    ///     Specifies which LLM backend to use
    /// </summary>
    public MockEndpointConfigBuilder WithBackend(string backend)
    {
        _config.Backend = backend;
        return this;
    }

    /// <summary>
    ///     Adds a custom header to the request
    /// </summary>
    public MockEndpointConfigBuilder WithHeader(string name, string value)
    {
        _config.Headers[name] = value;
        return this;
    }

    /// <summary>
    ///     Adds a query parameter to the request
    /// </summary>
    public MockEndpointConfigBuilder WithQueryParameter(string name, string value)
    {
        _config.QueryParameters[name] = value;
        return this;
    }

    /// <summary>
    ///     Enables streaming for this endpoint
    /// </summary>
    public MockEndpointConfigBuilder WithStreaming(bool enabled = true)
    {
        _config.UseStreaming = enabled;
        return this;
    }

    /// <summary>
    ///     Sets the SSE mode for streaming (LlmTokens, CompleteObjects, ArrayItems)
    /// </summary>
    public MockEndpointConfigBuilder WithSseMode(string mode)
    {
        _config.SseMode = mode;
        return this;
    }

    /// <summary>
    ///     Enables continuous streaming
    /// </summary>
    public MockEndpointConfigBuilder WithContinuousStreaming(bool enabled = true, int? intervalMs = null)
    {
        _config.Continuous = enabled;
        if (intervalMs.HasValue) _config.ContinuousInterval = intervalMs.Value;
        return this;
    }

    /// <summary>
    ///     Configures auto-chunking for large responses
    /// </summary>
    public MockEndpointConfigBuilder WithAutoChunking(bool enabled = true)
    {
        _config.AutoChunk = enabled;
        return this;
    }

    /// <summary>
    ///     Sets maximum items per response
    /// </summary>
    public MockEndpointConfigBuilder WithMaxItems(int maxItems)
    {
        _config.MaxItems = maxItems;
        return this;
    }

    internal MockEndpointConfig Build()
    {
        return _config;
    }
}