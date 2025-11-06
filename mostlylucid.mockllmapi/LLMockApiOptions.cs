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
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:11434/v1/";

    /// <summary>
    /// Model name to use (default: llama3)
    /// </summary>
    public string ModelName { get; set; } = "llama3";

    /// <summary>
    /// Temperature for LLM generation (default: 1.2 for high randomness)
    /// Higher values = more creative/random, lower values = more deterministic
    /// </summary>
    public double Temperature { get; set; } = 1.2;

    /// <summary>
    /// Maximum input tokens/length for model prompts (default: 2048)
    /// Used to automatically truncate context history to fit within model limits
    /// Common values: 2048 (Llama2), 4096 (Llama3), 8192 (larger models)
    /// </summary>
    public int MaxInputTokens { get; set; } = 2048;

    /// <summary>
    /// Maximum output tokens the LLM can generate (default: 2048)
    /// Used for automatic chunking calculations when EnableAutoChunking is true.
    /// If a request would exceed this limit, it's automatically split into chunks.
    /// Common values: 512 (small models), 2048 (Llama3), 4096 (larger models)
    /// </summary>
    public int MaxOutputTokens { get; set; } = 2048;

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
    /// Minimum delay in milliseconds between streaming chunks (default: 0 = no delay)
    /// </summary>
    public int StreamingChunkDelayMinMs { get; set; } = 0;

    /// <summary>
    /// Maximum delay in milliseconds between streaming chunks (default: 0 = no delay)
    /// If set with Min, a random delay between Min and Max will be used
    /// </summary>
    public int StreamingChunkDelayMaxMs { get; set; } = 0;

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
    public List<Models.HubContextConfig> HubContexts { get; set; } = new();

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
}

/// <summary>
/// Configuration for an OpenAPI specification to use for mock generation
/// </summary>
public class OpenApiSpecConfig
{
    /// <summary>
    /// Unique name for this spec configuration (used for logging and management)
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// URL or file path to the OpenAPI spec (YAML or JSON format)
    /// Examples:
    /// - "https://petstore3.swagger.io/api/v3/openapi.json"
    /// - "https://api.example.com/swagger.json"
    /// - "./specs/my-api.yaml"
    /// - "C:/specs/api-definition.json"
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Base path to mount the mock endpoints (e.g., "/api/v1")
    /// If not specified, uses the servers[0].url from the OpenAPI spec
    /// </summary>
    public string? BasePath { get; set; }

    /// <summary>
    /// Optional context name for maintaining consistency across related API calls
    /// Specs sharing the same context will maintain consistent IDs, names, and state
    /// Example: "user-session-1", "e-commerce-demo"
    /// </summary>
    public string? ContextName { get; set; }

    /// <summary>
    /// Enable streaming support for this spec (default: false)
    /// When enabled, adds /stream suffix to all endpoints for SSE streaming
    /// </summary>
    public bool EnableStreaming { get; set; } = false;

    /// <summary>
    /// If true, only generates endpoints for operations with specific tags
    /// </summary>
    public List<string>? IncludeTags { get; set; }

    /// <summary>
    /// If true, excludes endpoints for operations with specific tags
    /// </summary>
    public List<string>? ExcludeTags { get; set; }

    /// <summary>
    /// If true, only generates endpoints for specific paths (supports wildcards)
    /// Example: ["/users/*", "/products/*"]
    /// </summary>
    public List<string>? IncludePaths { get; set; }

    /// <summary>
    /// If true, excludes specific paths (supports wildcards)
    /// Example: ["/admin/*", "/internal/*"]
    /// </summary>
    public List<string>? ExcludePaths { get; set; }
}
