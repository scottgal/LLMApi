using Microsoft.Extensions.DependencyInjection;

namespace mostlylucid.mockllmapi.Testing;

/// <summary>
///     Extension methods for creating and configuring HttpClient instances with mock LLM API support
/// </summary>
public static class HttpClientExtensions
{
    /// <summary>
    ///     Creates an HttpClient configured for mock LLM API testing
    /// </summary>
    /// <param name="baseAddress">Base address for the API (e.g., "http://localhost:5116")</param>
    /// <param name="baseApiPath">Base API path for mock endpoints (e.g., "/api/mock")</param>
    /// <param name="configure">Action to configure mock endpoints</param>
    /// <returns>Configured HttpClient instance</returns>
    public static HttpClient CreateMockLlmClient(
        string baseAddress,
        string? baseApiPath = "/api/mock",
        Action<MockLlmHttpHandler>? configure = null)
    {
        var handler = new MockLlmHttpHandler(baseApiPath);
        configure?.Invoke(handler);

        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(baseAddress)
        };

        return client;
    }

    /// <summary>
    ///     Creates an HttpClient configured for mock LLM API testing with a single endpoint
    /// </summary>
    /// <param name="baseAddress">Base address for the API (e.g., "http://localhost:5116")</param>
    /// <param name="pathPattern">Path pattern for the endpoint (e.g., "/users")</param>
    /// <param name="configure">Action to configure the endpoint</param>
    /// <param name="baseApiPath">Base API path for mock endpoints (e.g., "/api/mock")</param>
    /// <returns>Configured HttpClient instance</returns>
    public static HttpClient CreateMockLlmClient(
        string baseAddress,
        string pathPattern,
        Action<MockEndpointConfigBuilder> configure,
        string? baseApiPath = "/api/mock")
    {
        var handler = new MockLlmHttpHandler(baseApiPath);
        handler.ForEndpoint(pathPattern, configure);

        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(baseAddress)
        };

        return client;
    }

    /// <summary>
    ///     Adds a typed HttpClient with mock LLM API support to the service collection
    /// </summary>
    public static IHttpClientBuilder AddMockLlmHttpClient<TClient>(
        this IServiceCollection services,
        string? baseApiPath = "/api/mock",
        Action<MockLlmHttpHandler>? configure = null)
        where TClient : class
    {
        return services.AddHttpClient<TClient>()
            .ConfigurePrimaryHttpMessageHandler(() =>
            {
                var handler = new MockLlmHttpHandler(baseApiPath);
                configure?.Invoke(handler);
                return handler;
            });
    }

    /// <summary>
    ///     Adds a named HttpClient with mock LLM API support to the service collection
    /// </summary>
    public static IHttpClientBuilder AddMockLlmHttpClient(
        this IServiceCollection services,
        string name,
        string? baseApiPath = "/api/mock",
        Action<MockLlmHttpHandler>? configure = null)
    {
        return services.AddHttpClient(name)
            .ConfigurePrimaryHttpMessageHandler(() =>
            {
                var handler = new MockLlmHttpHandler(baseApiPath);
                configure?.Invoke(handler);
                return handler;
            });
    }
}