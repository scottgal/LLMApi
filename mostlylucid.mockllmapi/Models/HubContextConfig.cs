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
    /// Plain English description of the data to generate (optional)
    /// Used by LLM to automatically create appropriate shape if Shape is not provided
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// HTTP method to simulate (GET, POST, etc.)
    /// </summary>
    public string Method { get; set; } = "GET";

    /// <summary>
    /// Path to simulate (e.g., "/weather/current", "/cars/status")
    /// </summary>
    public string Path { get; set; } = "/data";

    /// <summary>
    /// Simulated request body (optional)
    /// </summary>
    public string? Body { get; set; }

    /// <summary>
    /// JSON shape or JSON Schema for response structure
    /// </summary>
    public string? Shape { get; set; }

    /// <summary>
    /// Whether to treat Shape as JSON Schema (auto-detected if not specified)
    /// </summary>
    public bool? IsJsonSchema { get; set; }
}
