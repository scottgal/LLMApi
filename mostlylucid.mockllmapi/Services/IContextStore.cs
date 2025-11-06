namespace mostlylucid.mockllmapi.Services;

/// <summary>
/// Interface for storing and retrieving API conversation contexts.
/// Implementations can use in-memory cache, distributed cache, or database storage.
/// </summary>
public interface IContextStore
{
    /// <summary>
    /// Gets or creates a context with the specified name
    /// </summary>
    /// <param name="contextName">The unique context identifier</param>
    /// <param name="factory">Factory function to create a new context if it doesn't exist</param>
    /// <returns>The existing or newly created context</returns>
    ApiContext GetOrAdd(string contextName, Func<string, ApiContext> factory);

    /// <summary>
    /// Tries to get a context by name
    /// </summary>
    /// <param name="contextName">The unique context identifier</param>
    /// <param name="context">The retrieved context, if found</param>
    /// <returns>True if the context was found, false otherwise</returns>
    bool TryGetValue(string contextName, out ApiContext? context);

    /// <summary>
    /// Tries to remove a context by name
    /// </summary>
    /// <param name="contextName">The unique context identifier</param>
    /// <param name="context">The removed context, if found</param>
    /// <returns>True if the context was found and removed, false otherwise</returns>
    bool TryRemove(string contextName, out ApiContext? context);

    /// <summary>
    /// Gets all context names currently stored
    /// </summary>
    /// <returns>Collection of all context names</returns>
    IEnumerable<string> GetAllContextNames();

    /// <summary>
    /// Gets all contexts currently stored
    /// </summary>
    /// <returns>Collection of all contexts</returns>
    IEnumerable<ApiContext> GetAllContexts();

    /// <summary>
    /// Clears all contexts
    /// </summary>
    void Clear();

    /// <summary>
    /// Gets the count of stored contexts
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Updates a context's last used time (for sliding expiration)
    /// </summary>
    /// <param name="contextName">The unique context identifier</param>
    void TouchContext(string contextName);
}
