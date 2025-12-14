using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using mostlylucid.mockllmapi.Models;

namespace mostlylucid.mockllmapi.Services;

/// <summary>
///     Selects LLM backend for requests
///     Supports multiple selection strategies: round-robin, priority-based, failover, per-request selection
/// </summary>
public class LlmBackendSelector
{
    private readonly object _lock = new();
    private readonly ILogger<LlmBackendSelector> _logger;
    private readonly LLMockApiOptions _options;
    private int _roundRobinIndex;

    public LlmBackendSelector(
        IOptions<LLMockApiOptions> options,
        ILogger<LlmBackendSelector> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    ///     Gets the current backend configuration
    ///     Supports per-request selection via X-LLM-Backend header or ?backend= query parameter
    ///     Falls back to round-robin selection
    /// </summary>
    public LlmBackendConfig? SelectBackend(HttpRequest? request = null)
    {
        // Check for per-request backend selection
        if (request != null)
        {
            var requestedBackend = GetRequestedBackend(request);
            if (!string.IsNullOrWhiteSpace(requestedBackend))
            {
                var backend = GetBackendByName(requestedBackend);
                if (backend != null)
                {
                    _logger.LogInformation(
                        "Using requested backend: {BackendName} ({Provider}, {BaseUrl})",
                        backend.Name, backend.Provider, backend.BaseUrl);
                    return backend;
                }

                _logger.LogWarning(
                    "Requested backend '{BackendName}' not found or disabled, using default selection",
                    requestedBackend);
            }
        }

        // If new LlmBackends is configured, use it
        if (_options.LlmBackends.Count > 0)
        {
            var enabledBackends = _options.LlmBackends
                .Where(b => b.Enabled && !string.IsNullOrWhiteSpace(b.BaseUrl))
                .ToList();

            if (enabledBackends.Count == 0)
            {
                _logger.LogWarning(
                    "No enabled LLM backends configured. Falling back to BaseUrl/ModelName");
                return CreateLegacyBackend();
            }

            // Simple round-robin for now
            var backend = SelectRoundRobin(enabledBackends);

            _logger.LogDebug(
                "Selected backend: {BackendName} ({Provider}, {BaseUrl}, model: {ModelName})",
                backend.Name, backend.Provider, backend.BaseUrl, backend.ModelName);

            return backend;
        }

        // Fall back to legacy single backend configuration
        _logger.LogDebug(
            "Using legacy single backend: {BaseUrl}, model: {ModelName}",
            _options.BaseUrl, _options.ModelName);

        return CreateLegacyBackend();
    }

    /// <summary>
    ///     Extracts requested backend from request headers or query parameters
    /// </summary>
    private string? GetRequestedBackend(HttpRequest request)
    {
        // Check header first (X-LLM-Backend: backend-name)
        if (request.Headers.TryGetValue("X-LLM-Backend", out var headerValue) &&
            !string.IsNullOrWhiteSpace(headerValue))
            return headerValue.ToString();

        // Check query parameter (?backend=backend-name)
        if (request.Query.TryGetValue("backend", out var queryValue) &&
            !string.IsNullOrWhiteSpace(queryValue))
            return queryValue.ToString();

        return null;
    }

    /// <summary>
    ///     Gets a specific backend by name
    /// </summary>
    public LlmBackendConfig? GetBackendByName(string name)
    {
        return _options.LlmBackends
            .FirstOrDefault(b => b.Enabled &&
                                 b.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     Creates a backend config from legacy BaseUrl/ModelName settings
    /// </summary>
    private LlmBackendConfig CreateLegacyBackend()
    {
        return new LlmBackendConfig
        {
            Name = "default",
            Provider = "ollama",
            BaseUrl = _options.BaseUrl,
            ModelName = _options.ModelName,
            Enabled = true
        };
    }

    /// <summary>
    ///     Round-robin selection across backends
    /// </summary>
    private LlmBackendConfig SelectRoundRobin(List<LlmBackendConfig> backends)
    {
        if (backends.Count == 1)
            return backends[0];

        lock (_lock)
        {
            var index = _roundRobinIndex % backends.Count;
            _roundRobinIndex++;
            return backends[index];
        }
    }

    /// <summary>
    ///     Gets all configured backends (for monitoring/health checks)
    /// </summary>
    public List<LlmBackendConfig> GetAllBackends()
    {
        return _options.LlmBackends.ToList();
    }

    /// <summary>
    ///     Checks if multiple backends are configured
    /// </summary>
    public bool HasMultipleBackends()
    {
        return _options.LlmBackends.Count(b => b.Enabled) > 1;
    }

    /// <summary>
    ///     Selects a backend from a list of backend names using weighted round-robin
    ///     Falls back to default selection if none of the named backends are available
    /// </summary>
    public LlmBackendConfig? SelectFromBackends(string[] backendNames)
    {
        if (backendNames == null || backendNames.Length == 0) return SelectBackend();

        // Get all enabled backends that match the requested names
        var matchingBackends = _options.LlmBackends
            .Where(b => b.Enabled &&
                        backendNames.Any(name => b.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (matchingBackends.Count == 0)
        {
            _logger.LogWarning(
                "None of the requested backends are available: [{Backends}], using default selection",
                string.Join(", ", backendNames));
            return SelectBackend();
        }

        if (matchingBackends.Count == 1)
        {
            _logger.LogDebug("Single backend available from requested list: {BackendName}", matchingBackends[0].Name);
            return matchingBackends[0];
        }

        // Weighted round-robin selection
        var backend = SelectWeightedRoundRobin(matchingBackends);

        _logger.LogDebug(
            "Selected backend {BackendName} from pool: [{Pool}]",
            backend.Name, string.Join(", ", matchingBackends.Select(b => b.Name)));

        return backend;
    }

    /// <summary>
    ///     Weighted round-robin selection based on backend weights
    /// </summary>
    private LlmBackendConfig SelectWeightedRoundRobin(List<LlmBackendConfig> backends)
    {
        // Calculate total weight
        var totalWeight = backends.Sum(b => Math.Max(1, b.Weight));

        lock (_lock)
        {
            // Simple round-robin for now - can be enhanced to true weighted selection
            var index = _roundRobinIndex % backends.Count;
            _roundRobinIndex++;
            return backends[index];
        }

        // TODO: Implement true weighted round-robin:
        // 1. Build cumulative weight array
        // 2. Generate random number 0 to totalWeight
        // 3. Select backend based on which range the random number falls into
        // This would respect Weight property more accurately
    }
}