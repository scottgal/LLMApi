using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using mostlylucid.mockllmapi.Models;

namespace mostlylucid.mockllmapi.Services;

/// <summary>
///     Coordinates batched LLM requests with rate limiting strategies
///     Implements different execution patterns for n-completions
/// </summary>
public class BatchingCoordinator
{
    private readonly LlmClient _llmClient;
    private readonly ILogger<BatchingCoordinator> _logger;
    private readonly LLMockApiOptions _options;
    private readonly RateLimitService _rateLimitService;

    public BatchingCoordinator(
        LlmClient llmClient,
        RateLimitService rateLimitService,
        IOptions<LLMockApiOptions> options,
        ILogger<BatchingCoordinator> logger)
    {
        _llmClient = llmClient;
        _rateLimitService = rateLimitService;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    ///     Executes n-completions with configured rate limiting and batching strategy
    /// </summary>
    /// <param name="prompt">The prompt to generate completions for</param>
    /// <param name="n">Number of completions to generate</param>
    /// <param name="strategy">Batching strategy to use</param>
    /// <param name="delayRange">Rate limit delay range (overrides config)</param>
    /// <param name="endpointPath">Endpoint path for statistics tracking</param>
    /// <param name="request">HTTP request for backend selection</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Completion results with timing information</returns>
    public async Task<BatchCompletionResult> GetBatchedCompletionsAsync(
        string prompt,
        int n,
        RateLimitStrategy strategy,
        string? delayRange,
        string endpointPath,
        HttpRequest? request,
        CancellationToken cancellationToken = default)
    {
        // Auto-select strategy if needed
        var effectiveStrategy = _rateLimitService.SelectStrategy(n, strategy);

        _logger.LogDebug(
            "Executing {Count} completions with {Strategy} strategy for {Path}",
            n, effectiveStrategy, endpointPath);

        return effectiveStrategy switch
        {
            RateLimitStrategy.Sequential => await ExecuteSequentialAsync(
                prompt, n, delayRange, endpointPath, request, cancellationToken),
            RateLimitStrategy.Parallel => await ExecuteParallelAsync(
                prompt, n, delayRange, endpointPath, request, cancellationToken),
            RateLimitStrategy.Streaming => await ExecuteStreamingAsync(
                prompt, n, delayRange, endpointPath, request, cancellationToken),
            _ => await ExecuteSequentialAsync(
                prompt, n, delayRange, endpointPath, request, cancellationToken)
        };
    }

    /// <summary>
    ///     Sequential execution: Complete one request, delay, start next
    ///     Predictable timing but slower overall
    /// </summary>
    private async Task<BatchCompletionResult> ExecuteSequentialAsync(
        string prompt,
        int n,
        string? delayRange,
        string endpointPath,
        HttpRequest? request,
        CancellationToken cancellationToken)
    {
        var result = new BatchCompletionResult { Strategy = RateLimitStrategy.Sequential };
        var overallStopwatch = Stopwatch.StartNew();

        for (var i = 0; i < n; i++)
        {
            var itemStopwatch = Stopwatch.StartNew();

            // Execute single completion
            var completion = await _llmClient.GetCompletionAsync(prompt, cancellationToken, null, request);

            itemStopwatch.Stop();
            var requestTimeMs = itemStopwatch.ElapsedMilliseconds;

            // Record statistics
            var stats = _rateLimitService.RecordResponseTime(endpointPath, requestTimeMs);

            // Apply rate-limited delay (except for the last request)
            int? appliedDelay = null;
            if (i < n - 1)
            {
                var effectiveDelayRange = delayRange ?? _options.RateLimitDelayRange;
                appliedDelay = await _rateLimitService.ApplyRateLimitDelayAsync(
                    effectiveDelayRange, requestTimeMs, endpointPath, cancellationToken);
            }

            result.Completions.Add(new CompletionItem
            {
                Content = completion,
                RequestTimeMs = requestTimeMs,
                DelayAppliedMs = appliedDelay,
                Index = i
            });

            result.TotalRequestTimeMs += requestTimeMs;
            result.TotalDelayMs += appliedDelay ?? 0;
        }

        overallStopwatch.Stop();
        result.TotalElapsedMs = overallStopwatch.ElapsedMilliseconds;

        return result;
    }

    /// <summary>
    ///     Parallel execution: Start all requests simultaneously, stagger responses
    ///     Faster overall but more resource-intensive
    /// </summary>
    private async Task<BatchCompletionResult> ExecuteParallelAsync(
        string prompt,
        int n,
        string? delayRange,
        string endpointPath,
        HttpRequest? request,
        CancellationToken cancellationToken)
    {
        var result = new BatchCompletionResult { Strategy = RateLimitStrategy.Parallel };
        var overallStopwatch = Stopwatch.StartNew();

        // Start all requests in parallel
        var tasks = new List<Task<(string content, long timeMs, int index)>>();

        for (var i = 0; i < n; i++)
        {
            var index = i; // Capture loop variable
            var task = Task.Run(async () =>
            {
                var stopwatch = Stopwatch.StartNew();
                var completion = await _llmClient.GetCompletionAsync(prompt, cancellationToken, null, request);
                stopwatch.Stop();
                return (completion, stopwatch.ElapsedMilliseconds, index);
            }, cancellationToken);

            tasks.Add(task);
        }

        // Wait for all to complete
        var results = await Task.WhenAll(tasks);

        // Apply staggered delays and build result
        var effectiveDelayRange = delayRange ?? _options.RateLimitDelayRange;

        foreach (var (content, timeMs, index) in results.OrderBy(r => r.index))
        {
            // Record statistics
            var stats = _rateLimitService.RecordResponseTime(endpointPath, timeMs);

            // Calculate delay for this response
            var appliedDelay = _rateLimitService.CalculateDelay(effectiveDelayRange, timeMs, endpointPath);

            // Apply staggered delay based on completion order
            if (appliedDelay.HasValue && index < n - 1)
            {
                // Stagger delays to simulate rate limiting
                var staggeredDelay = appliedDelay.Value + index * 100; // Add 100ms per index
                await Task.Delay(staggeredDelay, cancellationToken);
                appliedDelay = staggeredDelay;
            }

            result.Completions.Add(new CompletionItem
            {
                Content = content,
                RequestTimeMs = timeMs,
                DelayAppliedMs = appliedDelay,
                Index = index
            });

            result.TotalRequestTimeMs += timeMs;
            result.TotalDelayMs += appliedDelay ?? 0;
        }

        overallStopwatch.Stop();
        result.TotalElapsedMs = overallStopwatch.ElapsedMilliseconds;

        return result;
    }

    /// <summary>
    ///     Streaming execution: Return results as they complete with delays
    ///     Best for real-time UIs and SSE endpoints
    /// </summary>
    private async Task<BatchCompletionResult> ExecuteStreamingAsync(
        string prompt,
        int n,
        string? delayRange,
        string endpointPath,
        HttpRequest? request,
        CancellationToken cancellationToken)
    {
        // For now, implement similar to parallel but optimized for streaming
        // In a real streaming scenario, this would yield results as they complete
        var result = new BatchCompletionResult { Strategy = RateLimitStrategy.Streaming };
        var overallStopwatch = Stopwatch.StartNew();

        var effectiveDelayRange = delayRange ?? _options.RateLimitDelayRange;

        // Use the native n-completions API if available
        var stopwatch = Stopwatch.StartNew();
        var completions = await _llmClient.GetNCompletionsAsync(prompt, n, cancellationToken, request);
        stopwatch.Stop();

        var avgTimeMs = stopwatch.ElapsedMilliseconds / n;

        // Record statistics for average time
        _rateLimitService.RecordResponseTime(endpointPath, avgTimeMs);

        // Build result with simulated streaming delays
        for (var i = 0; i < completions.Count; i++)
        {
            // Apply delay between "streamed" results
            int? appliedDelay = null;
            if (i > 0)
            {
                appliedDelay = _rateLimitService.CalculateDelay(effectiveDelayRange, avgTimeMs, endpointPath);
                if (appliedDelay.HasValue) await Task.Delay(appliedDelay.Value, cancellationToken);
            }

            result.Completions.Add(new CompletionItem
            {
                Content = completions[i],
                RequestTimeMs = avgTimeMs,
                DelayAppliedMs = appliedDelay,
                Index = i
            });

            result.TotalDelayMs += appliedDelay ?? 0;
        }

        result.TotalRequestTimeMs = stopwatch.ElapsedMilliseconds;
        overallStopwatch.Stop();
        result.TotalElapsedMs = overallStopwatch.ElapsedMilliseconds;

        return result;
    }
}

/// <summary>
///     Result of a batched completion request with timing information
/// </summary>
public class BatchCompletionResult
{
    public List<CompletionItem> Completions { get; set; } = new();
    public RateLimitStrategy Strategy { get; set; }
    public long TotalRequestTimeMs { get; set; }
    public long TotalDelayMs { get; set; }
    public long TotalElapsedMs { get; set; }
    public long AverageRequestTimeMs => Completions.Count > 0 ? TotalRequestTimeMs / Completions.Count : 0;
}

/// <summary>
///     Individual completion item with timing information
/// </summary>
public class CompletionItem
{
    public string Content { get; set; } = string.Empty;
    public long RequestTimeMs { get; set; }
    public int? DelayAppliedMs { get; set; }
    public int Index { get; set; }
}