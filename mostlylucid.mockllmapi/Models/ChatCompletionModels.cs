using System.Text.Json.Serialization;

namespace mostlylucid.mockllmapi.Models;

/// <summary>
/// Lightweight chat completion response structure
/// </summary>
internal struct ChatCompletionLite
{
    [JsonPropertyName("choices")]
    public ChoiceLite[] Choices { get; set; }

    [JsonIgnore]
    public string? FirstContent => Choices != null && Choices.Length > 0
        ? Choices[0].Message.Content
        : null;
}

internal struct ChoiceLite
{
    [JsonPropertyName("message")]
    public MessageLite Message { get; set; }
}

internal struct MessageLite
{
    [JsonPropertyName("content")]
    public string Content { get; set; }
}

/// <summary>
/// Source-generated JSON serialization context for chat completion models.
/// AOT and trimming-friendly for .NET 10+.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ChatCompletionLite))]
[JsonSerializable(typeof(ChoiceLite))]
[JsonSerializable(typeof(MessageLite))]
internal partial class ChatCompletionSerializerContext : JsonSerializerContext
{
}
