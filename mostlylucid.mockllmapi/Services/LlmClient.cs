using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using mostlylucid.mockllmapi.Models;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace mostlylucid.mockllmapi.Services;

/// <summary>
/// Client for communicating with LLM API using Microsoft.Extensions.AI abstractions
/// </summary>
public class LlmClient
{
    private readonly LLMockApiOptions _options;
    private readonly IChatClient _chatClient;
    private readonly ILogger<LlmClient> _logger;
    private readonly ResiliencePipeline<string> _resiliencePipeline;

    public LlmClient(
        IOptions<LLMockApiOptions> options,
        IChatClient chatClient,
        ILogger<LlmClient> logger)
    {
        _options = options.Value;
        _chatClient = chatClient;
        _logger = logger;
        _resiliencePipeline = BuildResiliencePipeline();
    }

    /// <summary>
    /// Builds a resilience pipeline with retry and circuit breaker policies
    /// </summary>
    private ResiliencePipeline<string> BuildResiliencePipeline()
    {
        var pipelineBuilder = new ResiliencePipelineBuilder<string>();

        // Add retry policy with exponential backoff
        if (_options.EnableRetryPolicy && _options.MaxRetryAttempts > 0)
        {
            pipelineBuilder.AddRetry(new RetryStrategyOptions<string>
            {
                MaxRetryAttempts = _options.MaxRetryAttempts,
                Delay = TimeSpan.FromSeconds(_options.RetryBaseDelaySeconds),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder<string>()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .Handle<Exception>(),
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
            pipelineBuilder.AddCircuitBreaker(new CircuitBreakerStrategyOptions<string>
            {
                FailureRatio = 1.0, // Open after consecutive failures (not ratio-based)
                MinimumThroughput = _options.CircuitBreakerFailureThreshold,
                SamplingDuration = TimeSpan.FromSeconds(60),
                BreakDuration = TimeSpan.FromSeconds(_options.CircuitBreakerDurationSeconds),
                ShouldHandle = new PredicateBuilder<string>()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .Handle<Exception>(),
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
        var chatOptions = new ChatOptions
        {
            ModelId = _options.ModelName,
            Temperature = (float?)_options.Temperature,
            MaxOutputTokens = maxTokens
        };

        // Execute with resilience pipeline if policies are enabled
        var result = (_options.EnableRetryPolicy || _options.EnableCircuitBreaker)
            ? await _resiliencePipeline.ExecuteAsync(async ct =>
            {
                var response = await _chatClient.GetResponseAsync(prompt, chatOptions, ct);
                return response.Text ?? "{}";
            }, cancellationToken)
            : await ExecuteChatRequestAsync(prompt, chatOptions, cancellationToken);

        return result;
    }

    private async Task<string> ExecuteChatRequestAsync(string prompt, ChatOptions chatOptions, CancellationToken cancellationToken)
    {
        var response = await _chatClient.GetResponseAsync(prompt, chatOptions, cancellationToken);
        return response.Text ?? "{}";
    }

    /// <summary>
    /// Sends a streaming chat completion request
    /// Returns an async enumerable of response chunks
    /// </summary>
    public async IAsyncEnumerable<string> GetStreamingCompletionAsync(string prompt, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var chatOptions = new ChatOptions
        {
            ModelId = _options.ModelName,
            Temperature = (float?)_options.Temperature
        };

        await foreach (var update in _chatClient.GetStreamingResponseAsync(prompt, chatOptions, cancellationToken))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return update.Text;
            }
        }
    }

    /// <summary>
    /// Requests multiple completions in a single call and returns all message contents.
    /// Note: Not all providers support generating multiple completions in a single call.
    /// This method will make N separate requests if needed.
    /// </summary>
    public async Task<List<string>> GetNCompletionsAsync(string prompt, int n, CancellationToken cancellationToken = default)
    {
        var chatOptions = new ChatOptions
        {
            ModelId = _options.ModelName,
            Temperature = (float?)_options.Temperature
        };

        var results = new List<string>();

        // Generate N completions (may require N separate requests depending on provider)
        for (int i = 0; i < n; i++)
        {
            var result = (_options.EnableRetryPolicy || _options.EnableCircuitBreaker)
                ? await _resiliencePipeline.ExecuteAsync(async ct =>
                {
                    var response = await _chatClient.GetResponseAsync(prompt, chatOptions, ct);
                    return response.Text ?? "{}";
                }, cancellationToken)
                : await ExecuteChatRequestAsync(prompt, chatOptions, cancellationToken);

            results.Add(result);
        }

        return results;
    }
}
