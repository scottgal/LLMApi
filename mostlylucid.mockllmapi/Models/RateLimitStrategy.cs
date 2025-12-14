namespace mostlylucid.mockllmapi.Models;

/// <summary>
///     Rate limiting strategy for n-completions and batched requests
///     Determines how multiple LLM requests are executed and delayed
/// </summary>
public enum RateLimitStrategy
{
    /// <summary>
    ///     Automatically select best strategy based on request parameters
    ///     Considers: number of completions (n), configured delays, endpoint statistics
    ///     Logic: n=1 → no batching, n=2-5 → parallel, n>5 → streaming
    /// </summary>
    Auto = 0,

    /// <summary>
    ///     Execute requests one at a time with delays between each
    ///     Use case: Predictable timing, testing sequential backoff strategies
    ///     Pattern: Request 1 → Delay → Request 2 → Delay → Request 3
    /// </summary>
    Sequential = 1,

    /// <summary>
    ///     Execute all requests simultaneously, stagger response delivery
    ///     Use case: Fast completion with simulated rate limiting
    ///     Pattern: Start all requests → Complete independently → Apply delays → Return
    /// </summary>
    Parallel = 2,

    /// <summary>
    ///     Stream results as they complete with rate-limited delays
    ///     Use case: Real-time UIs, testing SSE with rate limits
    ///     Pattern: Stream each completion as SSE event with configured delay
    /// </summary>
    Streaming = 3
}