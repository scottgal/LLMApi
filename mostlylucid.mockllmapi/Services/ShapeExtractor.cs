using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using mostlylucid.mockllmapi.Models;

namespace mostlylucid.mockllmapi.Services;

/// <summary>
/// Extracts and processes JSON shape/schema information from requests
/// </summary>
public class ShapeExtractor
{
    /// <summary>
    /// Extracts shape information from the request, including cache hints and JSON Schema detection
    /// </summary>
    public ShapeInfo ExtractShapeInfo(HttpRequest request, string? body)
    {
        var shapeText = ExtractShapeText(request, body);

        if (string.IsNullOrWhiteSpace(shapeText))
        {
            return new ShapeInfo();
        }

        var isJsonSchema = DetectJsonSchema(shapeText);
        var (sanitizedShape, cacheCount) = ExtractCacheHintAndSanitize(shapeText);

        return new ShapeInfo
        {
            Shape = sanitizedShape,
            CacheCount = cacheCount,
            IsJsonSchema = isJsonSchema
        };
    }

    /// <summary>
    /// Extracts raw shape text from query param, header, or body
    /// </summary>
    private string? ExtractShapeText(HttpRequest request, string? body)
    {
        // 1) Query parameter
        if (request.Query.TryGetValue("shape", out var shapeQuery) && shapeQuery.Count > 0)
        {
            return shapeQuery[0];
        }

        // 2) Header
        if (request.Headers.TryGetValue("X-Response-Shape", out var shapeHeader) && shapeHeader.Count > 0)
        {
            return shapeHeader[0];
        }

        // 3) Body property
        if (!string.IsNullOrWhiteSpace(body) &&
            request.ContentType != null &&
            request.ContentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("shape", out var shapeNode))
                {
                    return shapeNode.GetRawText();
                }
            }
            catch
            {
                // ignore JSON parse errors
            }
        }

        return null;
    }

    /// <summary>
    /// Detects if the shape is a JSON Schema (vs descriptive shape)
    /// </summary>
    private bool DetectJsonSchema(string shapeText)
    {
        try
        {
            using var doc = JsonDocument.Parse(shapeText);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                // JSON Schema indicators
                return doc.RootElement.TryGetProperty("$schema", out _) ||
                       doc.RootElement.TryGetProperty("type", out _) ||
                       doc.RootElement.TryGetProperty("properties", out _);
            }
        }
        catch
        {
            // ignore parse errors
        }

        return false;
    }

    /// <summary>
    /// Extracts cache hints ($cache, $cacheCount, cache) and returns sanitized shape
    /// </summary>
    private (string? sanitizedShape, int cacheCount) ExtractCacheHintAndSanitize(string shapeText)
    {
        int cacheCount = 0;

        try
        {
            using var doc = JsonDocument.Parse(shapeText);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return (shapeText, 0); // not an object, cannot contain cache hint
            }

            // Build sanitized object without cache hints
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    var name = prop.Name;
                    if (string.Equals(name, "$cache", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, "$cacheCount", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, "cache", StringComparison.OrdinalIgnoreCase))
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt32(out var n) && n > 0)
                        {
                            cacheCount = n;
                        }
                        else if (prop.Value.ValueKind == JsonValueKind.String && int.TryParse(prop.Value.GetString(), out var ns) && ns > 0)
                        {
                            cacheCount = ns;
                        }
                        continue; // skip writing cache hint
                    }
                    prop.WriteTo(writer);
                }
                writer.WriteEndObject();
            }

            var sanitized = Encoding.UTF8.GetString(stream.ToArray());
            return (sanitized, cacheCount);
        }
        catch
        {
            return (shapeText, 0); // if invalid JSON, ignore cache count
        }
    }
}
