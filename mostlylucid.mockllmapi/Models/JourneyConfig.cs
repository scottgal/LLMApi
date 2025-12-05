namespace mostlylucid.mockllmapi.Models;

/// <summary>
/// Configuration model for journey step prompt hints (JSON-serializable).
/// </summary>
public class JourneyStepPromptHintsConfig
{
    public List<string>? HighlightFields { get; set; }
    public List<string>? ContextKeys { get; set; }
    public List<string>? PromoteKeys { get; set; }
    public List<string>? DemoteKeys { get; set; }
    public List<string>? LureFields { get; set; }
    public string? Tone { get; set; }
    public string? RandomnessSeed { get; set; }
    public string? AdditionalInstructions { get; set; }

    public JourneyStepPromptHints ToRecord() => new(
        HighlightFields?.AsReadOnly(),
        ContextKeys?.AsReadOnly(),
        PromoteKeys?.AsReadOnly(),
        DemoteKeys?.AsReadOnly(),
        LureFields?.AsReadOnly(),
        Tone,
        RandomnessSeed,
        AdditionalInstructions);
}

/// <summary>
/// Configuration model for journey prompt hints (JSON-serializable).
/// </summary>
public class JourneyPromptHintsConfig
{
    public string? Scenario { get; set; }
    public string? DataStyle { get; set; }
    public string? RiskFlavor { get; set; }
    public string? RandomnessProfile { get; set; }

    public JourneyPromptHints ToRecord() => new(
        Scenario,
        DataStyle,
        RiskFlavor,
        RandomnessProfile);
}

/// <summary>
/// Configuration model for a journey step (JSON-serializable).
/// </summary>
public class JourneyStepConfig
{
    public string Method { get; set; } = "GET";
    public string Path { get; set; } = string.Empty;
    public string? ShapeJson { get; set; }
    public string? BodyTemplateJson { get; set; }
    public string? Description { get; set; }
    public JourneyStepPromptHintsConfig? PromptHints { get; set; }

    public JourneyStepTemplate ToRecord() => new(
        Method,
        Path,
        ShapeJson,
        BodyTemplateJson,
        Description,
        PromptHints?.ToRecord());
}

/// <summary>
/// Configuration model for a journey template (JSON-serializable).
/// </summary>
public class JourneyTemplateConfig
{
    public string Name { get; set; } = string.Empty;
    public string Modality { get; set; } = "Rest";
    public double Weight { get; set; } = 1.0;
    public JourneyPromptHintsConfig? PromptHints { get; set; }
    public List<JourneyStepConfig> Steps { get; set; } = new();

    public JourneyTemplate ToRecord() => new(
        Name,
        Enum.TryParse<JourneyModality>(Modality, true, out var modality) ? modality : JourneyModality.Other,
        Weight,
        PromptHints?.ToRecord(),
        Steps.Select(s => s.ToRecord()).ToList().AsReadOnly());
}

/// <summary>
/// Root configuration for journeys in appsettings.json.
/// </summary>
public class JourneysConfig
{
    /// <summary>
    /// Whether journeys are enabled.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Default variables available to all journeys (can be overridden per-session).
    /// </summary>
    public Dictionary<string, string> DefaultVariables { get; set; } = new();

    /// <summary>
    /// List of journey templates.
    /// </summary>
    public List<JourneyTemplateConfig> Journeys { get; set; } = new();
}
