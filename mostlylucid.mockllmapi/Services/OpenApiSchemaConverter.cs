using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using System.Text;

namespace mostlylucid.mockllmapi.Services;

/// <summary>
/// Converts OpenAPI schemas to JSON shape strings that can be used as prompts for LLM mock data generation.
/// </summary>
public class OpenApiSchemaConverter
{
    private readonly ILogger<OpenApiSchemaConverter> _logger;

    public OpenApiSchemaConverter(ILogger<OpenApiSchemaConverter> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Converts an OpenAPI operation's response schema to a JSON shape string.
    /// This shape will be used as the template for LLM-generated mock data.
    /// </summary>
    /// <param name="operation">The OpenAPI operation</param>
    /// <param name="statusCode">The HTTP status code to generate shape for (default: "200")</param>
    /// <returns>JSON shape string, or null if no schema found</returns>
    public string? GetResponseShape(OpenApiOperation operation, string statusCode = "200")
    {
        if (operation.Responses == null || !operation.Responses.TryGetValue(statusCode, out var response))
        {
            _logger.LogWarning("No {StatusCode} response found for operation: {OperationId}", statusCode, operation.OperationId);
            return null;
        }

        // Look for application/json content
        if (response.Content == null || !response.Content.TryGetValue("application/json", out var mediaType))
        {
            _logger.LogWarning("No application/json content found for operation: {OperationId}", operation.OperationId);
            return null;
        }

        if (mediaType.Schema == null)
        {
            _logger.LogWarning("No schema found for operation: {OperationId}", operation.OperationId);
            return null;
        }

        // Convert schema to JSON shape
        var shape = ConvertSchemaToShape(mediaType.Schema);
        _logger.LogDebug("Generated shape for {OperationId}: {Shape}", operation.OperationId, shape);
        return shape;
    }

    /// <summary>
    /// Converts an OpenAPI schema to a JSON shape template.
    /// </summary>
    private string ConvertSchemaToShape(OpenApiSchema schema, int depth = 0)
    {
        const int maxDepth = 4; // Prevent infinite recursion

        if (depth > maxDepth)
        {
            _logger.LogWarning("Max schema depth reached, truncating");
            return "\"...\"";
        }

        // Handle references
        if (schema.Reference != null)
        {
            _logger.LogDebug("Resolving schema reference: {Reference}", schema.Reference.Id);
            // The reference should already be resolved by OpenApiStreamReader
            // If not, we'll use a placeholder
            return $"\"<{schema.Reference.Id}>\"";
        }

        // Handle different schema types
        return schema.Type switch
        {
            "object" => ConvertObjectSchema(schema, depth),
            "array" => ConvertArraySchema(schema, depth),
            "string" => ConvertStringSchema(schema),
            "integer" or "number" => ConvertNumberSchema(schema),
            "boolean" => "true",
            null when schema.Properties?.Count > 0 => ConvertObjectSchema(schema, depth), // Infer object from properties
            null when schema.Items != null => ConvertArraySchema(schema, depth), // Infer array from items
            _ => $"\"{schema.Type ?? "unknown"}\""
        };
    }

    private string ConvertObjectSchema(OpenApiSchema schema, int depth)
    {
        if (schema.Properties == null || schema.Properties.Count == 0)
        {
            return "{}";
        }

        var sb = new StringBuilder();
        sb.Append('{');

        var properties = schema.Properties.ToList();
        for (int i = 0; i < properties.Count; i++)
        {
            var prop = properties[i];
            sb.Append($"\"{prop.Key}\":");
            sb.Append(ConvertSchemaToShape(prop.Value, depth + 1));

            if (i < properties.Count - 1)
            {
                sb.Append(',');
            }
        }

        sb.Append('}');
        return sb.ToString();
    }

    private string ConvertArraySchema(OpenApiSchema schema, int depth)
    {
        if (schema.Items == null)
        {
            return "[]";
        }

        var itemShape = ConvertSchemaToShape(schema.Items, depth + 1);
        return $"[{itemShape}]";
    }

    private string ConvertStringSchema(OpenApiSchema schema)
    {
        // Use enum values if available
        if (schema.Enum?.Count > 0)
        {
            var firstEnum = schema.Enum.FirstOrDefault()?.ToString() ?? "value";
            return $"\"{firstEnum}\"";
        }

        // Use example if available
        if (schema.Example != null)
        {
            return $"\"{schema.Example}\"";
        }

        // Use format-specific placeholders
        return schema.Format switch
        {
            "date" => "\"2025-01-01\"",
            "date-time" => "\"2025-01-01T00:00:00Z\"",
            "email" => "\"user@example.com\"",
            "uri" => "\"https://example.com\"",
            "uuid" => "\"123e4567-e89b-12d3-a456-426614174000\"",
            _ => "\"string\""
        };
    }

    private string ConvertNumberSchema(OpenApiSchema schema)
    {
        // Use example if available
        if (schema.Example != null)
        {
            return schema.Example.ToString() ?? "0";
        }

        // Use minimum if available
        if (schema.Minimum.HasValue)
        {
            return schema.Minimum.Value.ToString();
        }

        // Default based on format
        return schema.Format switch
        {
            "int64" => "1000",
            "float" or "double" => "123.45",
            _ => "42"
        };
    }

    /// <summary>
    /// Generates a description of the operation for context in the LLM prompt.
    /// </summary>
    public string GetOperationDescription(OpenApiOperation operation, string path, OperationType method)
    {
        var sb = new StringBuilder();

        sb.Append($"{method.ToString().ToUpper()} {path}");

        if (!string.IsNullOrEmpty(operation.Summary))
        {
            sb.Append($" - {operation.Summary}");
        }

        if (!string.IsNullOrEmpty(operation.Description))
        {
            sb.Append($"\n{operation.Description}");
        }

        return sb.ToString();
    }
}
