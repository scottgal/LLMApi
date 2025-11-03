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
}
