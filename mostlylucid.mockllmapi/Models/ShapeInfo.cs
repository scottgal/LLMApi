namespace mostlylucid.mockllmapi.Models;

/// <summary>
///     Information about the JSON shape/schema for the response
/// </summary>
public class ShapeInfo
{
    /// <summary>
    ///     The sanitized shape (without cache or error hints)
    /// </summary>
    public string? Shape { get; set; }

    /// <summary>
    ///     Number of responses to pre-cache (from $cache hint in shape)
    /// </summary>
    public int CacheCount { get; set; }

    /// <summary>
    ///     Whether this is a JSON Schema (vs descriptive shape)
    /// </summary>
    public bool IsJsonSchema { get; set; }

    /// <summary>
    ///     Error configuration for simulating error responses (from $error hint or error params)
    ///     If not null, the endpoint should return an error instead of generating mock data
    /// </summary>
    public ErrorConfig? ErrorConfig { get; set; }
}