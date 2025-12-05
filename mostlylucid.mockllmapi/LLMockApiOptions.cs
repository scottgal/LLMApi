using mostlylucid.mockllmapi.Models;

namespace mostlylucid.mockllmapi;

/// <summary>
/// Configuration options for LLMock API
/// </summary>
public class LLMockApiOptions
{
    /// <summary>
    /// Configuration section name in appsettings.json
    /// </summary>
    public const string SectionName = "MockLlmApi";

    /// <summary>
    /// Base URL for the LLM API (default: http://localhost:11434/v1/)
    /// DEPRECATED: Use LlmBackends instead for multiple backend support
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:11434/v1/";

    /// <summary>
    /// Model name to use (default: llama3)
    /// DEPRECATED: Use LlmBackends instead for multiple backend support
    /// </summary>
    public string ModelName { get; set; } = "llama3";

    /// <summary>
    /// Multiple LLM backend configurations (introduced in v1.8.0)
    /// Supports multiple LLM instances for load balancing and failover.
    /// If empty, falls back to BaseUrl and ModelName for backward compatibility.
    /// </summary>
    public List<LlmBackendConfig> LlmBackends { get; set; } = new();

    /// <summary>
    /// Temperature for LLM generation (default: 1.2 for high randomness)
    /// Higher values = more creative/random, lower values = more deterministic
    /// </summary>
    public double Temperature { get; set; } = 1.2;

    /// <summary>
    /// Maximum context window size for the model (default: 4096)
    /// Set this to match your model's total context window capacity.
    /// The system automatically handles allocation between input (prompts/context history)
    /// and output (generation), as well as chunking for large responses.
    ///
    /// Common values by model:
    /// - gemma3:4b: 4096
    /// - llama3: 8192
    /// - mistral:7b: 8192
    /// - mistral-nemo: 32768 (or up to 128000 if configured in Ollama)
    ///
    /// Where to find this value:
    /// 1. Check model card on Ollama: https://ollama.com/library/{model}
    /// 2. Run: ollama show {model}
    /// 3. Look for "context_length" or "num_ctx" in model parameters
    ///
    /// For Ollama models with larger contexts (like mistral-nemo 128K):
    /// See: https://github.com/ScottGalloway/mostlylucid.mockllmapi/blob/main/docs/MULTIPLE_LLM_BACKENDS.md#%EF%B8%8F-ollama-context-window-configuration
    /// </summary>
    public int MaxContextWindow { get; set; } = 4096;

    // Internal helpers for allocation (implementation detail - not exposed to users)
    internal int MaxInputTokens => (int)(MaxContextWindow * 0.75);
    internal int MaxOutputTokens => (int)(MaxContextWindow * 0.25);

    /// <summary>
    /// Enable automatic request chunking for large responses (default: true)
    /// When enabled, requests that would exceed MaxOutputTokens are automatically
    /// split into multiple chunks, maintaining consistency across chunks.
    /// Disable per-request with ?autoChunk=false query parameter.
    /// </summary>
    public bool EnableAutoChunking { get; set; } = true;

    /// <summary>
    /// Custom prompt template for non-streaming requests (optional)
    /// Available placeholders: {method}, {path}, {body}, {randomSeed}, {timestamp}
    /// </summary>
    public string? CustomPromptTemplate { get; set; }

    /// <summary>
    /// Custom prompt template for streaming requests (optional)
    /// </summary>
    public string? CustomStreamingPromptTemplate { get; set; }

    /// <summary>
    /// Timeout in seconds for LLM requests (default: 30)
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Enable verbose logging (default: false)
    /// </summary>
    public bool EnableVerboseLogging { get; set; } = false;

    /// <summary>
    /// When enabled, includes the JSON shape/schema used to generate the response.
    /// Can be overridden per request with ?includeSchema=true.
    /// </summary>
    public bool IncludeShapeInResponse { get; set; } = false;

    /// <summary>
    /// Max number of cached response variants to keep per unique key (method+path+shape).
    /// Can be capped lower by the $cache value in shape; defaults to 5.
    /// </summary>
    public int MaxCachePerKey { get; set; } = 5;

    /// <summary>
    /// Sliding expiration in minutes for cached responses (default: 15 minutes)
    /// Cache entries are automatically removed after this period of inactivity.
    /// Each cache hit refreshes the expiration timer.
    /// </summary>
    public int CacheSlidingExpirationMinutes { get; set; } = 15;

    /// <summary>
    /// Cache refresh threshold as a percentage (0-100, default: 50)
    /// When cache utilization drops below this percentage, background pre-fetch can be triggered.
    /// For example, 50 means cache will consider refreshing when < 50% full.
    /// Note: Automatic pre-fetch is not yet implemented, this is for future use.
    /// </summary>
    public int CacheRefreshThresholdPercent { get; set; } = 50;

    /// <summary>
    /// Maximum items per response AND maximum cache size (default: 1000)
    /// This is a dual-purpose setting:
    /// 1. Response Limit: Maximum number of items that can be returned in a single API response
    /// 2. Cache Size: Maximum number of cached response variants across all keys
    /// Requests exceeding this will be automatically chunked (if EnableAutoChunking is true).
    /// </summary>
    public int MaxItems { get; set; } = 1000;

    /// <summary>
    /// Absolute expiration in minutes for cached responses (default: 60 minutes, null = no absolute expiration)
    /// Cached entries will be removed after this time regardless of access.
    /// Works in conjunction with CacheSlidingExpirationMinutes.
    /// </summary>
    public int? CacheAbsoluteExpirationMinutes { get; set; } = 60;

    /// <summary>
    /// Cache priority for memory management (default: Normal)
    /// Options: Low (0), Normal (1), High (2), NeverRemove (3)
    /// Higher priority items are retained longer under memory pressure.
    /// </summary>
    public int CachePriority { get; set; } = 1; // Normal

    /// <summary>
    /// Enable cache statistics tracking (default: false)
    /// When enabled, tracks cache hits, misses, and utilization.
    /// Accessible via management endpoints. Minimal performance overhead.
    /// </summary>
    public bool EnableCacheStatistics { get; set; } = false;

    /// <summary>
    /// Enable cache compression for responses (default: false)
    /// When enabled, compresses cached responses to save memory.
    /// Trade-off: saves memory but adds CPU overhead for compression/decompression.
    /// Recommended for large responses or memory-constrained environments.
    /// </summary>
    public bool EnableCacheCompression { get; set; } = false;

    /// <summary>
    /// Server-Sent Events (SSE) streaming mode (default: LlmTokens for backward compatibility)
    /// - LlmTokens: Stream LLM generation token-by-token (AI chat interface testing)
    /// - CompleteObjects: Stream complete JSON objects as separate events (realistic REST API)
    /// - ArrayItems: Stream array items individually with metadata (paginated results)
    /// Can be overridden per-request with ?sseMode=CompleteObjects query parameter
    /// </summary>
    public SseMode SseMode { get; set; } = SseMode.LlmTokens;

    /// <summary>
    /// Minimum delay in milliseconds between streaming chunks (default: 0 = no delay)
    /// </summary>
    public int StreamingChunkDelayMinMs { get; set; } = 0;

    /// <summary>
    /// Maximum delay in milliseconds between streaming chunks (default: 0 = no delay)
    /// If set with Min, a random delay between Min and Max will be used
    /// </summary>
    public int StreamingChunkDelayMaxMs { get; set; } = 0;

    /// <summary>
    /// Enable continuous SSE streaming mode (default: false)
    /// When enabled, SSE connections stay open and continuously generate new data like SignalR.
    /// Can be enabled per-request with ?continuous=true query parameter.
    /// </summary>
    public bool EnableContinuousStreaming { get; set; } = false;

    /// <summary>
    /// Interval in milliseconds between continuous SSE events (default: 2000 = 2 seconds)
    /// Only applies when continuous streaming is enabled.
    /// Can be overridden per-request with ?interval=3000 query parameter.
    /// </summary>
    public int ContinuousStreamingIntervalMs { get; set; } = 2000;

    /// <summary>
    /// Maximum duration in seconds for continuous SSE connections (default: 300 = 5 minutes)
    /// Prevents infinite connections and resource leaks.
    /// Set to 0 for unlimited duration (not recommended for production).
    /// Can be overridden per-request with ?maxDuration=600 query parameter.
    /// </summary>
    public int ContinuousStreamingMaxDurationSeconds { get; set; } = 300;

    /// <summary>
    /// Minimum random delay in milliseconds before responding to any request (default: 0 = no delay)
    /// </summary>
    public int RandomRequestDelayMinMs { get; set; } = 0;

    /// <summary>
    /// Maximum random delay in milliseconds before responding to any request (default: 0 = no delay)
    /// If set with Min, a random delay between Min and Max will be used
    /// </summary>
    public int RandomRequestDelayMaxMs { get; set; } = 0;

    /// <summary>
    /// Hub context configurations for SignalR (only used when AddLLMockSignalR is called)
    /// Each context simulates an API request and generates data continuously
    /// </summary>
    public List<HubContextConfig> HubContexts { get; set; } = new();

    /// <summary>
    /// Interval in milliseconds between background data generation pushes (default: 5000 = 5 seconds)
    /// </summary>
    public int SignalRPushIntervalMs { get; set; } = 5000;

    #region Resilience Policy Options

    /// <summary>
    /// Enable exponential backoff retry policy for LLM requests (default: true)
    /// </summary>
    public bool EnableRetryPolicy { get; set; } = true;

    /// <summary>
    /// Maximum number of retry attempts for failed LLM requests (default: 3)
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Base delay in seconds for exponential backoff (default: 1)
    /// Actual delay = BaseDelay * 2^attempt (e.g., 1s, 2s, 4s)
    /// </summary>
    public double RetryBaseDelaySeconds { get; set; } = 1.0;

    /// <summary>
    /// Enable circuit breaker pattern for LLM requests (default: true)
    /// </summary>
    public bool EnableCircuitBreaker { get; set; } = true;

    /// <summary>
    /// Number of consecutive failures before circuit breaker opens (default: 5)
    /// </summary>
    public int CircuitBreakerFailureThreshold { get; set; } = 5;

    /// <summary>
    /// Duration in seconds the circuit breaker stays open before attempting to close (default: 30)
    /// </summary>
    public int CircuitBreakerDurationSeconds { get; set; } = 30;

    #endregion

    #region GraphQL Options

    /// <summary>
    /// Maximum tokens for GraphQL responses (default: 500, null = no limit)
    /// IMPORTANT: Lower values (200-300) are RECOMMENDED to ensure complete, valid JSON.
    /// The prompt instructs the LLM to prioritize correctness over length - smaller limits
    /// force simpler, more reliable responses. Set lower for smaller models like tinyllama,
    /// or if you see truncated JSON errors. Higher values (500-1000) work for larger models.
    /// </summary>
    public int? GraphQLMaxTokens { get; set; } = 500;

    #endregion

    #region OpenAPI Options

    /// <summary>
    /// OpenAPI/Swagger specification configurations.
    /// Each spec can be loaded from a URL or file path and will generate mock endpoints
    /// based on the API definition.
    /// </summary>
    public List<OpenApiSpecConfig> OpenApiSpecs { get; set; } = new();

    #endregion

    #region Tools & Actions Options

    /// <summary>
    /// Tool execution mode (default: Disabled)
    /// - Disabled: Tools not available
    /// - Explicit: Tools called via ?useTool=name or X-Use-Tool header
    /// - LlmDriven: LLM decides which tools to call (Phase 2)
    /// </summary>
    public ToolExecutionMode ToolExecutionMode { get; set; } = ToolExecutionMode.Disabled;

    /// <summary>
    /// Available tools that can be called from mock endpoints
    /// Supports HTTP calls, mock endpoint calls, and extensible tool types
    /// MCP-compatible design for future integration
    /// </summary>
    public List<ToolConfig> Tools { get; set; } = new();

    /// <summary>
    /// Maximum concurrent tool executions per request (default: 5)
    /// Prevents runaway tool chains
    /// </summary>
    public int MaxConcurrentTools { get; set; } = 5;

    /// <summary>
    /// Maximum tool chain depth (default: 3)
    /// Prevents infinite recursion in decision trees
    /// </summary>
    public int MaxToolChainDepth { get; set; } = 3;

    /// <summary>
    /// Include tool results in response (default: false)
    /// When true, adds "toolResults" field to response JSON
    /// Useful for debugging tool chains
    /// </summary>
    public bool IncludeToolResultsInResponse { get; set; } = false;

    #endregion

    #region Pre-configured REST APIs

    /// <summary>
    /// Pre-configured REST API definitions (NEW in v2.2.0)
    /// Define complete API configurations with shape, context, tools, etc.
    /// Call by name to apply all settings automatically: /api/configured/{name}
    /// </summary>
    public List<RestApiConfig> RestApis { get; set; } = new();

    #endregion

    #region Context Options

    /// <summary>
    /// Sliding expiration in minutes for API contexts (default: 15 minutes)
    /// Contexts are automatically removed after this period of inactivity.
    /// Each context access (request/response) refreshes the expiration timer.
    /// Set higher for long-running test sessions, lower to reduce memory usage.
    /// </summary>
    public int ContextExpirationMinutes { get; set; } = 15;

    #endregion

    #region Journeys Options

    /// <summary>
    /// Journey configuration for simulating multi-step user flows.
    /// Journeys define sequences of API calls that simulate realistic user behavior,
    /// with each step having its own shape, context, and prompt hints.
    /// LLMs can later use this to decide on a journey based on their own decisions.
    /// </summary>
    public JourneysConfig? Journeys { get; set; }

    #endregion

    #region Rate Limiting & Batching Options

    /// <summary>
    /// Enable rate limiting simulation (default: false)
    /// When enabled, adds artificial delays to responses to simulate rate-limited APIs.
    /// Useful for testing backoff strategies, timeouts, and concurrent request handling.
    /// </summary>
    public bool EnableRateLimiting { get; set; } = false;

    /// <summary>
    /// Delay range in milliseconds for rate limiting (default: null = disabled)
    /// Format: "min-max" (e.g., "500-4000") or "max" to match measured LLM response time.
    /// - "500-4000": Random delay between 500ms and 4000ms
    /// - "max": Delay matches actual LLM response time (doubles total response time)
    /// - null: No delay (same as EnableRateLimiting=false)
    /// Can be overridden per-request with ?rateLimit=500-4000 query parameter or X-Rate-Limit-Delay header.
    /// </summary>
    public string? RateLimitDelayRange { get; set; }

    /// <summary>
    /// Rate limiting strategy for n-completions (default: Auto)
    /// - Auto: System chooses optimal strategy based on request parameters
    /// - Sequential: Complete one request, delay, start next (predictable but slower)
    /// - Parallel: Start all requests simultaneously, stagger responses (faster but more resource-intensive)
    /// - Streaming: Stream results as ready with rate-limited delays (best for real-time UIs)
    /// Can be overridden per-request with ?strategy=parallel query parameter or X-Rate-Limit-Strategy header.
    /// </summary>
    public RateLimitStrategy RateLimitStrategy { get; set; } = RateLimitStrategy.Auto;

    /// <summary>
    /// Enable per-endpoint rate limit statistics tracking (default: true)
    /// Tracks moving average of LLM response times per endpoint path.
    /// Used for calculating realistic rate limits and auto-delay calculations.
    /// Statistics exposed via X-LLMApi-Avg-Time response header.
    /// </summary>
    public bool EnableRateLimitStatistics { get; set; } = true;

    /// <summary>
    /// Window size for calculating moving average response times (default: 10)
    /// Higher values = smoother average but slower to adapt to changes.
    /// Lower values = more responsive to recent performance but more volatile.
    /// </summary>
    public int RateLimitStatsWindowSize { get; set; } = 10;

    #endregion
}
