namespace mostlylucid.mockllmapi.Packs;

/// <summary>
///     An API Holodeck persona pack. Defines the stage the Holodeck performs on.
///     The LLM improvises all data; packs define structure, personality, and timing only.
/// </summary>
public class HoldeckPack
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>System prompt additions that give the LLM domain vocabulary and API style.</summary>
    public string? PromptPersonality { get; set; }

    public List<ApiSurfaceEntry> ApiSurface { get; set; } = new();
    public List<ResponseShapeEntry> ResponseShapes { get; set; } = new();
    public List<PackJourneyPattern> JourneyPatterns { get; set; } = new();
    public PackTimingProfile TimingProfile { get; set; } = new();
    public PackModelHints ModelHints { get; set; } = new();
    public PackContextSchema? ContextSchema { get; set; }
}

public class ApiSurfaceEntry
{
    public string Path { get; set; } = string.Empty;
    public List<string> Methods { get; set; } = new();
    public string? Description { get; set; }
}

public class ResponseShapeEntry
{
    public string PathPattern { get; set; } = string.Empty;
    public string? Shape { get; set; }
}

public class PackJourneyPattern
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<string> Steps { get; set; } = new();
}

public class PackTimingProfile
{
    public int MinMs { get; set; } = 0;
    public int MaxMs { get; set; } = 0;
    public int JitterMs { get; set; } = 0;

    public bool IsActive => MaxMs > 0;
}

public class PackModelHints
{
    public double? Temperature { get; set; }
    public int? MaxTokens { get; set; }
}

public class PackContextSchema
{
    public List<PackContextKey> Keys { get; set; } = new();
    public List<PackSeedKey> SeedKeys { get; set; } = new();
}

public class PackContextKey
{
    public string Key { get; set; } = string.Empty;
    public string? ExtractFrom { get; set; }
    public string Scope { get; set; } = "session";
}

public class PackSeedKey
{
    public string Key { get; set; } = string.Empty;
}
