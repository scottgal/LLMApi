namespace mostlylucid.mockllmapi.Models;

/// <summary>
///     Configuration for a SignalR hub context with request-based data generation
/// </summary>
public class HubContextConfig
{
    /// <summary>
    ///     Name of the context (used for SignalR group name)
    /// </summary>
    public string Name { get; set; } = "default";

    /// <summary>
    ///     Plain English description of the data to generate
    ///     Used by LLM to automatically create appropriate JSON structure
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    ///     Internal: HTTP method used for LLM prompt context (defaults to GET)
    ///     Note: SignalR uses WebSockets, not HTTP - this is only for prompt generation
    /// </summary>
    public string Method { get; set; } = "GET";

    /// <summary>
    ///     Internal: Path used for LLM prompt context (defaults to /data)
    ///     Note: SignalR uses WebSockets, not HTTP - this is only for prompt generation
    /// </summary>
    public string Path { get; set; } = "/data";

    /// <summary>
    ///     Internal: Request body used for LLM prompt context (optional)
    ///     Note: SignalR uses WebSockets, not HTTP - this is only for prompt generation
    /// </summary>
    public string? Body { get; set; }

    /// <summary>
    ///     Optional: JSON shape or JSON Schema for response structure
    ///     If not provided, LLM generates structure from Description
    /// </summary>
    public string? Shape { get; set; }

    /// <summary>
    ///     Whether to treat Shape as JSON Schema (auto-detected if not specified)
    /// </summary>
    public bool? IsJsonSchema { get; set; }

    /// <summary>
    ///     Optional: API context name for maintaining consistency across related calls
    ///     When set, the data generation will reference previous calls in the same context
    ///     Useful for realistic data variance (e.g., stock prices changing gradually)
    /// </summary>
    public string? ApiContextName { get; set; }

    /// <summary>
    ///     Optional: Single LLM backend name to use for this context
    ///     Allows per-context backend selection (e.g., use GPT-4 for complex data, Ollama for simple data)
    ///     If not specified, uses default backend selection
    ///     Must match a backend name from the Backends configuration array
    ///     Example: "openai-gpt4", "ollama-llama3", "lmstudio-local"
    ///     NOTE: For load balancing across multiple backends, use BackendNames instead
    /// </summary>
    public string? BackendName { get; set; }

    /// <summary>
    ///     Optional: Multiple LLM backend names for load balancing
    ///     When specified, requests are distributed across these backends using weighted round-robin
    ///     Backend weights are taken from the Backends configuration
    ///     Example: ["ollama-llama3", "ollama-mistral", "openai-gpt4-turbo"]
    ///     NOTE: If both BackendName and BackendNames are set, BackendNames takes precedence
    /// </summary>
    public string[]? BackendNames { get; set; }

    /// <summary>
    ///     Whether this context is actively generating data (default: true)
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    ///     Number of currently connected clients
    /// </summary>
    public int ConnectionCount { get; set; } = 0;

    /// <summary>
    ///     Optional: Error configuration for simulating error responses
    ///     When set, error data will be sent to SignalR clients instead of generating mock data
    /// </summary>
    public ErrorConfig? ErrorConfig { get; set; }
}