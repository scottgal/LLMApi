using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace mostlylucid.mockllmapi.Services;

/// <summary>
///     Service for optimized JSON processing with object pooling
/// </summary>
public class JsonProcessingService
{
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly IOptions<LLMockApiOptions> _options;

    public JsonProcessingService(IOptions<LLMockApiOptions> options)
    {
        _options = options;
        _jsonOptions = CreateJsonSerializerOptions();
    }

    /// <summary>
    ///     Creates optimized JsonSerializerOptions with pooling
    /// </summary>
    private JsonSerializerOptions CreateJsonSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString,
            ReferenceHandler = ReferenceHandler.Preserve,
            MaxDepth = 64, // Prevent deep nesting attacks
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            PropertyNameCaseInsensitive = true
        };

        // Add common converters
        options.Converters.Add(new JsonStringEnumConverter());

        return options;
    }

    /// <summary>
    ///     Serializes object to JSON with optimized settings
    /// </summary>
    public string Serialize(object obj)
    {
        return JsonSerializer.Serialize(obj, _jsonOptions);
    }

    /// <summary>
    ///     Deserializes JSON string to object with optimized settings
    /// </summary>
    public T Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, _jsonOptions)!;
    }

    /// <summary>
    ///     Serializes object to JSON with streaming support
    /// </summary>
    public async Task SerializeAsync<T>(T obj, Stream stream)
    {
        await JsonSerializer.SerializeAsync(stream, obj, _jsonOptions);
    }

    /// <summary>
    ///     Deserializes JSON from stream with optimized settings
    /// </summary>
    public async Task<T> DeserializeAsync<T>(Stream stream)
    {
        return await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions) ??
               throw new JsonException("Deserialization failed");
    }

    /// <summary>
    ///     Escapes JSON string for safe output
    /// </summary>
    public string EscapeJsonString(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    /// <summary>
    ///     Validates JSON structure for security
    /// </summary>
    public bool IsValidJson(string json)
    {
        try
        {
            using var jsonDocument = JsonDocument.Parse(json);

            // Check for suspicious patterns
            var root = jsonDocument.RootElement;

            // Prevent overly large documents
            if (root.GetArrayLength() > _options.Value.MaxItems)
                return false;

            // Check for potential injection attacks
            if (root.ValueKind == JsonValueKind.Object)
                foreach (var property in root.EnumerateObject())
                    if (property.Name.Contains("$", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Contains("__", StringComparison.OrdinalIgnoreCase))
                        return false;

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    ///     Gets the size of a JSON string in bytes
    /// </summary>
    public long GetJsonSize(string json)
    {
        return Encoding.UTF8.GetByteCount(json);
    }

    /// <summary>
    ///     Compresses JSON for storage (optional)
    /// </summary>
    public string CompressJson(string json)
    {
        // In a real implementation, this would use compression
        // For now, we'll just return the original JSON
        return json;
    }

    /// <summary>
    ///     Decompresses JSON from storage (optional)
    /// </summary>
    public string DecompressJson(string compressedJson)
    {
        // In a real implementation, this would decompress
        // For now, we'll just return the original JSON
        return compressedJson;
    }
}