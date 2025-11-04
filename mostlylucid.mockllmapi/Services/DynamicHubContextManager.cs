using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using mostlylucid.mockllmapi.Models;

namespace mostlylucid.mockllmapi.Services;

/// <summary>
/// Manages dynamically created hub contexts at runtime
/// </summary>
public class DynamicHubContextManager(ILogger<DynamicHubContextManager> logger)
{
    private readonly ConcurrentDictionary<string, HubContextConfig> _dynamicContexts = new();

    /// <summary>
    /// Registers a new dynamic hub context
    /// </summary>
    public bool RegisterContext(HubContextConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Name))
        {
            logger.LogWarning("Cannot register context with empty name");
            return false;
        }

        var added = _dynamicContexts.TryAdd(config.Name, config);

        if (added)
        {
            logger.LogInformation("Registered dynamic context: {ContextName} ({Method} {Path})",
                config.Name, config.Method, config.Path);
        }
        else
        {
            logger.LogWarning("Context {ContextName} already exists", config.Name);
        }

        return added;
    }

    /// <summary>
    /// Unregisters a dynamic hub context
    /// </summary>
    public bool UnregisterContext(string contextName)
    {
        var removed = _dynamicContexts.TryRemove(contextName, out _);

        if (removed)
        {
            logger.LogInformation("Unregistered dynamic context: {ContextName}", contextName);
        }

        return removed;
    }

    /// <summary>
    /// Gets a specific context configuration
    /// </summary>
    public HubContextConfig? GetContext(string contextName)
    {
        _dynamicContexts.TryGetValue(contextName, out var config);
        return config;
    }

    /// <summary>
    /// Gets all registered dynamic contexts
    /// </summary>
    public IReadOnlyCollection<HubContextConfig> GetAllContexts()
    {
        return _dynamicContexts.Values.ToList();
    }

    /// <summary>
    /// Checks if a context exists
    /// </summary>
    public bool ContextExists(string contextName)
    {
        return _dynamicContexts.ContainsKey(contextName);
    }

    /// <summary>
    /// Starts (activates) a context
    /// </summary>
    public bool StartContext(string contextName)
    {
        if (_dynamicContexts.TryGetValue(contextName, out var config))
        {
            config.IsActive = true;
            logger.LogInformation("Started context: {ContextName}", contextName);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Stops (deactivates) a context
    /// </summary>
    public bool StopContext(string contextName)
    {
        if (_dynamicContexts.TryGetValue(contextName, out var config))
        {
            config.IsActive = false;
            logger.LogInformation("Stopped context: {ContextName}", contextName);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Increments the connection count for a context
    /// </summary>
    public void IncrementConnectionCount(string contextName)
    {
        if (_dynamicContexts.TryGetValue(contextName, out var config))
        {
            config.ConnectionCount++;
            logger.LogInformation("Context {ContextName} connection count incremented to: {Count}", contextName, config.ConnectionCount);
        }
        else
        {
            logger.LogWarning("Attempted to increment connection count for non-existent context: {ContextName}. Available contexts: {Contexts}",
                contextName, string.Join(", ", _dynamicContexts.Keys));
        }
    }

    /// <summary>
    /// Decrements the connection count for a context
    /// </summary>
    public void DecrementConnectionCount(string contextName)
    {
        if (_dynamicContexts.TryGetValue(contextName, out var config))
        {
            config.ConnectionCount = Math.Max(0, config.ConnectionCount - 1);
            logger.LogDebug("Context {ContextName} connection count: {Count}", contextName, config.ConnectionCount);
        }
    }
}
