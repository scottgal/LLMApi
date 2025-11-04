using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using mostlylucid.mockllmapi.Models;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace mostlylucid.mockllmapi.Services;

/// <summary>
/// Client for communicating with LLM API
/// </summary>
public class LlmClient
{
    private readonly LLMockApiOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LlmClient> _logger;
    private readonly ResiliencePipeline<HttpResponseMessage> _resiliencePipeline;

    public LlmClient(
        IOptions<LLMockApiOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<LlmClient> logger)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _resiliencePipeline = BuildResiliencePipeline();
    }

    /// <summary>
    /// Builds a resilience pipeline with retry and circuit breaker policies
    /// </summary>
    private ResiliencePipeline<HttpResponseMessage> BuildResiliencePipeline()
    {
        var pipelineBuilder = new ResiliencePipelineBuilder<HttpResponseMessage>();

        // Add retry policy with exponential backoff
        if (_options.EnableRetryPolicy && _options.MaxRetryAttempts > 0)
        {
            pipelineBuilder.AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = _options.MaxRetryAttempts,
                Delay = TimeSpan.FromSeconds(_options.RetryBaseDelaySeconds),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .HandleResult(response => !response.IsSuccessStatusCode),
                OnRetry = args =>
                {
                    var exception = args.Outcome.Exception;
                    var statusCode = args.Outcome.Result?.StatusCode;

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
                    else if (statusCode.HasValue)
                    {
                        _logger.LogWarning(
                            "LLM request returned {StatusCode} (attempt {Attempt}/{MaxAttempts}). Retrying in {Delay}ms",
                            statusCode,
                            args.AttemptNumber + 1,
                            _options.MaxRetryAttempts + 1,
                            args.RetryDelay.TotalMilliseconds);
                    }

                    return default;
                }
            });
        }

        // Add circuit breaker policy
        if (_options.EnableCircuitBreaker)
        {
            pipelineBuilder.AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
            {
                FailureRatio = 1.0, // Open after consecutive failures (not ratio-based)
                MinimumThroughput = _options.CircuitBreakerFailureThreshold,
                SamplingDuration = TimeSpan.FromSeconds(60),
                BreakDuration = TimeSpan.FromSeconds(_options.CircuitBreakerDurationSeconds),
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .HandleResult(response => !response.IsSuccessStatusCode),
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
    /// </summary>
    public virtual async Task<string> GetCompletionAsync(string prompt, CancellationToken cancellationToken = default, int? maxTokens = null)
    {
        using var client = CreateHttpClient();
        var payload = BuildChatRequest(prompt, stream: false, maxTokens: maxTokens);

        // Execute with resilience pipeline if policies are enabled
        var httpRes = (_options.EnableRetryPolicy || _options.EnableCircuitBreaker)
            ? await _resiliencePipeline.ExecuteAsync(async ct =>
            {
                // Create a NEW HttpRequestMessage for each attempt (including retries)
                using var httpReq = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
                httpReq.Content = JsonContent.Create(payload);
                return await client.SendAsync(httpReq, ct);
            }, cancellationToken)
            : await ExecuteRequestAsync(client, payload, cancellationToken);

        using (httpRes)
        {
            httpRes.EnsureSuccessStatusCode();
            var result = await httpRes.Content.ReadFromJsonAsync<ChatCompletionLite>(cancellationToken: cancellationToken);
            return result.FirstContent ?? "{}";
        }
    }

    private async Task<HttpResponseMessage> ExecuteRequestAsync(HttpClient client, object payload, CancellationToken cancellationToken)
    {
        using var httpReq = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
        httpReq.Content = JsonContent.Create(payload);
        return await client.SendAsync(httpReq, cancellationToken);
    }



    /// <summary>
    /// Sends a streaming chat completion request
    /// </summary>
    public async Task<HttpResponseMessage> GetStreamingCompletionAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var client = CreateHttpClient();
        var payload = BuildChatRequest(prompt, stream: true);

        // Execute with resilience pipeline if policies are enabled
        var httpRes = (_options.EnableRetryPolicy || _options.EnableCircuitBreaker)
            ? await _resiliencePipeline.ExecuteAsync(async ct =>
            {
                // Create a NEW HttpRequestMessage for each attempt (including retries)
                var httpReq = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
                {
                    Content = JsonContent.Create(payload)
                };
                return await client.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, ct);
            }, cancellationToken)
            : await ExecuteStreamingRequestAsync(client, payload, cancellationToken);

        httpRes.EnsureSuccessStatusCode();
        return httpRes;
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
    /// </summary>
    public async Task<List<string>> GetNCompletionsAsync(string prompt, int n, CancellationToken cancellationToken = default)
    {
        using var client = CreateHttpClient();
        var payload = BuildChatRequest(prompt, stream: false, n: n);

        // Execute with resilience pipeline if policies are enabled
        var httpRes = (_options.EnableRetryPolicy || _options.EnableCircuitBreaker)
            ? await _resiliencePipeline.ExecuteAsync(async ct =>
            {
                // Create a NEW HttpRequestMessage for each attempt (including retries)
                using var httpReq = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
                {
                    Content = JsonContent.Create(payload)
                };
                return await client.SendAsync(httpReq, ct);
            }, cancellationToken)
            : await ExecuteRequestAsync(client, payload, cancellationToken);

        using (httpRes)
        {
            httpRes.EnsureSuccessStatusCode();
            var result = await httpRes.Content.ReadFromJsonAsync<ChatCompletionLite>(cancellationToken: cancellationToken);
            var list = new List<string>();
            foreach (var c in result.Choices)
            {
                if (!string.IsNullOrEmpty(c.Message.Content))
                    list.Add(c.Message.Content);
            }
            return list;
        }
    }

    /// <summary>
    /// Builds a chat request object for the LLM API
    /// </summary>
    public object BuildChatRequest(string prompt, bool stream, int? n = null, int? maxTokens = null)
    {
        return new
        {
            model = _options.ModelName,
            stream,
            temperature = _options.Temperature,
            n,
            max_tokens = maxTokens,
            messages = new[] { new { role = "user", content = prompt } }
        };
    }

    /// <summary>
    /// Creates an HttpClient configured for the LLM API
    /// </summary>
    private HttpClient CreateHttpClient()
    {
        var client = _httpClientFactory.CreateClient("LLMockApi");
        client.BaseAddress = new Uri(_options.BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        return client;
    }
}
