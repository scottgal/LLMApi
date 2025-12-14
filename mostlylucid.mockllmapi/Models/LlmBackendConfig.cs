namespace mostlylucid.mockllmapi.Models;

/// <summary>
///     Configuration for an LLM backend instance
/// </summary>
public class LlmBackendConfig
{
    /// <summary>
    ///     Unique name for this backend (for logging and monitoring)
    ///     Used for per-request backend selection via X-LLM-Backend header or ?backend= query param
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    ///     Provider type: "ollama", "openai", "lmstudio", "anthropic", "azure"
    ///     Determines which API format to use for requests
    /// </summary>
    public string Provider { get; set; } = "ollama";

    /// <summary>
    ///     Base URL for this LLM instance
    ///     Examples:
    ///     - Ollama: "http://localhost:11434/v1/"
    ///     - OpenAI: "https://api.openai.com/v1/"
    ///     - LM Studio: "http://localhost:1234/v1/"
    ///     - Azure: "https://your-resource.openai.azure.com/"
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    ///     Model name to use with this backend
    ///     Examples:
    ///     - Ollama: "llama3", "mistral", "codellama"
    ///     - OpenAI: "gpt-4", "gpt-4-turbo", "gpt-3.5-turbo"
    ///     - LM Studio: model name from loaded model
    /// </summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>
    ///     Optional API key for this backend (required for OpenAI, Anthropic, etc.)
    ///     Can also be set via environment variable for security
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    ///     Weight for load balancing (default: 1)
    ///     Higher weights receive more traffic in weighted round-robin
    /// </summary>
    public int Weight { get; set; } = 1;

    /// <summary>
    ///     Enable this backend (default: true)
    ///     Can be disabled for maintenance without removing from config
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     Maximum output tokens for this backend (optional)
    ///     If not set, uses global MaxOutputTokens from LLMockApiOptions
    ///     Different models have different token limits:
    ///     - GPT-4: 8192 or 32768 tokens
    ///     - GPT-3.5-turbo: 4096 tokens
    ///     - Llama 3 8B: 8192 tokens
    ///     - Mistral: 8192 tokens
    ///     Use this to tune each backend based on its model capabilities
    /// </summary>
    public int? MaxTokens { get; set; }

    /// <summary>
    ///     Maximum concurrent requests for this backend (default: 0 = unlimited)
    /// </summary>
    public int MaxConcurrentRequests { get; set; } = 0;

    /// <summary>
    ///     Health check endpoint (optional, e.g., "/health")
    ///     If specified, backend health will be monitored
    /// </summary>
    public string? HealthCheckPath { get; set; }

    /// <summary>
    ///     Priority for backend selection (default: 0)
    ///     Higher priority backends are preferred when available
    ///     Useful for primary/backup configurations
    /// </summary>
    public int Priority { get; set; } = 0;
}