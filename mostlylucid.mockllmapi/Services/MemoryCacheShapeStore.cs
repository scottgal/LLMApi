using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace mostlylucid.mockllmapi.Services;

/// <summary>
/// In-memory shape store with configurable automatic expiration (default: 15 minutes of inactivity).
/// Uses IMemoryCache with sliding expiration to prevent memory leaks.
/// Shapes are automatically removed when not accessed within the expiration window.
/// </summary>
public class MemoryCacheShapeStore : IShapeStore
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<MemoryCacheShapeStore> _logger;
    private readonly ConcurrentDictionary<string, byte> _paths; // Tracks all paths
    private readonly TimeSpan _slidingExpiration;
    private const string CacheKeyPrefix = "AutoShape_";

    public MemoryCacheShapeStore(
        IMemoryCache cache,
        ILogger<MemoryCacheShapeStore> logger,
        int expirationMinutes = 15)
    {
        _cache = cache;
        _logger = logger;
        _paths = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        _slidingExpiration = TimeSpan.FromMinutes(expirationMinutes);
    }

    public string GetOrAdd(string path, Func<string, string> factory)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path cannot be null or empty", nameof(path));

        var cacheKey = GetCacheKey(path);

        // Try to get existing shape
        if (_cache.TryGetValue<string>(cacheKey, out var existingShape) && existingShape != null)
        {
            // Touch the cache to refresh sliding expiration
            SetShapeInCache(cacheKey, path, existingShape);
            return existingShape;
        }

        // Create new shape
        var newShape = factory(path);
        SetShapeInCache(cacheKey, path, newShape);

        _logger.LogDebug(
            "Created new shape for path '{Path}' with {ExpirationMinutes}-minute sliding expiration",
            path, _slidingExpiration.TotalMinutes);

        return newShape;
    }

    public bool TryGetValue(string path, out string? shape)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            shape = null;
            return false;
        }

        var cacheKey = GetCacheKey(path);
        var found = _cache.TryGetValue<string>(cacheKey, out shape);

        if (found && shape != null)
        {
            // Refresh sliding expiration
            SetShapeInCache(cacheKey, path, shape);
        }

        return found && shape != null;
    }

    public void Set(string path, string shape)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path cannot be null or empty", nameof(path));

        if (string.IsNullOrWhiteSpace(shape))
            throw new ArgumentException("Shape cannot be null or empty", nameof(shape));

        var cacheKey = GetCacheKey(path);
        SetShapeInCache(cacheKey, path, shape);

        _logger.LogDebug("Stored shape for path '{Path}'", path);
    }

    public bool TryRemove(string path, out string? shape)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            shape = null;
            return false;
        }

        var cacheKey = GetCacheKey(path);
        var found = _cache.TryGetValue<string>(cacheKey, out shape);

        if (found)
        {
            _cache.Remove(cacheKey);
            _paths.TryRemove(path, out _);

            _logger.LogDebug("Removed shape for path '{Path}' from cache", path);
        }

        return found && shape != null;
    }

    public IEnumerable<string> GetAllPaths()
    {
        // Return a snapshot of current paths
        // Note: Some may have expired since being added
        return _paths.Keys.Where(path =>
        {
            var cacheKey = GetCacheKey(path);
            return _cache.TryGetValue<string>(cacheKey, out _);
        }).ToList();
    }

    public void Clear()
    {
        var paths = _paths.Keys.ToList();
        var count = paths.Count;

        foreach (var path in paths)
        {
            var cacheKey = GetCacheKey(path);
            _cache.Remove(cacheKey);
        }

        _paths.Clear();

        _logger.LogInformation("Cleared all shapes from cache ({Count} shapes removed)", count);
    }

    public int Count
    {
        get
        {
            // Clean up stale entries and return accurate count
            var validCount = 0;
            foreach (var path in _paths.Keys.ToList())
            {
                var cacheKey = GetCacheKey(path);
                if (_cache.TryGetValue<string>(cacheKey, out _))
                {
                    validCount++;
                }
                else
                {
                    // Remove stale entry
                    _paths.TryRemove(path, out _);
                }
            }
            return validCount;
        }
    }

    public void TouchShape(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var cacheKey = GetCacheKey(path);
        if (_cache.TryGetValue<string>(cacheKey, out var shape) && shape != null)
        {
            // Refresh cache expiration
            SetShapeInCache(cacheKey, path, shape);
        }
    }

    private string GetCacheKey(string path)
    {
        // Normalize to lowercase for case-insensitive lookups
        return $"{CacheKeyPrefix}{path.ToLowerInvariant()}";
    }

    private void SetShapeInCache(string cacheKey, string path, string shape)
    {
        var options = new MemoryCacheEntryOptions
        {
            SlidingExpiration = _slidingExpiration,
            Priority = CacheItemPriority.Normal
        };

        // Use a local variable for the callback to avoid closure issues
        var pathToTrack = path;

        // Register callback to clean up path tracking when cache entry expires
        options.RegisterPostEvictionCallback((key, value, reason, state) =>
        {
            if (reason == EvictionReason.Expired || reason == EvictionReason.TokenExpired)
            {
                _paths.TryRemove(pathToTrack, out _);
                _logger.LogDebug(
                    "Shape for path '{Path}' expired and was removed from cache (reason: {Reason})",
                    pathToTrack, reason);
            }
        });

        _cache.Set(cacheKey, shape, options);
        _paths.TryAdd(path, 0); // Track path (case-insensitive dictionary)
    }
}
