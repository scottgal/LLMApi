namespace mostlylucid.mockllmapi.Models;

/// <summary>
/// Lightweight chat completion response structure
/// </summary>
internal struct ChatCompletionLite
{
    public ChoiceLite[] Choices { get; set; }

    public string? FirstContent => Choices != null && Choices.Length > 0
        ? Choices[0].Message.Content
        : null;
}

internal struct ChoiceLite
{
    public MessageLite Message { get; set; }
}

internal struct MessageLite
{
    public string Content { get; set; }
}
