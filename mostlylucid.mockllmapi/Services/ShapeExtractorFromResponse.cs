using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace mostlylucid.mockllmapi.Services;

/// <summary>
/// Extracts JSON structure/shape from response data for autoshape memory.
/// Converts actual data into a template shape that can guide future responses.
/// </summary>
public class ShapeExtractorFromResponse
{
    private readonly ILogger<ShapeExtractorFromResponse> _logger;

    public ShapeExtractorFromResponse(ILogger<ShapeExtractorFromResponse> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Extracts a JSON shape from a response string.
    /// </summary>
    /// <param name="jsonResponse">The JSON response to extract shape from</param>
    /// <returns>A simplified JSON shape template, or null if extraction fails</returns>
    public string? ExtractShape(string jsonResponse)
    {
        if (string.IsNullOrWhiteSpace(jsonResponse))
        {
            _logger.LogDebug("Cannot extract shape from empty response");
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(jsonResponse);
            var shape = ExtractShapeFromElement(doc.RootElement);

            if (shape == null)
            {
                _logger.LogDebug("Failed to extract shape from response");
                return null;
            }

            var shapeJson = JsonSerializer.Serialize(shape, new JsonSerializerOptions
            {
                WriteIndented = false // Compact format for storage
            });

            _logger.LogDebug("Extracted shape from response: {Shape}", shapeJson);
            return shapeJson;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse JSON response for shape extraction");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while extracting shape from response");
            return null;
        }
    }

    /// <summary>
    /// Recursively extracts shape from a JSON element.
    /// </summary>
    private object? ExtractShapeFromElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => ExtractObjectShape(element),
            JsonValueKind.Array => ExtractArrayShape(element),
            JsonValueKind.String => "",
            JsonValueKind.Number => DetermineNumberType(element),
            JsonValueKind.True or JsonValueKind.False => true,
            JsonValueKind.Null => null,
            _ => null
        };
    }

    /// <summary>
    /// Extracts shape from an object by creating a template with all properties.
    /// </summary>
    private Dictionary<string, object?>? ExtractObjectShape(JsonElement element)
    {
        var shape = new Dictionary<string, object?>();

        foreach (var property in element.EnumerateObject())
        {
            var propertyShape = ExtractShapeFromElement(property.Value);
            shape[property.Name] = propertyShape;
        }

        return shape.Count > 0 ? shape : null;
    }

    /// <summary>
    /// Extracts shape from an array by using the first item as a template.
    /// For arrays of objects, extracts the object shape. For primitive arrays, uses a sample value.
    /// </summary>
    private object[]? ExtractArrayShape(JsonElement element)
    {
        var arrayLength = element.GetArrayLength();

        if (arrayLength == 0)
        {
            // Empty array - return a generic array indicator
            return Array.Empty<object>();
        }

        // Use the first item as the template
        var firstItem = element.EnumerateArray().First();
        var itemShape = ExtractShapeFromElement(firstItem);

        if (itemShape == null)
        {
            return Array.Empty<object>();
        }

        // Return an array with a single template item
        return new[] { itemShape };
    }

    /// <summary>
    /// Determines whether a number should be represented as integer (0) or decimal (0.0).
    /// </summary>
    private object DetermineNumberType(JsonElement element)
    {
        // Try to get as decimal to check for fractional part
        if (element.TryGetDecimal(out var decimalValue))
        {
            // If it has a fractional part, use 0.0 as template
            if (decimalValue != Math.Floor(decimalValue))
            {
                return 0.0;
            }
        }

        // Otherwise, use 0 as template (integer)
        return 0;
    }

    /// <summary>
    /// Validates if a response is suitable for shape extraction.
    /// Returns false for error responses or malformed data.
    /// </summary>
    public bool IsValidForShapeExtraction(string jsonResponse)
    {
        if (string.IsNullOrWhiteSpace(jsonResponse))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(jsonResponse);
            var root = doc.RootElement;

            // Reject error responses (common error object patterns)
            if (root.ValueKind == JsonValueKind.Object)
            {
                // Check for common error indicators
                if (root.TryGetProperty("error", out _) &&
                    !root.TryGetProperty("data", out _)) // Allow GraphQL responses with both
                {
                    return false;
                }

                if (root.TryGetProperty("errors", out var errorsArray) &&
                    errorsArray.ValueKind == JsonValueKind.Array)
                {
                    return false;
                }

                if (root.TryGetProperty("message", out _) &&
                    root.TryGetProperty("statusCode", out _))
                {
                    return false;
                }
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
