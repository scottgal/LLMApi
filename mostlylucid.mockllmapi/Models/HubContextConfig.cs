namespace mostlylucid.mockllmapi.Models;

/// <summary>
/// Configuration for a SignalR hub context with request-based data generation
/// </summary>
public class HubContextConfig
{
    /// <summary>
    /// Name of the context (used for SignalR group name)
    /// </summary>
    public string Name { get; set; } = "default";

    /// <summary>
    /// Plain English description of the data to generate
    /// Used by LLM to automatically create appropriate JSON structure
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Internal: HTTP method used for LLM prompt context (defaults to GET)
    /// Note: SignalR uses WebSockets, not HTTP - this is only for prompt generation
    /// </summary>
    public string Method { get; set; } = "GET";

    /// <summary>
    /// Internal: Path used for LLM prompt context (defaults to /data)
    /// Note: SignalR uses WebSockets, not HTTP - this is only for prompt generation
    /// </summary>
    public string Path { get; set; } = "/data";

    /// <summary>
    /// Internal: Request body used for LLM prompt context (optional)
    /// Note: SignalR uses WebSockets, not HTTP - this is only for prompt generation
    /// </summary>
    public string? Body { get; set; }

    /// <summary>
    /// Optional: JSON shape or JSON Schema for response structure
    /// If not provided, LLM generates structure from Description
    /// </summary>
    public string? Shape { get; set; }

    /// <summary>
    /// Whether to treat Shape as JSON Schema (auto-detected if not specified)
    /// </summary>
    public bool? IsJsonSchema { get; set; }

    /// <summary>
    /// Whether this context is actively generating data (default: true)
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Number of currently connected clients
    /// </summary>
    public int ConnectionCount { get; set; } = 0;
}
