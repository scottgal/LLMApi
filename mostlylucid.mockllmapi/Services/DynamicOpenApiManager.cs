using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;

namespace mostlylucid.mockllmapi.Services;

/// <summary>
///     Manages dynamically loaded OpenAPI specifications at runtime
/// </summary>
public class DynamicOpenApiManager
{
    private readonly ConcurrentDictionary<string, LoadedSpec> _loadedSpecs;
    private readonly ILogger<DynamicOpenApiManager> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public DynamicOpenApiManager(
        ILogger<DynamicOpenApiManager> logger,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _loadedSpecs = new ConcurrentDictionary<string, LoadedSpec>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Loads an OpenAPI spec dynamically and tracks it
    /// </summary>
    public async Task<SpecLoadResult> LoadSpecAsync(string name, string source, string? basePath = null,
        string? contextName = null, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Loading OpenAPI spec: {Name} from {Source}", name, source);

            // Create a scope to resolve scoped services
            using var scope = _scopeFactory.CreateScope();
            var specLoader = scope.ServiceProvider.GetRequiredService<OpenApiSpecLoader>();

            // Load and parse the spec
            var document = await specLoader.LoadSpecAsync(source, cancellationToken);

            // Extract operations
            var operations = specLoader.GetOperations(document).ToList();

            // Determine base path
            var effectiveBasePath = basePath ?? document.Servers?.FirstOrDefault()?.Url ?? "/api";

            // Build endpoint info
            var endpoints = operations.Select(op => new EndpointInfo
            {
                Path = $"{effectiveBasePath.TrimEnd('/')}{op.Path}",
                Method = op.Method.ToString().ToUpper(),
                OperationId = op.Operation.OperationId,
                Summary = op.Operation.Summary,
                Tags = op.Operation.Tags?.Select(t => t.Name).ToList() ?? new List<string>()
            }).ToList();

            // Store the loaded spec
            var loadedSpec = new LoadedSpec
            {
                Name = name,
                Source = source,
                BasePath = effectiveBasePath,
                ContextName = contextName,
                Document = document,
                Endpoints = endpoints,
                LoadedAt = DateTimeOffset.UtcNow
            };

            _loadedSpecs[name] = loadedSpec;

            _logger.LogInformation("Successfully loaded OpenAPI spec '{Name}' with {Count} endpoints", name,
                endpoints.Count);

            return new SpecLoadResult
            {
                Success = true,
                SpecName = name,
                EndpointCount = endpoints.Count,
                Endpoints = endpoints,
                Info = new SpecInfo
                {
                    Title = document.Info?.Title,
                    Version = document.Info?.Version,
                    Description = document.Info?.Description
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load OpenAPI spec '{Name}' from {Source}", name, source);
            return new SpecLoadResult
            {
                Success = false,
                SpecName = name,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    ///     Gets all loaded specs
    /// </summary>
    public List<LoadedSpecSummary> GetAllSpecs()
    {
        return _loadedSpecs.Values.Select(spec => new LoadedSpecSummary
        {
            Name = spec.Name,
            Source = spec.Source,
            BasePath = spec.BasePath,
            ContextName = spec.ContextName,
            EndpointCount = spec.Endpoints.Count,
            LoadedAt = spec.LoadedAt,
            Info = new SpecInfo
            {
                Title = spec.Document.Info?.Title,
                Version = spec.Document.Info?.Version,
                Description = spec.Document.Info?.Description
            }
        }).ToList();
    }

    /// <summary>
    ///     Gets a specific loaded spec
    /// </summary>
    public LoadedSpec? GetSpec(string name)
    {
        _loadedSpecs.TryGetValue(name, out var spec);
        return spec;
    }

    /// <summary>
    ///     Removes a loaded spec
    /// </summary>
    public bool RemoveSpec(string name)
    {
        var removed = _loadedSpecs.TryRemove(name, out var spec);
        if (removed && spec != null)
        {
            _logger.LogInformation("Removed OpenAPI spec: {Name}", name);
            // Also clear from spec loader cache
            using var scope = _scopeFactory.CreateScope();
            var specLoader = scope.ServiceProvider.GetRequiredService<OpenApiSpecLoader>();
            specLoader.RemoveFromCache(spec.Source);
        }

        return removed;
    }

    /// <summary>
    ///     Reloads a spec (useful if the source has changed)
    /// </summary>
    public async Task<SpecLoadResult> ReloadSpecAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!_loadedSpecs.TryGetValue(name, out var existing))
            return new SpecLoadResult
            {
                Success = false,
                SpecName = name,
                Error = "Spec not found"
            };

        // Clear from cache first
        using (var scope = _scopeFactory.CreateScope())
        {
            var specLoader = scope.ServiceProvider.GetRequiredService<OpenApiSpecLoader>();
            specLoader.RemoveFromCache(existing.Source);
        }

        // Reload with same contextName
        return await LoadSpecAsync(name, existing.Source, existing.BasePath, existing.ContextName, cancellationToken);
    }
}

/// <summary>
///     Represents a loaded OpenAPI specification
/// </summary>
public class LoadedSpec
{
    public string Name { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string BasePath { get; set; } = string.Empty;
    public string? ContextName { get; set; }
    public OpenApiDocument Document { get; set; } = null!;
    public List<EndpointInfo> Endpoints { get; set; } = new();
    public DateTimeOffset LoadedAt { get; set; }
}

/// <summary>
///     Summary of a loaded spec (without the full document)
/// </summary>
public class LoadedSpecSummary
{
    public string Name { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string BasePath { get; set; } = string.Empty;
    public string? ContextName { get; set; }
    public int EndpointCount { get; set; }
    public DateTimeOffset LoadedAt { get; set; }
    public SpecInfo? Info { get; set; }
}

/// <summary>
///     Information about an API endpoint
/// </summary>
public class EndpointInfo
{
    public string Path { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string? OperationId { get; set; }
    public string? Summary { get; set; }
    public List<string> Tags { get; set; } = new();
}

/// <summary>
///     Result of loading a spec
/// </summary>
public class SpecLoadResult
{
    public bool Success { get; set; }
    public string SpecName { get; set; } = string.Empty;
    public int EndpointCount { get; set; }
    public List<EndpointInfo>? Endpoints { get; set; }
    public SpecInfo? Info { get; set; }
    public string? Error { get; set; }
}

/// <summary>
///     OpenAPI spec info
/// </summary>
public class SpecInfo
{
    public string? Title { get; set; }
    public string? Version { get; set; }
    public string? Description { get; set; }
}