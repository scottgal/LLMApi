namespace mostlylucid.mockllmapi.Services;

/// <summary>
///     Interface for storing and retrieving auto-learned JSON shapes for endpoints.
///     Implementations can use in-memory cache, distributed cache, or database storage.
/// </summary>
public interface IShapeStore
{
    /// <summary>
    ///     Gets the count of stored shapes
    /// </summary>
    int Count { get; }

    /// <summary>
    ///     Gets or creates a shape with the specified path
    /// </summary>
    /// <param name="path">The normalized endpoint path (e.g., /api/mock/users/{id})</param>
    /// <param name="factory">Factory function to create a new shape if it doesn't exist</param>
    /// <returns>The existing or newly created shape</returns>
    string GetOrAdd(string path, Func<string, string> factory);

    /// <summary>
    ///     Tries to get a shape by path
    /// </summary>
    /// <param name="path">The normalized endpoint path</param>
    /// <param name="shape">The retrieved shape, if found</param>
    /// <returns>True if the shape was found, false otherwise</returns>
    bool TryGetValue(string path, out string? shape);

    /// <summary>
    ///     Sets or updates a shape for a path
    /// </summary>
    /// <param name="path">The normalized endpoint path</param>
    /// <param name="shape">The JSON shape to store</param>
    void Set(string path, string shape);

    /// <summary>
    ///     Tries to remove a shape by path
    /// </summary>
    /// <param name="path">The normalized endpoint path</param>
    /// <param name="shape">The removed shape, if found</param>
    /// <returns>True if the shape was found and removed, false otherwise</returns>
    bool TryRemove(string path, out string? shape);

    /// <summary>
    ///     Gets all paths currently stored
    /// </summary>
    /// <returns>Collection of all normalized paths</returns>
    IEnumerable<string> GetAllPaths();

    /// <summary>
    ///     Clears all shapes
    /// </summary>
    void Clear();

    /// <summary>
    ///     Updates a shape's last used time (for sliding expiration)
    /// </summary>
    /// <param name="path">The normalized endpoint path</param>
    void TouchShape(string path);
}