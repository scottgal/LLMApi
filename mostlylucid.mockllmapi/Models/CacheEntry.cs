namespace mostlylucid.mockllmapi.Models;

/// <summary>
/// Represents a cached set of pre-generated responses for a specific request signature
/// </summary>
internal class CacheEntry
{
    /// <summary>
    /// Depleting queue of cached responses. Items are consumed (dequeued) once.
    /// </summary>
    public Queue<string> Responses { get; } = new();

    /// <summary>
    /// Prevents multiple concurrent background refills
    /// </summary>
    public bool IsRefilling { get; set; }

    /// <summary>
    /// Indicates whether we've performed the initial prime for this key
    /// </summary>
    public bool IsPrimed { get; set; }

    /// <summary>
    /// When this cache entry was created
    /// </summary>
    public DateTime CreatedUtc { get; } = DateTime.UtcNow;

    /// <summary>
    /// Semaphore for thread-safe access to this entry
    /// </summary>
    public SemaphoreSlim Gate { get; } = new(1, 1);
}
