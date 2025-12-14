using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using mostlylucid.mockllmapi.Models;

namespace mostlylucid.mockllmapi.Services;

/// <summary>
///     Registry for managing pre-configured REST API definitions
///     Provides lookup, listing, and validation of configured APIs
/// </summary>
public class RestApiRegistry
{
    private readonly ConcurrentDictionary<string, RestApiConfig> _apiCache;
    private readonly ILogger<RestApiRegistry> _logger;
    private readonly IOptionsMonitor<LLMockApiOptions> _options;

    public RestApiRegistry(
        ILogger<RestApiRegistry> logger,
        IOptionsMonitor<LLMockApiOptions> options)
    {
        _logger = logger;
        _options = options;
        _apiCache = new ConcurrentDictionary<string, RestApiConfig>(StringComparer.OrdinalIgnoreCase);

        // Load APIs on initialization
        LoadApis();

        // Reload when options change
        _options.OnChange(_ => LoadApis());
    }

    /// <summary>
    ///     Get count of loaded APIs
    /// </summary>
    public int Count => _apiCache.Count(kvp => kvp.Value.Enabled);

    /// <summary>
    ///     Get count of all APIs (including disabled)
    /// </summary>
    public int TotalCount => _apiCache.Count;

    /// <summary>
    ///     Load all configured APIs from options
    /// </summary>
    private void LoadApis()
    {
        _apiCache.Clear();

        var apis = _options.CurrentValue.RestApis ?? new List<RestApiConfig>();
        var loadedCount = 0;
        var errors = new List<string>();

        foreach (var api in apis)
            try
            {
                // Validate configuration
                var validationErrors = ValidateApiConfig(api);
                if (validationErrors.Count > 0)
                {
                    _logger.LogWarning(
                        "Skipping invalid REST API config '{Name}': {Errors}",
                        api.Name,
                        string.Join(", ", validationErrors));
                    errors.AddRange(validationErrors);
                    continue;
                }

                // Add to cache
                if (_apiCache.TryAdd(api.Name, api))
                {
                    loadedCount++;
                    _logger.LogDebug(
                        "Loaded REST API config '{Name}': {Method} {Path}",
                        api.Name, api.Method, api.Path);
                }
                else
                {
                    _logger.LogWarning(
                        "Duplicate REST API config name '{Name}' - using first definition",
                        api.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error loading REST API config '{Name}'",
                    api.Name);
                errors.Add($"{api.Name}: {ex.Message}");
            }

        _logger.LogInformation(
            "Loaded {Count} REST API configurations ({Errors} errors)",
            loadedCount,
            errors.Count);
    }

    /// <summary>
    ///     Get a specific API configuration by name
    /// </summary>
    public RestApiConfig? GetApi(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        _apiCache.TryGetValue(name, out var api);
        return api;
    }

    /// <summary>
    ///     Get all configured APIs
    /// </summary>
    public IReadOnlyList<RestApiConfig> GetAllApis()
    {
        return _apiCache.Values
            .Where(a => a.Enabled)
            .OrderBy(a => a.Name)
            .ToList();
    }

    /// <summary>
    ///     Get APIs filtered by tags
    /// </summary>
    public IReadOnlyList<RestApiConfig> GetApisByTags(params string[] tags)
    {
        if (tags == null || tags.Length == 0)
            return GetAllApis();

        return _apiCache.Values
            .Where(a => a.Enabled && a.HasAnyTag(tags))
            .OrderBy(a => a.Name)
            .ToList();
    }

    /// <summary>
    ///     Get all unique tags across all APIs
    /// </summary>
    public IReadOnlyList<string> GetAllTags()
    {
        return _apiCache.Values
            .SelectMany(a => a.Tags)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t)
            .ToList();
    }

    /// <summary>
    ///     Get APIs grouped by their primary tag
    /// </summary>
    public Dictionary<string, List<RestApiConfig>> GetApisGroupedByTag()
    {
        var groups = new Dictionary<string, List<RestApiConfig>>(StringComparer.OrdinalIgnoreCase);

        foreach (var api in _apiCache.Values.Where(a => a.Enabled))
            if (api.Tags.Count == 0)
            {
                // Add to "untagged" group
                if (!groups.ContainsKey("untagged"))
                    groups["untagged"] = new List<RestApiConfig>();
                groups["untagged"].Add(api);
            }
            else
            {
                // Add to first tag group
                var tag = api.Tags[0];
                if (!groups.ContainsKey(tag))
                    groups[tag] = new List<RestApiConfig>();
                groups[tag].Add(api);
            }

        return groups;
    }

    /// <summary>
    ///     Check if an API with the given name exists
    /// </summary>
    public bool Exists(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        return _apiCache.ContainsKey(name);
    }

    /// <summary>
    ///     Validate an API configuration
    /// </summary>
    private List<string> ValidateApiConfig(RestApiConfig api)
    {
        var errors = new List<string>();

        // Name is required
        if (string.IsNullOrWhiteSpace(api.Name))
            errors.Add("Name is required");

        // Method is required
        if (string.IsNullOrWhiteSpace(api.Method))
            errors.Add("Method is required");
        else if (!IsValidHttpMethod(api.Method))
            errors.Add($"Invalid HTTP method: {api.Method}");

        // Path is required
        if (string.IsNullOrWhiteSpace(api.Path))
            errors.Add("Path is required");

        // Either Shape or OpenApiSpec must be provided
        if (string.IsNullOrWhiteSpace(api.Shape) &&
            string.IsNullOrWhiteSpace(api.OpenApiSpec))
            // This is actually OK - we can generate without a shape
            // Just log a warning, not an error
            _logger.LogDebug(
                "REST API '{Name}' has no Shape or OpenApiSpec - will use LLM default generation",
                api.Name);

        // If OpenApiSpec is specified, OpenApiOperationId should also be specified
        if (!string.IsNullOrWhiteSpace(api.OpenApiSpec) &&
            string.IsNullOrWhiteSpace(api.OpenApiOperationId))
            _logger.LogWarning(
                "REST API '{Name}' specifies OpenApiSpec but not OpenApiOperationId - shape may not be applied",
                api.Name);

        // Validate CacheCount
        if (api.CacheCount.HasValue && api.CacheCount.Value <= 0)
            errors.Add("CacheCount must be greater than 0");

        // Validate NCompletions
        if (api.NCompletions.HasValue && api.NCompletions.Value <= 0)
            errors.Add("NCompletions must be greater than 0");

        return errors;
    }

    /// <summary>
    ///     Check if HTTP method is valid
    /// </summary>
    private bool IsValidHttpMethod(string method)
    {
        var validMethods = new[] { "GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS" };
        return validMethods.Contains(method.ToUpperInvariant());
    }

    /// <summary>
    ///     Get summary information about all APIs
    /// </summary>
    public object GetRegistrySummary()
    {
        var apis = GetAllApis();
        var tags = GetAllTags();
        var groups = GetApisGroupedByTag();

        return new
        {
            totalApis = TotalCount,
            enabledApis = Count,
            disabledApis = TotalCount - Count,
            tags,
            groups = groups.Select(g => new
            {
                tag = g.Key,
                count = g.Value.Count,
                apis = g.Value.Select(a => a.Name).ToList()
            }).ToList(),
            apis = apis.Select(a => a.ToSummary()).ToList()
        };
    }
}