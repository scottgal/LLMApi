using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using mostlylucid.mockllmapi.Models;

namespace mostlylucid.mockllmapi.Services;

/// <summary>
///     Service for rate limiting simulation with per-endpoint statistics tracking
///     Tracks LLM response times and calculates appropriate delays to simulate rate-limited APIs
/// </summary>
public class RateLimitService
{
    // Per-endpoint statistics: endpoint path -> response time history
    private readonly ConcurrentDictionary<string, EndpointStats> _endpointStats = new();
    private readonly LLMockApiOptions _options;
    private readonly Random _random = new();

    public RateLimitService(IOptions<LLMockApiOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    ///     Records a response time for an endpoint and returns statistics
    /// </summary>
    /// <param name="endpointPath">The endpoint path (e.g., "/api/mock/users")</param>
    /// <param name="responseTimeMs">Response time in milliseconds</param>
    /// <returns>Updated statistics for the endpoint</returns>
    public EndpointStats RecordResponseTime(string endpointPath, long responseTimeMs)
    {
        if (!_options.EnableRateLimitStatistics) return EndpointStats.CreateSimple(responseTimeMs);

        var stats = _endpointStats.GetOrAdd(endpointPath, _ => new EndpointStats(_options.RateLimitStatsWindowSize));

        stats.AddResponseTime(responseTimeMs);
        return stats;
    }

    /// <summary>
    ///     Gets statistics for an endpoint without recording new data
    /// </summary>
    public EndpointStats? GetEndpointStats(string endpointPath)
    {
        return _endpointStats.TryGetValue(endpointPath, out var stats) ? stats : null;
    }

    /// <summary>
    ///     Parses a delay range string and calculates the delay to apply
    /// </summary>
    /// <param name="delayRange">Delay range: "min-max" (e.g., "500-4000"), "max", or null</param>
    /// <param name="measuredTimeMs">The measured LLM response time in milliseconds</param>
    /// <param name="endpointPath">The endpoint path for statistics lookup</param>
    /// <returns>Delay in milliseconds to apply, or null if no delay</returns>
    public int? CalculateDelay(string? delayRange, long measuredTimeMs, string? endpointPath = null)
    {
        if (string.IsNullOrWhiteSpace(delayRange))
            return null;

        delayRange = delayRange.Trim();

        // Handle "max" - match measured time
        if (delayRange.Equals("max", StringComparison.OrdinalIgnoreCase)) return (int)measuredTimeMs;

        // Handle "avg" - use endpoint average if available
        if (delayRange.Equals("avg", StringComparison.OrdinalIgnoreCase) && endpointPath != null)
        {
            var stats = GetEndpointStats(endpointPath);
            return stats != null ? (int)stats.AverageResponseTimeMs : (int)measuredTimeMs;
        }

        // Handle range format: "min-max"
        var parts = delayRange.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 &&
            int.TryParse(parts[0].Trim(), out var minMs) &&
            int.TryParse(parts[1].Trim(), out var maxMs))
        {
            if (maxMs > minMs) return _random.Next(minMs, maxMs + 1);
            return Math.Max(minMs, maxMs);
        }

        // Handle single value
        if (int.TryParse(delayRange, out var fixedDelay)) return fixedDelay;

        return null;
    }

    /// <summary>
    ///     Applies a rate-limited delay based on configuration and measured time
    /// </summary>
    /// <param name="delayRange">Delay range from config or request override</param>
    /// <param name="measuredTimeMs">Measured LLM response time</param>
    /// <param name="endpointPath">Endpoint path for statistics</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The delay that was applied in milliseconds</returns>
    public async Task<int?> ApplyRateLimitDelayAsync(
        string? delayRange,
        long measuredTimeMs,
        string? endpointPath = null,
        CancellationToken cancellationToken = default)
    {
        var delay = CalculateDelay(delayRange, measuredTimeMs, endpointPath);

        if (delay.HasValue && delay.Value > 0) await Task.Delay(delay.Value, cancellationToken);

        return delay;
    }

    /// <summary>
    ///     Determines the optimal rate limit strategy for a given number of completions
    /// </summary>
    public RateLimitStrategy SelectStrategy(int nCompletions, RateLimitStrategy configuredStrategy)
    {
        if (configuredStrategy != RateLimitStrategy.Auto)
            return configuredStrategy;

        // Auto-select based on n
        return nCompletions switch
        {
            1 => RateLimitStrategy.Sequential, // No batching needed
            <= 5 => RateLimitStrategy.Parallel, // Small batch - parallel is efficient
            _ => RateLimitStrategy.Streaming // Large batch - streaming is best
        };
    }

    /// <summary>
    ///     Calculates standard rate limit headers based on endpoint statistics
    /// </summary>
    public RateLimitHeaders CalculateRateLimitHeaders(string endpointPath, long? currentRequestMs = null)
    {
        var stats = GetEndpointStats(endpointPath);
        var avgTime = currentRequestMs ?? stats?.AverageResponseTimeMs ?? 1000;

        // Calculate requests per minute based on average response time
        var requestsPerMinute = (int)Math.Floor(60000.0 / avgTime);

        return new RateLimitHeaders
        {
            Limit = requestsPerMinute,
            Remaining = requestsPerMinute - 1, // Simple simulation
            Reset = DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeSeconds()
        };
    }
}

/// <summary>
///     Per-endpoint statistics with moving average
/// </summary>
public class EndpointStats
{
    private readonly object _lock = new();
    private readonly Queue<long> _responseTimes = new();

    public EndpointStats()
    {
    }

    public EndpointStats(int windowSize)
    {
        WindowSize = windowSize;
    }

    public int WindowSize { get; set; } = 10;
    public long AverageResponseTimeMs { get; internal set; }
    public long RequestCount { get; internal set; }
    public long MinResponseTimeMs { get; internal set; }
    public long MaxResponseTimeMs { get; internal set; }
    public DateTime LastRequestTime { get; internal set; }

    public static EndpointStats CreateSimple(long responseTimeMs)
    {
        return new EndpointStats
        {
            AverageResponseTimeMs = responseTimeMs,
            RequestCount = 1,
            MinResponseTimeMs = responseTimeMs,
            MaxResponseTimeMs = responseTimeMs,
            LastRequestTime = DateTime.UtcNow
        };
    }

    public void AddResponseTime(long timeMs)
    {
        lock (_lock)
        {
            _responseTimes.Enqueue(timeMs);
            RequestCount++;
            LastRequestTime = DateTime.UtcNow;

            // Maintain window size
            while (_responseTimes.Count > WindowSize) _responseTimes.Dequeue();

            // Recalculate statistics
            var times = _responseTimes.ToArray();
            AverageResponseTimeMs = (long)times.Average();
            MinResponseTimeMs = times.Min();
            MaxResponseTimeMs = times.Max();
        }
    }
}

/// <summary>
///     Standard rate limit response headers
/// </summary>
public class RateLimitHeaders
{
    /// <summary>
    ///     Maximum requests allowed per time window
    /// </summary>
    public int Limit { get; set; }

    /// <summary>
    ///     Requests remaining in current window
    /// </summary>
    public int Remaining { get; set; }

    /// <summary>
    ///     Unix timestamp when the rate limit resets
    /// </summary>
    public long Reset { get; set; }
}