using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace mostlylucid.mockllmapi.Services;

/// <summary>
///     Extracts JSON structure/shape from response data for autoshape memory.
///     Converts actual data into a template shape that can guide future responses.
/// </summary>
public class ShapeExtractorFromResponse
{
    private readonly ILogger<ShapeExtractorFromResponse> _logger;

    public ShapeExtractorFromResponse(ILogger<ShapeExtractorFromResponse> logger)
    {
        _logger = logger;
    }

    /// <summary>
    ///     Extracts a JSON shape from a response string.
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
            var shapeJson = ExtractShapeAsJson(doc.RootElement);

            if (shapeJson == null)
            {
                _logger.LogDebug("Failed to extract shape from response");
                return null;
            }

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
    ///     Extracts shape directly as JSON string without using reflection-based serialization.
    /// </summary>
    private string? ExtractShapeAsJson(JsonElement element)
    {
        var sb = new StringBuilder();
        if (WriteShapeToBuilder(element, sb))
            return sb.ToString();
        return null;
    }

    /// <summary>
    ///     Writes the shape of a JSON element to a StringBuilder.
    /// </summary>
    private bool WriteShapeToBuilder(JsonElement element, StringBuilder sb)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                return WriteObjectShape(element, sb);
            case JsonValueKind.Array:
                return WriteArrayShape(element, sb);
            case JsonValueKind.String:
                sb.Append("\"\"");
                return true;
            case JsonValueKind.Number:
                WriteNumberShape(element, sb);
                return true;
            case JsonValueKind.True:
            case JsonValueKind.False:
                sb.Append("true");
                return true;
            case JsonValueKind.Null:
                sb.Append("null");
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    ///     Writes an object shape to the StringBuilder.
    /// </summary>
    private bool WriteObjectShape(JsonElement element, StringBuilder sb)
    {
        sb.Append('{');
        var first = true;
        
        foreach (var property in element.EnumerateObject())
        {
            if (!first) sb.Append(',');
            first = false;
            
            sb.Append('"');
            sb.Append(EscapeJsonString(property.Name));
            sb.Append("\":");
            
            if (!WriteShapeToBuilder(property.Value, sb))
                sb.Append("null");
        }
        
        sb.Append('}');
        return true;
    }

    /// <summary>
    ///     Writes an array shape to the StringBuilder (uses first item as template).
    /// </summary>
    private bool WriteArrayShape(JsonElement element, StringBuilder sb)
    {
        sb.Append('[');
        
        var arrayLength = element.GetArrayLength();
        if (arrayLength > 0)
        {
            var firstItem = element.EnumerateArray().First();
            WriteShapeToBuilder(firstItem, sb);
        }
        
        sb.Append(']');
        return true;
    }

    /// <summary>
    ///     Writes a number shape (0 for integers, 0.0 for decimals).
    /// </summary>
    private void WriteNumberShape(JsonElement element, StringBuilder sb)
    {
        if (element.TryGetDecimal(out var decimalValue))
        {
            if (decimalValue != Math.Floor(decimalValue))
            {
                sb.Append("0.0");
                return;
            }
        }
        sb.Append('0');
    }

    /// <summary>
    ///     Escapes a string for JSON output.
    /// </summary>
    private static string EscapeJsonString(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var sb = new StringBuilder();
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < ' ')
                        sb.Append($"\\u{(int)c:X4}");
                    else
                        sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    ///     Validates if a response is suitable for shape extraction.
    ///     Returns false for error responses or malformed data.
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
                    return false;

                if (root.TryGetProperty("errors", out var errorsArray) &&
                    errorsArray.ValueKind == JsonValueKind.Array)
                    return false;

                if (root.TryGetProperty("message", out _) &&
                    root.TryGetProperty("statusCode", out _))
                    return false;
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}