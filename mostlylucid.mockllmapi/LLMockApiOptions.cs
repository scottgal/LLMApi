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
}
