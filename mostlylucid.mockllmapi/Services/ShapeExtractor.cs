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
    /// Extracts shape information from the request, including cache hints, error config, and JSON Schema detection
    /// </summary>
    public ShapeInfo ExtractShapeInfo(HttpRequest request, string? body)
    {
        var shapeText = ExtractShapeText(request, body);
        var errorConfig = ExtractErrorConfig(request, body, shapeText);

        if (string.IsNullOrWhiteSpace(shapeText))
        {
            return new ShapeInfo
            {
                ErrorConfig = errorConfig
            };
        }

        var isJsonSchema = DetectJsonSchema(shapeText);
        var (sanitizedShape, cacheCount) = ExtractCacheHintAndSanitize(shapeText);
        sanitizedShape = SanitizeErrorHints(sanitizedShape);

        return new ShapeInfo
        {
            Shape = sanitizedShape,
            CacheCount = cacheCount,
            IsJsonSchema = isJsonSchema,
            ErrorConfig = errorConfig
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

    /// <summary>
    /// Extracts error configuration from query params, headers, or shape JSON
    /// Precedence: Query params > Headers > Shape JSON
    /// </summary>
    private ErrorConfig? ExtractErrorConfig(HttpRequest request, string? body, string? shapeText)
    {
        // 1) Query parameters: ?error=404 or ?error=404&errorMessage=Custom&errorDetails=Details
        if (request.Query.TryGetValue("error", out var errorQuery) && errorQuery.Count > 0)
        {
            var errorStr = errorQuery[0];
            if (int.TryParse(errorStr, out var statusCode) && statusCode >= 100 && statusCode < 600)
            {
                var message = request.Query.TryGetValue("errorMessage", out var msgQuery) && msgQuery.Count > 0
                    ? msgQuery[0]
                    : null;
                var details = request.Query.TryGetValue("errorDetails", out var detailsQuery) && detailsQuery.Count > 0
                    ? detailsQuery[0]
                    : null;

                return new ErrorConfig(statusCode, message, details);
            }
        }

        // 2) Headers: X-Error-Code and X-Error-Message
        if (request.Headers.TryGetValue("X-Error-Code", out var errorHeader) && errorHeader.Count > 0)
        {
            var errorStr = errorHeader[0];
            if (int.TryParse(errorStr, out var statusCode) && statusCode >= 100 && statusCode < 600)
            {
                var message = request.Headers.TryGetValue("X-Error-Message", out var msgHeader) && msgHeader.Count > 0
                    ? msgHeader[0]
                    : null;
                var details = request.Headers.TryGetValue("X-Error-Details", out var detailsHeader) && detailsHeader.Count > 0
                    ? detailsHeader[0]
                    : null;

                return new ErrorConfig(statusCode, message, details);
            }
        }

        // 3) Shape JSON: {"$error": 404} or {"$error": {"code": 404, "message": "...", "details": "..."}}
        if (!string.IsNullOrWhiteSpace(shapeText))
        {
            try
            {
                using var doc = JsonDocument.Parse(shapeText);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("$error", out var errorProp))
                {
                    // Simple form: {"$error": 404}
                    if (errorProp.ValueKind == JsonValueKind.Number && errorProp.TryGetInt32(out var simpleCode))
                    {
                        if (simpleCode >= 100 && simpleCode < 600)
                            return new ErrorConfig(simpleCode);
                    }

                    // Complex form: {"$error": {"code": 404, "message": "...", "details": "..."}}
                    if (errorProp.ValueKind == JsonValueKind.Object)
                    {
                        if (errorProp.TryGetProperty("code", out var codeProp) &&
                            codeProp.ValueKind == JsonValueKind.Number &&
                            codeProp.TryGetInt32(out var complexCode) &&
                            complexCode >= 100 && complexCode < 600)
                        {
                            var message = errorProp.TryGetProperty("message", out var msgProp) &&
                                          msgProp.ValueKind == JsonValueKind.String
                                ? msgProp.GetString()
                                : null;

                            var details = errorProp.TryGetProperty("details", out var detailsProp) &&
                                          detailsProp.ValueKind == JsonValueKind.String
                                ? detailsProp.GetString()
                                : null;

                            return new ErrorConfig(complexCode, message, details);
                        }
                    }
                }
            }
            catch
            {
                // ignore JSON parse errors
            }
        }

        // 4) Body property: {"error": 404} or {"error": {"code": 404, ...}}
        if (!string.IsNullOrWhiteSpace(body) &&
            request.ContentType != null &&
            request.ContentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("error", out var errorProp))
                {
                    // Simple form
                    if (errorProp.ValueKind == JsonValueKind.Number && errorProp.TryGetInt32(out var simpleCode))
                    {
                        if (simpleCode >= 100 && simpleCode < 600)
                            return new ErrorConfig(simpleCode);
                    }

                    // Complex form
                    if (errorProp.ValueKind == JsonValueKind.Object)
                    {
                        if (errorProp.TryGetProperty("code", out var codeProp) &&
                            codeProp.ValueKind == JsonValueKind.Number &&
                            codeProp.TryGetInt32(out var complexCode) &&
                            complexCode >= 100 && complexCode < 600)
                        {
                            var message = errorProp.TryGetProperty("message", out var msgProp) &&
                                          msgProp.ValueKind == JsonValueKind.String
                                ? msgProp.GetString()
                                : null;

                            var details = errorProp.TryGetProperty("details", out var detailsProp) &&
                                          detailsProp.ValueKind == JsonValueKind.String
                                ? detailsProp.GetString()
                                : null;

                            return new ErrorConfig(complexCode, message, details);
                        }
                    }
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
    /// Removes $error hints from shape JSON
    /// </summary>
    private string? SanitizeErrorHints(string? shapeText)
    {
        if (string.IsNullOrWhiteSpace(shapeText))
            return shapeText;

        try
        {
            using var doc = JsonDocument.Parse(shapeText);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return shapeText;

            // Check if $error exists
            if (!doc.RootElement.TryGetProperty("$error", out _))
                return shapeText;

            // Rebuild without $error
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (string.Equals(prop.Name, "$error", StringComparison.OrdinalIgnoreCase))
                        continue; // skip error hint

                    prop.WriteTo(writer);
                }
                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch
        {
            return shapeText; // if invalid JSON, return as-is
        }
    }
}
