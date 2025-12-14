using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;

namespace mostlylucid.mockllmapi.Services;

/// <summary>
///     Service for loading and parsing OpenAPI specifications from URLs or file paths.
///     Caches parsed specs for reuse across requests.
/// </summary>
public class OpenApiSpecLoader
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenApiSpecLoader> _logger;
    private readonly ConcurrentDictionary<string, OpenApiDocument> _specCache;

    public OpenApiSpecLoader(ILogger<OpenApiSpecLoader> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();
        _specCache = new ConcurrentDictionary<string, OpenApiDocument>();
    }

    /// <summary>
    ///     Loads an OpenAPI specification from a URL or file path.
    ///     Results are cached for subsequent requests.
    /// </summary>
    /// <param name="source">URL or file path to the OpenAPI spec (YAML or JSON)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Parsed OpenAPI document</returns>
    public async Task<OpenApiDocument> LoadSpecAsync(string source, CancellationToken cancellationToken = default)
    {
        // Check cache first
        if (_specCache.TryGetValue(source, out var cachedSpec))
        {
            _logger.LogDebug("Using cached OpenAPI spec for: {Source}", source);
            return cachedSpec;
        }

        _logger.LogInformation("Loading OpenAPI spec from: {Source}", source);

        try
        {
            Stream stream;

            // Determine if source is URL or file path
            if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                // Load from URL
                var response = await _httpClient.GetAsync(source, cancellationToken);
                response.EnsureSuccessStatusCode();
                stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            }
            else
            {
                // Load from file
                if (!File.Exists(source)) throw new FileNotFoundException($"OpenAPI spec file not found: {source}");
                stream = File.OpenRead(source);
            }

            using (stream)
            {
                // Parse the OpenAPI document
                var reader = new OpenApiStreamReader();
                var document = reader.Read(stream, out var diagnostic);

                if (diagnostic.Errors.Count > 0)
                {
                    var errors = string.Join(", ", diagnostic.Errors.Select(e => e.Message));
                    _logger.LogWarning("OpenAPI spec has errors: {Errors}", errors);
                }

                // Resolve all $ref references in the document
                // This walks the document and replaces reference objects with their resolved schemas
                try
                {
                    document.ResolveReferences();
                    _logger.LogDebug("Successfully resolved all $ref references in OpenAPI spec");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to resolve some references in OpenAPI spec, continuing anyway");
                }

                // Cache the parsed document
                _specCache[source] = document;

                _logger.LogInformation("Successfully loaded OpenAPI spec: {Title} v{Version} with {PathCount} paths",
                    document.Info?.Title ?? "Unknown",
                    document.Info?.Version ?? "Unknown",
                    document.Paths?.Count ?? 0);

                return document;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load OpenAPI spec from: {Source}", source);
            throw;
        }
    }

    /// <summary>
    ///     Gets all path definitions from an OpenAPI document.
    /// </summary>
    public IEnumerable<(string Path, OperationType Method, OpenApiOperation Operation)> GetOperations(
        OpenApiDocument document)
    {
        if (document.Paths == null) yield break;

        foreach (var path in document.Paths)
        foreach (var operation in path.Value.Operations)
            yield return (path.Key, operation.Key, operation.Value);
    }

    /// <summary>
    ///     Clears the specification cache.
    /// </summary>
    public void ClearCache()
    {
        _specCache.Clear();
        _logger.LogInformation("OpenAPI spec cache cleared");
    }

    /// <summary>
    ///     Removes a specific spec from the cache.
    /// </summary>
    public bool RemoveFromCache(string source)
    {
        var removed = _specCache.TryRemove(source, out _);
        if (removed) _logger.LogDebug("Removed OpenAPI spec from cache: {Source}", source);
        return removed;
    }
}