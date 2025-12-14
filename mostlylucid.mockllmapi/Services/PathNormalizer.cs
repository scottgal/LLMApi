using System.Text.RegularExpressions;

namespace mostlylucid.mockllmapi.Services;

/// <summary>
/// Normalizes endpoint paths by replacing dynamic segments (IDs, UUIDs, etc.) with placeholders.
/// This allows grouping requests to the same logical endpoint together for autoshape memory.
/// Example: /api/mock/users/123 -> /api/mock/users/{id}
/// </summary>
public static class PathNormalizer
{
    // Matches UUIDs (with or without hyphens)
    private static readonly Regex UuidPattern = new(
        @"^[0-9a-f]{8}-?[0-9a-f]{4}-?[0-9a-f]{4}-?[0-9a-f]{4}-?[0-9a-f]{12}$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Matches pure numbers (integer IDs)
    private static readonly Regex NumberPattern = new(
        @"^\d+$",
        RegexOptions.Compiled);

    // Matches alphanumeric IDs (mix of letters and numbers, often used as resource IDs)
    private static readonly Regex AlphanumericIdPattern = new(
        @"^[a-z0-9]+-[a-z0-9-]+$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Common endpoint keywords that should NOT be replaced (case-insensitive)
    private static readonly HashSet<string> KnownKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "api", "mock", "stream", "graphql", "grpc", "signalr",
        "users", "products", "orders", "customers", "items", "posts", "comments",
        "login", "logout", "register", "auth", "token", "refresh",
        "search", "filter", "list", "details", "create", "update", "delete",
        "v1", "v2", "v3", "latest", "stable", "beta", "alpha"
    };

    /// <summary>
    /// Normalizes a path by replacing dynamic segments with placeholders.
    /// </summary>
    /// <param name="path">The original path (e.g., /api/mock/users/123)</param>
    /// <returns>The normalized path (e.g., /api/mock/users/{id})</returns>
    public static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        // Remove query string if present
        var pathWithoutQuery = path.Split('?')[0];

        // Split by '/' and process each segment
        var segments = pathWithoutQuery.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var normalizedSegments = new List<string>(segments.Length);

        foreach (var segment in segments)
        {
            normalizedSegments.Add(NormalizeSegment(segment));
        }

        // Reconstruct path
        return "/" + string.Join("/", normalizedSegments);
    }

    /// <summary>
    /// Normalizes a single path segment.
    /// Returns "{id}" for dynamic IDs, or the original segment for static keywords.
    /// </summary>
    private static string NormalizeSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
            return segment;

        // If it's a known keyword, keep it as-is
        if (KnownKeywords.Contains(segment))
            return segment.ToLowerInvariant();

        // If it matches UUID pattern, replace with {id}
        if (UuidPattern.IsMatch(segment))
            return "{id}";

        // If it's a pure number, replace with {id}
        if (NumberPattern.IsMatch(segment))
            return "{id}";

        // If it matches alphanumeric ID pattern (e.g., "abc-123", "user-abc-def"), replace with {id}
        if (AlphanumericIdPattern.IsMatch(segment))
            return "{id}";

        // Otherwise, keep the segment as-is (lowercase for consistency)
        return segment.ToLowerInvariant();
    }

    /// <summary>
    /// Checks if a path segment appears to be a dynamic ID.
    /// Useful for validation and logging purposes.
    /// </summary>
    public static bool IsLikelyDynamicId(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
            return false;

        if (KnownKeywords.Contains(segment))
            return false;

        return UuidPattern.IsMatch(segment) ||
               NumberPattern.IsMatch(segment) ||
               AlphanumericIdPattern.IsMatch(segment);
    }
}
