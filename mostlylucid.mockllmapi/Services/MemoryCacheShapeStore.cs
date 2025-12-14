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
    private long _currentCacheSizeBytes;
    private readonly object _sizeLock = new object();

    public MemoryCacheShapeStore(
        IMemoryCache cache,
        ILogger<MemoryCacheShapeStore> logger,
        int expirationMinutes = 15)
    {
        _cache = cache;
        _logger = logger;
        _paths = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        _slidingExpiration = TimeSpan.FromMinutes(expirationMinutes);
        _currentCacheSizeBytes = 0;
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
            RemoveShape(path);
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
        ClearAllShapes();
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
        var shapeSize = System.Text.Encoding.UTF8.GetByteCount(shape);
        
        lock (_sizeLock)
        {
            _currentCacheSizeBytes += shapeSize;
        }
        
        _cache.Set(cacheKey, shape, _slidingExpiration);
        _paths[path] = 0; // Mark path as having a shape
    }

    public void RemoveShape(string path)
    {
        var cacheKey = GetCacheKey(path);
        if (_cache.TryGetValue<string>(cacheKey, out var shape))
        {
            var shapeSize = System.Text.Encoding.UTF8.GetByteCount(shape);
            lock (_sizeLock)
            {
                _currentCacheSizeBytes -= shapeSize;
                if (_currentCacheSizeBytes < 0) _currentCacheSizeBytes = 0;
            }
        }
        _cache.Remove(cacheKey);
        _paths.TryRemove(path, out _);
    }

    public void ClearAllShapes()
    {
        foreach (var path in _paths.Keys.ToList())
        {
            RemoveShape(path);
        }
        _paths.Clear();
        lock (_sizeLock)
        {
            _currentCacheSizeBytes = 0;
        }
        _logger.LogInformation("Cleared all shapes from cache");
    }

    public int GetStoredShapeCount()
    {
        return _paths.Count;
    }

    public IEnumerable<string> GetStoredPaths()
    {
        return _paths.Keys;
    }

    /// <summary>
    /// Gets the current cache size in bytes
    /// </summary>
    public long GetCurrentCacheSizeBytes()
    {
        return _currentCacheSizeBytes;
    }

    /// <summary>
    /// Gets the current cache size in megabytes
    /// </summary>
    public double GetCurrentCacheSizeMB()
    {
        return _currentCacheSizeBytes / (1024.0 * 1024.0);
    }

    /// <summary>
    /// Checks if the cache has exceeded the maximum size
    /// </summary>
    public bool HasExceededMaxSize(long maxCacheSizeBytes)
    {
        return _currentCacheSizeBytes > maxCacheSizeBytes;
    }

    /// <summary>
    /// Trims the cache to the specified size by removing oldest entries
    /// </summary>
    public void TrimCacheToSize(long targetSizeBytes)
    {
        if (_currentCacheSizeBytes <= targetSizeBytes)
            return;

        var entriesToRemove = new List<string>();
        var currentSize = _currentCacheSizeBytes;

        foreach (var path in _paths.Keys)
        {
            if (currentSize <= targetSizeBytes)
                break;

            var cacheKey = GetCacheKey(path);
            if (_cache.TryGetValue<string>(cacheKey, out var shape))
            {
                var shapeSize = System.Text.Encoding.UTF8.GetByteCount(shape);
                entriesToRemove.Add(path);
                currentSize -= shapeSize;
            }
        }

        foreach (var path in entriesToRemove)
        {
            RemoveShape(path);
        }

        _logger.LogInformation("Trimmed cache: removed {Count} entries", entriesToRemove.Count);
    }
}
