using System.Collections.Concurrent;
using System.IO.Hashing;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using mostlylucid.mockllmapi.Models;

namespace mostlylucid.mockllmapi.Services;

/// <summary>
/// Manages pre-caching of LLM responses for improved performance.
/// Implements LRU eviction to prevent memory exhaustion.
/// </summary>
public class CacheManager
{
    private readonly LLMockApiOptions _options;
    private readonly ILogger<CacheManager> _logger;
    private readonly ConcurrentDictionary<ulong, CacheEntry> _cache = new();
    
    // Track access order for LRU eviction (key -> last access time)
    private readonly ConcurrentDictionary<ulong, DateTime> _accessTimes = new();
    
    // Lock for eviction operations
    private readonly SemaphoreSlim _evictionLock = new(1, 1);
    
    // Eviction threshold - trigger eviction when cache exceeds this percentage of max size
    private const double EvictionThresholdPercent = 0.9;
    
    // Amount to evict when triggered (percentage of current cache size)
    private const double EvictionBatchPercent = 0.2;

    public CacheManager(IOptions<LLMockApiOptions> options, ILogger<CacheManager> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Gets the current number of cache entries
    /// </summary>
    public int CacheCount => _cache.Count;

    /// <summary>
    /// Gets the maximum allowed cache entries
    /// </summary>
    public int MaxCacheSize => _options.MaxItems;

    /// <summary>
    /// Gets a response from cache or fetches a new one
    /// </summary>
    public async Task<string> GetOrFetchAsync(
        string method,
        string fullPathWithQuery,
        string? body,
        string? shape,
        int cacheCount,
        Func<Task<string>> fetchFunc)
    {
        if (cacheCount <= 0)
        {
            return await fetchFunc();
        }

        // Check if eviction is needed before adding new entries
        await TryEvictIfNeededAsync();

        var key = ComputeCacheKey(method, fullPathWithQuery, shape);
        var entry = _cache.GetOrAdd(key, _ => new CacheEntry());
        var target = Math.Max(1, Math.Min(cacheCount, _options.MaxCachePerKey));

        // Update access time for LRU tracking
        _accessTimes[key] = DateTime.UtcNow;

        string? chosen = null;
        bool scheduleRefill = false;

        await entry.Gate.WaitAsync();
        try
        {
            // Initial prime only once per key
            if (!entry.IsPrimed)
            {
                await PrimeCache(entry, target, fetchFunc);
                entry.IsPrimed = true;
            }

            // Serve one and deplete
            if (entry.Responses.Count > 0)
            {
                chosen = entry.Responses.Dequeue();
                // Also remove from the HashSet for consistency
                entry.ResponseHashes.TryRemove(ComputeContentHash(chosen), out _);
                
                if (entry.Responses.Count == 0 && !entry.IsRefilling)
                {
                    // Trigger background refill of a new batch
                    entry.IsRefilling = true;
                    scheduleRefill = true;
                }
            }
        }
        finally
        {
            entry.Gate.Release();
        }

        if (scheduleRefill)
        {
            _ = Task.Run(() => RefillCacheAsync(entry, target, fetchFunc));
        }

        // If we served from cache, return it
        if (chosen != null)
        {
            return chosen;
        }

        // Fallback if cache was empty and not yet refilled
        return await fetchFunc();
    }

    /// <summary>
    /// Clears all cache entries
    /// </summary>
    public void ClearCache()
    {
        _cache.Clear();
        _accessTimes.Clear();
        _logger.LogInformation("Cache cleared");
    }

    /// <summary>
    /// Removes a specific cache entry by key components
    /// </summary>
    public bool RemoveEntry(string method, string fullPathWithQuery, string? shape)
    {
        var key = ComputeCacheKey(method, fullPathWithQuery, shape);
        var removed = _cache.TryRemove(key, out _);
        if (removed)
        {
            _accessTimes.TryRemove(key, out _);
            _logger.LogDebug("Removed cache entry for {Method} {Path}", method, fullPathWithQuery);
        }
        return removed;
    }

    /// <summary>
    /// Gets cache statistics
    /// </summary>
    public CacheStatistics GetStatistics()
    {
        var totalResponses = _cache.Values.Sum(e => e.Responses.Count);
        return new CacheStatistics
        {
            TotalEntries = _cache.Count,
            TotalResponses = totalResponses,
            MaxEntries = _options.MaxItems,
            MaxResponsesPerEntry = _options.MaxCachePerKey,
            UtilizationPercent = _options.MaxItems > 0 
                ? (double)_cache.Count / _options.MaxItems * 100 
                : 0
        };
    }

    private async Task TryEvictIfNeededAsync()
    {
        var maxSize = _options.MaxItems;
        if (maxSize <= 0) return; // Unlimited cache

        var currentSize = _cache.Count;
        var threshold = (int)(maxSize * EvictionThresholdPercent);

        if (currentSize < threshold) return;

        // Try to acquire eviction lock (non-blocking to avoid contention)
        if (!await _evictionLock.WaitAsync(0)) return;

        try
        {
            // Double-check after acquiring lock
            currentSize = _cache.Count;
            if (currentSize < threshold) return;

            await EvictLruEntriesAsync();
        }
        finally
        {
            _evictionLock.Release();
        }
    }

    private Task EvictLruEntriesAsync()
    {
        var currentSize = _cache.Count;
        var evictCount = (int)(currentSize * EvictionBatchPercent);
        evictCount = Math.Max(1, evictCount); // At least 1

        _logger.LogInformation(
            "Cache eviction triggered: {CurrentSize}/{MaxSize} entries. Evicting {EvictCount} LRU entries.",
            currentSize, _options.MaxItems, evictCount);

        // Get LRU entries (oldest access times)
        var keysToEvict = _accessTimes
            .OrderBy(kv => kv.Value)
            .Take(evictCount)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in keysToEvict)
        {
            if (_cache.TryRemove(key, out var entry))
            {
                _accessTimes.TryRemove(key, out _);
                entry.Dispose(); // Clean up semaphore
            }
        }

        _logger.LogInformation("Evicted {Count} cache entries", keysToEvict.Count);
        return Task.CompletedTask;
    }

    private async Task PrimeCache(CacheEntry entry, int target, Func<Task<string>> fetchFunc)
    {
        for (int i = 0; i < target; i++)
        {
            try
            {
                var content = await fetchFunc();
                if (!string.IsNullOrEmpty(content))
                {
                    var contentHash = ComputeContentHash(content);
                    // Use HashSet for O(1) duplicate check instead of Queue.Contains O(n)
                    if (entry.ResponseHashes.TryAdd(contentHash, true))
                    {
                        entry.Responses.Enqueue(content);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error prefetching cached response");
            }
        }
    }

    private async Task RefillCacheAsync(CacheEntry entry, int target, Func<Task<string>> fetchFunc)
    {
        try
        {
            int attempts = 0;
            while (true)
            {
                // Check how many we still need
                await entry.Gate.WaitAsync();
                int missing;
                try
                {
                    missing = target - entry.Responses.Count;
                    if (missing <= 0)
                    {
                        return;
                    }
                }
                finally
                {
                    entry.Gate.Release();
                }

                // Avoid infinite loops
                if (attempts++ > target * 5)
                {
                    return;
                }

                string? content = null;
                try
                {
                    content = await fetchFunc();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Background refill fetch failed");
                    await Task.Delay(50);
                }

                if (string.IsNullOrEmpty(content))
                {
                    continue;
                }

                await entry.Gate.WaitAsync();
                try
                {
                    var contentHash = ComputeContentHash(content);
                    // Use HashSet for O(1) duplicate check
                    if (entry.ResponseHashes.TryAdd(contentHash, true))
                    {
                        entry.Responses.Enqueue(content);
                    }
                }
                finally
                {
                    entry.Gate.Release();
                }
            }
        }
        finally
        {
            await entry.Gate.WaitAsync();
            try { entry.IsRefilling = false; }
            finally { entry.Gate.Release(); }
        }
    }

    private static ulong ComputeCacheKey(string method, string fullPathWithQuery, string? shape)
    {
        var input = Encoding.UTF8.GetBytes(string.Concat(method, "|", fullPathWithQuery, "|", shape ?? string.Empty));
        var hash = XxHash64.Hash(input);
        return BitConverter.ToUInt64(hash, 0);
    }

    /// <summary>
    /// Computes a hash of content for deduplication
    /// </summary>
    private static ulong ComputeContentHash(string content)
    {
        var input = Encoding.UTF8.GetBytes(content);
        var hash = XxHash64.Hash(input);
        return BitConverter.ToUInt64(hash, 0);
    }
}

/// <summary>
/// Cache statistics
/// </summary>
public class CacheStatistics
{
    public int TotalEntries { get; init; }
    public int TotalResponses { get; init; }
    public int MaxEntries { get; init; }
    public int MaxResponsesPerEntry { get; init; }
    public double UtilizationPercent { get; init; }
}
