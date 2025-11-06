using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using mostlylucid.mockllmapi.Models;
using mostlylucid.mockllmapi.Services.Providers;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace mostlylucid.mockllmapi.Services;

/// <summary>
/// Client for communicating with LLM API
/// Supports multiple backends and providers with automatic selection
/// </summary>
public class LlmClient
{
    private readonly LLMockApiOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LlmClient> _logger;
    private readonly LlmBackendSelector _backendSelector;
    private readonly LlmProviderFactory _providerFactory;
    private readonly ResiliencePipeline _resiliencePipeline;

    public LlmClient(
        IOptions<LLMockApiOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<LlmClient> logger,
        LlmBackendSelector backendSelector,
        LlmProviderFactory providerFactory)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _backendSelector = backendSelector;
        _providerFactory = providerFactory;
        _resiliencePipeline = BuildResiliencePipeline();
    }

    /// <summary>
    /// Builds a resilience pipeline with retry and circuit breaker policies
    /// </summary>
    private ResiliencePipeline BuildResiliencePipeline()
    {
        var pipelineBuilder = new ResiliencePipelineBuilder();

        // Add retry policy with exponential backoff
        if (_options.EnableRetryPolicy && _options.MaxRetryAttempts > 0)
        {
            pipelineBuilder.AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = _options.MaxRetryAttempts,
                Delay = TimeSpan.FromSeconds(_options.RetryBaseDelaySeconds),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>(),
                OnRetry = args =>
                {
                    var exception = args.Outcome.Exception;

                    if (exception != null)
                    {
                        _logger.LogWarning(
                            exception,
                            "LLM request failed (attempt {Attempt}/{MaxAttempts}). Retrying in {Delay}ms. Error: {ErrorMessage}",
                            args.AttemptNumber + 1,
                            _options.MaxRetryAttempts + 1,
                            args.RetryDelay.TotalMilliseconds,
                            exception.Message);
                    }

                    return default;
                }
            });
        }

        // Add circuit breaker policy
        if (_options.EnableCircuitBreaker)
        {
            pipelineBuilder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 1.0, // Open after consecutive failures (not ratio-based)
                MinimumThroughput = _options.CircuitBreakerFailureThreshold,
                SamplingDuration = TimeSpan.FromSeconds(60),
                BreakDuration = TimeSpan.FromSeconds(_options.CircuitBreakerDurationSeconds),
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>(),
                OnOpened = args =>
                {
                    _logger.LogError(
                        "Circuit breaker OPENED after {FailureCount} consecutive failures. " +
                        "All LLM requests will be rejected for {BreakDuration} seconds",
                        _options.CircuitBreakerFailureThreshold,
                        _options.CircuitBreakerDurationSeconds);
                    return default;
                },
                OnClosed = args =>
                {
                    _logger.LogInformation(
                        "Circuit breaker CLOSED. LLM requests will be attempted normally");
                    return default;
                },
                OnHalfOpened = args =>
                {
                    _logger.LogInformation(
                        "Circuit breaker HALF-OPEN. Testing if LLM service has recovered");
                    return default;
                }
            });
        }

        return pipelineBuilder.Build();
    }

    /// <summary>
    /// Sends a non-streaming chat completion request
    /// Supports per-request backend selection via HttpRequest
    /// </summary>
    public virtual async Task<string> GetCompletionAsync(
        string prompt,
        CancellationToken cancellationToken = default,
        int? maxTokens = null,
        HttpRequest? request = null)
    {
        var backend = _backendSelector.SelectBackend(request);
        if (backend == null)
        {
            throw new InvalidOperationException("No LLM backend available");
        }

        return await GetCompletionWithBackendAsync(backend, prompt, cancellationToken, maxTokens);
    }

    /// <summary>
    /// Sends a non-streaming chat completion request with specific backend
    /// </summary>
    public virtual async Task<string> GetCompletionAsync(
        string prompt,
        string? backendName,
        CancellationToken cancellationToken = default,
        int? maxTokens = null)
    {
        LlmBackendConfig? backend = null;

        if (!string.IsNullOrWhiteSpace(backendName))
        {
            backend = _backendSelector.GetBackendByName(backendName);
            if (backend == null)
            {
                _logger.LogWarning("Backend '{BackendName}' not found or disabled, using default selection", backendName);
                backend = _backendSelector.SelectBackend();
            }
        }
        else
        {
            backend = _backendSelector.SelectBackend();
        }

        if (backend == null)
        {
            throw new InvalidOperationException("No LLM backend available");
        }

        return await GetCompletionWithBackendAsync(backend, prompt, cancellationToken, maxTokens);
    }

    /// <summary>
    /// Sends a non-streaming chat completion request with load balancing across multiple backends
    /// </summary>
    public virtual async Task<string> GetCompletionAsync(
        string prompt,
        string[]? backendNames,
        CancellationToken cancellationToken = default,
        int? maxTokens = null)
    {
        var backend = backendNames != null && backendNames.Length > 0
            ? _backendSelector.SelectFromBackends(backendNames)
            : _backendSelector.SelectBackend();

        if (backend == null)
        {
            throw new InvalidOperationException("No LLM backend available");
        }

        return await GetCompletionWithBackendAsync(backend, prompt, cancellationToken, maxTokens);
    }

    /// <summary>
    /// Internal method to execute completion with a specific backend configuration
    /// </summary>
    private async Task<string> GetCompletionWithBackendAsync(
        LlmBackendConfig backend,
        string prompt,
        CancellationToken cancellationToken,
        int? maxTokens)
    {
        var provider = _providerFactory.GetProvider(backend.Provider);
        using var client = CreateHttpClient(backend.BaseUrl);
        provider.ConfigureClient(client, backend.ApiKey);

        // Use backend-specific MaxTokens if configured, otherwise use parameter value
        var effectiveMaxTokens = backend.MaxTokens ?? maxTokens;

        // Execute with resilience pipeline if policies are enabled
        return (_options.EnableRetryPolicy || _options.EnableCircuitBreaker)
            ? await _resiliencePipeline.ExecuteAsync(async ct =>
            {
                return await provider.GetCompletionAsync(
                    client, prompt, backend.ModelName, _options.Temperature, effectiveMaxTokens, ct);
            }, cancellationToken)
            : await provider.GetCompletionAsync(
                client, prompt, backend.ModelName, _options.Temperature, effectiveMaxTokens, cancellationToken);
    }

    private async Task<HttpResponseMessage> ExecuteRequestAsync(HttpClient client, object payload, CancellationToken cancellationToken)
    {
        using var httpReq = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
        httpReq.Content = JsonContent.Create(payload);
        return await client.SendAsync(httpReq, cancellationToken);
    }



    /// <summary>
    /// Sends a streaming chat completion request
    /// Supports per-request backend selection via HttpRequest
    /// </summary>
    public async Task<HttpResponseMessage> GetStreamingCompletionAsync(
        string prompt,
        CancellationToken cancellationToken = default,
        HttpRequest? request = null)
    {
        var backend = _backendSelector.SelectBackend(request);
        if (backend == null)
        {
            throw new InvalidOperationException("No LLM backend available");
        }

        var provider = _providerFactory.GetProvider(backend.Provider);
        var client = CreateHttpClient(backend.BaseUrl);
        provider.ConfigureClient(client, backend.ApiKey);

        // Execute with resilience pipeline if policies are enabled
        return (_options.EnableRetryPolicy || _options.EnableCircuitBreaker)
            ? await _resiliencePipeline.ExecuteAsync(async ct =>
            {
                return await provider.GetStreamingCompletionAsync(
                    client, prompt, backend.ModelName, _options.Temperature, ct);
            }, cancellationToken)
            : await provider.GetStreamingCompletionAsync(
                client, prompt, backend.ModelName, _options.Temperature, cancellationToken);
    }

    private async Task<HttpResponseMessage> ExecuteStreamingRequestAsync(HttpClient client, object payload, CancellationToken cancellationToken)
    {
        var httpReq = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(payload)
        };
        return await client.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    /// <summary>
    /// Requests multiple completions in a single call and returns all message contents.
    /// Supports per-request backend selection via HttpRequest
    /// </summary>
    public async Task<List<string>> GetNCompletionsAsync(
        string prompt,
        int n,
        CancellationToken cancellationToken = default,
        HttpRequest? request = null)
    {
        var backend = _backendSelector.SelectBackend(request);
        if (backend == null)
        {
            throw new InvalidOperationException("No LLM backend available");
        }

        return await GetNCompletionsWithBackendAsync(backend, prompt, n, cancellationToken);
    }

    /// <summary>
    /// Requests multiple completions with specific backend
    /// </summary>
    public async Task<List<string>> GetNCompletionsAsync(
        string prompt,
        int n,
        string? backendName,
        CancellationToken cancellationToken = default)
    {
        LlmBackendConfig? backend = null;

        if (!string.IsNullOrWhiteSpace(backendName))
        {
            backend = _backendSelector.GetBackendByName(backendName);
            if (backend == null)
            {
                _logger.LogWarning("Backend '{BackendName}' not found or disabled, using default selection", backendName);
                backend = _backendSelector.SelectBackend();
            }
        }
        else
        {
            backend = _backendSelector.SelectBackend();
        }

        if (backend == null)
        {
            throw new InvalidOperationException("No LLM backend available");
        }

        return await GetNCompletionsWithBackendAsync(backend, prompt, n, cancellationToken);
    }

    /// <summary>
    /// Requests multiple completions with load balancing across multiple backends
    /// </summary>
    public async Task<List<string>> GetNCompletionsAsync(
        string prompt,
        int n,
        string[]? backendNames,
        CancellationToken cancellationToken = default)
    {
        var backend = backendNames != null && backendNames.Length > 0
            ? _backendSelector.SelectFromBackends(backendNames)
            : _backendSelector.SelectBackend();

        if (backend == null)
        {
            throw new InvalidOperationException("No LLM backend available");
        }

        return await GetNCompletionsWithBackendAsync(backend, prompt, n, cancellationToken);
    }

    /// <summary>
    /// Internal method to execute N completions with a specific backend configuration
    /// </summary>
    private async Task<List<string>> GetNCompletionsWithBackendAsync(
        LlmBackendConfig backend,
        string prompt,
        int n,
        CancellationToken cancellationToken)
    {
        var provider = _providerFactory.GetProvider(backend.Provider);
        using var client = CreateHttpClient(backend.BaseUrl);
        provider.ConfigureClient(client, backend.ApiKey);

        // Execute with resilience pipeline if policies are enabled
        return (_options.EnableRetryPolicy || _options.EnableCircuitBreaker)
            ? await _resiliencePipeline.ExecuteAsync(async ct =>
            {
                return await provider.GetNCompletionsAsync(
                    client, prompt, backend.ModelName, _options.Temperature, n, ct);
            }, cancellationToken)
            : await provider.GetNCompletionsAsync(
                client, prompt, backend.ModelName, _options.Temperature, n, cancellationToken);
    }

    /// <summary>
    /// Builds a chat request object for the LLM API
    /// </summary>
    public object BuildChatRequest(string prompt, string modelName, bool stream, int? n = null, int? maxTokens = null)
    {
        return new
        {
            model = modelName,
            stream,
            temperature = _options.Temperature,
            n,
            max_tokens = maxTokens,
            messages = new[] { new { role = "user", content = prompt } }
        };
    }

    /// <summary>
    /// Creates an HttpClient configured for the specified LLM backend
    /// </summary>
    private HttpClient CreateHttpClient(string baseUrl)
    {
        var client = _httpClientFactory.CreateClient("LLMockApi");
        client.BaseAddress = new Uri(baseUrl);
        client.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        return client;
    }
}
