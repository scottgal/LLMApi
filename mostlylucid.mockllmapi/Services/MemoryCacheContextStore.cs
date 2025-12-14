using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace mostlylucid.mockllmapi.Services;

/// <summary>
///     In-memory context store with configurable automatic expiration (default: 15 minutes of inactivity).
///     Uses IMemoryCache with sliding expiration to prevent memory leaks.
///     Contexts are automatically removed when not accessed within the expiration window.
/// </summary>
public class MemoryCacheContextStore : IContextStore
{
    private const string CacheKeyPrefix = "ApiContext_";
    private readonly IMemoryCache _cache;
    private readonly ConcurrentDictionary<string, byte> _contextNames; // Tracks all context names
    private readonly ILogger<MemoryCacheContextStore> _logger;
    private readonly TimeSpan _slidingExpiration;

    public MemoryCacheContextStore(
        IMemoryCache cache,
        ILogger<MemoryCacheContextStore> logger,
        int expirationMinutes = 15)
    {
        _cache = cache;
        _logger = logger;
        _contextNames = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        _slidingExpiration = TimeSpan.FromMinutes(expirationMinutes);
    }

    public ApiContext GetOrAdd(string contextName, Func<string, ApiContext> factory)
    {
        if (string.IsNullOrWhiteSpace(contextName))
            throw new ArgumentException("Context name cannot be null or empty", nameof(contextName));

        var cacheKey = GetCacheKey(contextName);

        // Try to get existing context
        if (_cache.TryGetValue<ApiContext>(cacheKey, out var existingContext) && existingContext != null)
        {
            // Touch the cache to refresh sliding expiration
            SetContextInCache(cacheKey, contextName, existingContext);
            return existingContext;
        }

        // Create new context
        var newContext = factory(contextName);
        SetContextInCache(cacheKey, contextName, newContext);

        _logger.LogInformation(
            "Created new context '{ContextName}' with {ExpirationMinutes}-minute sliding expiration",
            contextName, _slidingExpiration.TotalMinutes);

        return newContext;
    }

    public bool TryGetValue(string contextName, out ApiContext? context)
    {
        if (string.IsNullOrWhiteSpace(contextName))
        {
            context = null;
            return false;
        }

        var cacheKey = GetCacheKey(contextName);
        var found = _cache.TryGetValue(cacheKey, out context);

        if (found && context != null)
            // Refresh sliding expiration
            SetContextInCache(cacheKey, contextName, context);

        return found && context != null;
    }

    public bool TryRemove(string contextName, out ApiContext? context)
    {
        if (string.IsNullOrWhiteSpace(contextName))
        {
            context = null;
            return false;
        }

        var cacheKey = GetCacheKey(contextName);
        var found = _cache.TryGetValue(cacheKey, out context);

        if (found)
        {
            _cache.Remove(cacheKey);
            _contextNames.TryRemove(contextName, out _);

            _logger.LogInformation("Removed context '{ContextName}' from cache", contextName);
        }

        return found && context != null;
    }

    public IEnumerable<string> GetAllContextNames()
    {
        // Return a snapshot of current context names
        // Note: Some may have expired since being added
        return _contextNames.Keys.Where(name =>
        {
            var cacheKey = GetCacheKey(name);
            return _cache.TryGetValue<ApiContext>(cacheKey, out _);
        }).ToList();
    }

    public IEnumerable<ApiContext> GetAllContexts()
    {
        var contexts = new List<ApiContext>();

        foreach (var contextName in _contextNames.Keys.ToList())
        {
            var cacheKey = GetCacheKey(contextName);
            if (_cache.TryGetValue<ApiContext>(cacheKey, out var context) && context != null)
                contexts.Add(context);
            else
                // Remove stale entry from tracking dictionary
                _contextNames.TryRemove(contextName, out _);
        }

        return contexts;
    }

    public void Clear()
    {
        var contextNames = _contextNames.Keys.ToList();
        var count = contextNames.Count;

        foreach (var contextName in contextNames)
        {
            var cacheKey = GetCacheKey(contextName);
            _cache.Remove(cacheKey);
        }

        _contextNames.Clear();

        _logger.LogInformation("Cleared all contexts from cache ({Count} contexts removed)", count);
    }

    public int Count
    {
        get
        {
            // Clean up stale entries and return accurate count
            var validCount = 0;
            foreach (var contextName in _contextNames.Keys.ToList())
            {
                var cacheKey = GetCacheKey(contextName);
                if (_cache.TryGetValue<ApiContext>(cacheKey, out _))
                    validCount++;
                else
                    // Remove stale entry
                    _contextNames.TryRemove(contextName, out _);
            }

            return validCount;
        }
    }

    public void TouchContext(string contextName)
    {
        if (string.IsNullOrWhiteSpace(contextName))
            return;

        var cacheKey = GetCacheKey(contextName);
        if (_cache.TryGetValue<ApiContext>(cacheKey, out var context) && context != null)
        {
            // Update LastUsedAt and refresh cache expiration
            context.LastUsedAt = DateTimeOffset.UtcNow;
            SetContextInCache(cacheKey, contextName, context);
        }
    }

    private string GetCacheKey(string contextName)
    {
        // Normalize to lowercase for case-insensitive lookups
        return $"{CacheKeyPrefix}{contextName.ToLowerInvariant()}";
    }

    private void SetContextInCache(string cacheKey, string contextName, ApiContext context)
    {
        var options = new MemoryCacheEntryOptions
        {
            SlidingExpiration = _slidingExpiration,
            Priority = CacheItemPriority.Normal
        };

        // Use a local variable for the callback to avoid closure issues
        var nameToTrack = contextName;

        // Register callback to clean up context name tracking when cache entry expires
        options.RegisterPostEvictionCallback((key, value, reason, state) =>
        {
            if (reason == EvictionReason.Expired || reason == EvictionReason.TokenExpired)
            {
                _contextNames.TryRemove(nameToTrack, out _);
                _logger.LogInformation(
                    "Context '{ContextName}' expired and was removed from cache (reason: {Reason})",
                    nameToTrack, reason);
            }
        });

        _cache.Set(cacheKey, context, options);
        _contextNames.TryAdd(contextName, 0); // Track context name (case-insensitive dictionary)
    }
}