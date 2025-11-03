using System.Collections.Concurrent;
using System.IO.Hashing;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using mostlylucid.mockllmapi.Models;

namespace mostlylucid.mockllmapi.Services;

/// <summary>
/// Manages pre-caching of LLM responses for improved performance
/// </summary>
public class CacheManager(IOptions<LLMockApiOptions> options, ILogger<CacheManager> logger)
{
    private readonly LLMockApiOptions _options = options.Value;
    private readonly ConcurrentDictionary<ulong, CacheEntry> _cache = new();

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

        var key = ComputeCacheKey(method, fullPathWithQuery, shape);
        var entry = _cache.GetOrAdd(key, _ => new CacheEntry());
        var target = Math.Max(1, Math.Min(cacheCount, _options.MaxCachePerKey));

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

    private async Task PrimeCache(CacheEntry entry, int target, Func<Task<string>> fetchFunc)
    {
        for (int i = 0; i < target; i++)
        {
            try
            {
                var content = await fetchFunc();
                if (!string.IsNullOrEmpty(content) && !entry.Responses.Contains(content))
                {
                    entry.Responses.Enqueue(content);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error prefetching cached response");
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
                    logger.LogWarning(ex, "Background refill fetch failed");
                    await Task.Delay(50);
                }

                if (string.IsNullOrEmpty(content))
                {
                    continue;
                }

                await entry.Gate.WaitAsync();
                try
                {
                    if (!entry.Responses.Contains(content))
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
}
