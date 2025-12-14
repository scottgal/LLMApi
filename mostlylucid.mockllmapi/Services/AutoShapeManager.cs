using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using mostlylucid.mockllmapi.Models;

namespace mostlylucid.mockllmapi.Services;

/// <summary>
/// Manages automatic shape memory for endpoints.
/// Coordinates shape storage, retrieval, and per-request configuration.
/// </summary>
public class AutoShapeManager
{
    private readonly LLMockApiOptions _options;
    private readonly IShapeStore _shapeStore;
    private readonly ShapeExtractorFromResponse _shapeExtractor;
    private readonly ILogger<AutoShapeManager> _logger;

    public AutoShapeManager(
        IOptions<LLMockApiOptions> options,
        IShapeStore shapeStore,
        ShapeExtractorFromResponse shapeExtractor,
        ILogger<AutoShapeManager> logger)
    {
        _options = options.Value;
        _shapeStore = shapeStore;
        _shapeExtractor = shapeExtractor;
        _logger = logger;
    }

    /// <summary>
    /// Checks if autoshape is enabled for a specific request.
    /// Considers both global configuration and per-request overrides.
    /// </summary>
    /// <param name="request">The HTTP request</param>
    /// <returns>True if autoshape should be active for this request</returns>
    public bool IsAutoShapeEnabled(HttpRequest? request)
    {
        // Start with global configuration
        var enabled = _options.EnableAutoShape;

        // Check for per-request override
        if (request != null)
        {
            // Query parameter override: ?autoshape=true or ?autoshape=false
            if (request.Query.TryGetValue("autoshape", out var queryValue) && queryValue.Count > 0)
            {
                if (bool.TryParse(queryValue[0], out var queryOverride))
                {
                    enabled = queryOverride;
                    _logger.LogDebug("AutoShape overridden via query parameter: {Enabled}", enabled);
                }
            }
            // Header override: X-Auto-Shape: true or X-Auto-Shape: false
            else if (request.Headers.TryGetValue("X-Auto-Shape", out var headerValue) && headerValue.Count > 0)
            {
                if (bool.TryParse(headerValue[0], out var headerOverride))
                {
                    enabled = headerOverride;
                    _logger.LogDebug("AutoShape overridden via header: {Enabled}", enabled);
                }
            }
        }

        return enabled;
    }

    /// <summary>
    /// Attempts to retrieve a stored shape for the given request path.
    /// Only retrieves if autoshape is enabled and no explicit shape is provided.
    /// </summary>
    /// <param name="request">The HTTP request</param>
    /// <param name="existingShapeInfo">The shape info extracted from the request (may have explicit shape)</param>
    /// <returns>A stored shape if found and applicable, otherwise null</returns>
    public string? GetShapeForRequest(HttpRequest request, ShapeInfo existingShapeInfo)
    {
        // Don't apply autoshape if an explicit shape is already provided
        if (!string.IsNullOrWhiteSpace(existingShapeInfo.Shape))
        {
            _logger.LogDebug("Skipping autoshape: explicit shape provided in request");
            return null;
        }

        // Check if autoshape is enabled for this request
        if (!IsAutoShapeEnabled(request))
        {
            return null;
        }

        // Check for renewshape parameter - if true, skip retrieval to force new shape generation
        if (IsRenewShapeRequested(request))
        {
            var normalizedPath = PathNormalizer.NormalizePath(request.Path.Value ?? "/");
            _logger.LogInformation(
                "Renewing shape for path '{NormalizedPath}' (original: '{OriginalPath}')",
                normalizedPath, request.Path.Value);
            return null; // Don't retrieve stored shape, forcing new generation
        }

        // Normalize the path
        var normalizedPath2 = PathNormalizer.NormalizePath(request.Path.Value ?? "/");

        // Try to retrieve stored shape
        if (_shapeStore.TryGetValue(normalizedPath2, out var storedShape) && !string.IsNullOrWhiteSpace(storedShape))
        {
            _logger.LogInformation(
                "Retrieved autoshape for path '{NormalizedPath}' (original: '{OriginalPath}')",
                normalizedPath2, request.Path.Value);
            return storedShape;
        }

        _logger.LogDebug("No autoshape found for path '{NormalizedPath}'", normalizedPath2);
        return null;
    }

    /// <summary>
    /// Checks if the request is asking to renew/replace the stored shape.
    /// </summary>
    /// <param name="request">The HTTP request</param>
    /// <returns>True if renewshape is requested</returns>
    private bool IsRenewShapeRequested(HttpRequest? request)
    {
        if (request == null)
            return false;

        // Check query parameter: ?renewshape=true
        if (request.Query.TryGetValue("renewshape", out var queryValue) && queryValue.Count > 0)
        {
            if (bool.TryParse(queryValue[0], out var queryRenew))
            {
                return queryRenew;
            }
        }

        // Check header: X-Renew-Shape: true
        if (request.Headers.TryGetValue("X-Renew-Shape", out var headerValue) && headerValue.Count > 0)
        {
            if (bool.TryParse(headerValue[0], out var headerRenew))
            {
                return headerRenew;
            }
        }

        return false;
    }

    /// <summary>
    /// Stores the shape extracted from a response for future requests to the same endpoint.
    /// Only stores if autoshape is enabled and the response is valid.
    /// </summary>
    /// <param name="request">The HTTP request that generated the response</param>
    /// <param name="jsonResponse">The JSON response to extract shape from</param>
    public void StoreShapeFromResponse(HttpRequest request, string jsonResponse)
    {
        // Check if autoshape is enabled for this request
        if (!IsAutoShapeEnabled(request))
        {
            return;
        }

        // Validate response is suitable for shape extraction
        if (!_shapeExtractor.IsValidForShapeExtraction(jsonResponse))
        {
            _logger.LogDebug("Response not suitable for shape extraction (likely error response)");
            return;
        }

        // Extract shape from response
        var extractedShape = _shapeExtractor.ExtractShape(jsonResponse);
        if (string.IsNullOrWhiteSpace(extractedShape))
        {
            _logger.LogDebug("Failed to extract shape from response");
            return;
        }

        // Normalize the path
        var normalizedPath = PathNormalizer.NormalizePath(request.Path.Value ?? "/");

        // Check if we already have a shape for this path
        if (_shapeStore.TryGetValue(normalizedPath, out var existingShape) && !string.IsNullOrWhiteSpace(existingShape))
        {
            // Don't overwrite existing shape - first response wins
            _logger.LogDebug(
                "Shape already exists for path '{NormalizedPath}', keeping existing shape",
                normalizedPath);

            // Touch the shape to refresh expiration
            _shapeStore.TouchShape(normalizedPath);
            return;
        }

        // Store the new shape
        _shapeStore.Set(normalizedPath, extractedShape);
        _logger.LogInformation(
            "Stored autoshape for path '{NormalizedPath}' (original: '{OriginalPath}')",
            normalizedPath, request.Path.Value);
    }

    /// <summary>
    /// Clears all stored shapes.
    /// </summary>
    public void ClearAllShapes()
    {
        _shapeStore.Clear();
        _logger.LogInformation("Cleared all autoshapes from memory");
    }

    /// <summary>
    /// Gets the count of stored shapes.
    /// </summary>
    public int GetStoredShapeCount()
    {
        return _shapeStore.Count;
    }

    /// <summary>
    /// Gets all normalized paths that have stored shapes.
    /// </summary>
    public IEnumerable<string> GetStoredPaths()
    {
        return _shapeStore.GetAllPaths();
    }

    /// <summary>
    /// Removes the stored shape for a specific path.
    /// </summary>
    /// <param name="path">The path (will be normalized)</param>
    /// <returns>True if a shape was removed, false otherwise</returns>
    public bool RemoveShape(string path)
    {
        var normalizedPath = PathNormalizer.NormalizePath(path);
        var removed = _shapeStore.TryRemove(normalizedPath, out _);

        if (removed)
        {
            _logger.LogInformation("Removed autoshape for path '{NormalizedPath}'", normalizedPath);
        }

        return removed;
    }
}
