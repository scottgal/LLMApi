namespace mostlylucid.mockllmapi.Models;

/// <summary>
///     High-level classification of the attacking/requesting client.
///     E.g. "Rest", "GraphQL", "AuthBruteForce", "Scanner", etc.
/// </summary>
public enum JourneyModality
{
    Rest,
    GraphQL,
    Auth,
    Scanner,
    Other
}

/// <summary>
///     Per-journey high-level prompt guidance.
/// </summary>
public sealed record JourneyPromptHints(
    string? Scenario = null,
    string? DataStyle = null,
    string? RiskFlavor = null,
    string? RandomnessProfile = null);

/// <summary>
///     Per-step prompt guidance and knobs for randomness / context emphasis.
/// </summary>
public sealed record JourneyStepPromptHints(
    IReadOnlyList<string>? HighlightFields = null,
    IReadOnlyList<string>? ContextKeys = null,
    IReadOnlyList<string>? PromoteKeys = null,
    IReadOnlyList<string>? DemoteKeys = null,
    IReadOnlyList<string>? LureFields = null,
    string? Tone = null,
    string? RandomnessSeed = null,
    string? AdditionalInstructions = null);

/// <summary>
///     Template for a single step in a journey.
///     Paths and bodies can contain template tokens like {{userId}} that are
///     resolved per-session before use.
/// </summary>
public sealed record JourneyStepTemplate(
    string Method,
    string Path,
    string? ShapeJson = null,
    string? BodyTemplateJson = null,
    string? Description = null,
    JourneyStepPromptHints? PromptHints = null);

/// <summary>
///     Template for a complete "user journey" for a given modality.
/// </summary>
public sealed record JourneyTemplate(
    string Name,
    JourneyModality Modality,
    double Weight,
    JourneyPromptHints? PromptHints,
    IReadOnlyList<JourneyStepTemplate> Steps);

/// <summary>
///     A materialised journey instance for a specific session.
///     All template tokens ({{...}}) should already be resolved in Path/Body
///     based on per-session variables when the instance is created.
/// </summary>
public sealed record JourneyInstance(
    string SessionId,
    JourneyTemplate Template,
    IReadOnlyDictionary<string, string> Variables,
    IReadOnlyList<JourneyStepTemplate> ResolvedSteps,
    int CurrentStepIndex)
{
    /// <summary>
    ///     Gets the current step in the journey.
    /// </summary>
    public JourneyStepTemplate? CurrentStep =>
        CurrentStepIndex >= 0 && CurrentStepIndex < ResolvedSteps.Count
            ? ResolvedSteps[CurrentStepIndex]
            : null;

    /// <summary>
    ///     Indicates whether the journey has been completed.
    /// </summary>
    public bool IsComplete => CurrentStepIndex >= ResolvedSteps.Count;

    /// <summary>
    ///     Creates a new instance with the step index advanced by one.
    /// </summary>
    public JourneyInstance AdvanceStep()
    {
        return this with { CurrentStepIndex = CurrentStepIndex + 1 };
    }
}

/// <summary>
///     Lightweight snapshot of "API context memory" for the current session.
///     This is derived from your existing shared context store.
///     Keys are dotted paths (e.g. "user.id", "customer.tier", "items[0].sku").
/// </summary>
public sealed record ApiContextSnapshot(
    IReadOnlyDictionary<string, string> AllKeys,
    IReadOnlyDictionary<string, string> PromotedKeys,
    IReadOnlyDictionary<string, string> DemotedKeys);

/// <summary>
///     Result of combining global/journey/step hints + context into a single
///     object that the prompt builder can inject into the LLM prompt.
/// </summary>
public sealed record JourneyPromptInfluence(
    string JourneyName,
    JourneyModality Modality,
    string? Scenario,
    string? DataStyle,
    string? RiskFlavor,
    string? RandomnessProfile,
    string? StepDescription,
    string? Tone,
    string RandomnessSeed,
    IReadOnlyDictionary<string, string> PromotedContext,
    IReadOnlyDictionary<string, string> DemotedContext,
    IReadOnlyList<string> HighlightFields,
    IReadOnlyList<string> LureFields,
    IReadOnlyDictionary<string, object> RawStepHints);