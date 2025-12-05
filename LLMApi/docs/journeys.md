Here’s a concrete function spec you can drop into `mostlylucid.mockllmapi` as the “journey → prompt influencer” bridge.

I’ll define:

* minimal data types (you can merge with your existing options/models)
* one core function `BuildJourneyPromptInfluence(...)`
* what it should do + how it gets used in the prompt builder

---

## 1. Data models (C#)

```csharp
/// <summary>
/// High-level classification of the attacking/requesting client.
/// E.g. "Rest", "GraphQL", "AuthBruteForce", "Scanner", etc.
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
/// Per-journey high-level prompt guidance.
/// </summary>
public sealed record JourneyPromptHints(
    string? Scenario = null,
    string? DataStyle = null,
    string? RiskFlavor = null,
    string? RandomnessProfile = null);

/// <summary>
/// Per-step prompt guidance and knobs for randomness / context emphasis.
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
/// Template for a single step in a journey.
/// Paths and bodies can contain template tokens like {{userId}} that are
/// resolved per-session before use.
/// </summary>
public sealed record JourneyStepTemplate(
    string Method,
    string Path,
    string? ShapeJson = null,
    string? BodyTemplateJson = null,
    string? Description = null,
    JourneyStepPromptHints? PromptHints = null);

/// <summary>
/// Template for a complete "user journey" for a given modality.
/// </summary>
public sealed record JourneyTemplate(
    string Name,
    JourneyModality Modality,
    double Weight,
    JourneyPromptHints? PromptHints,
    IReadOnlyList<JourneyStepTemplate> Steps);

/// <summary>
/// A materialised journey instance for a specific honeypot session.
/// All template tokens ({{...}}) should already be resolved in Path/Body
/// based on per-session variables when the instance is created.
/// </summary>
public sealed record JourneyInstance(
    string SessionId,
    JourneyTemplate Template,
    IReadOnlyDictionary<string, string> Variables,
    IReadOnlyList<JourneyStepTemplate> Steps,
    int CurrentStepIndex);

/// <summary>
/// Lightweight snapshot of "API context memory" for the current session.
/// This is derived from your existing shared context store.
/// Keys are dotted paths (e.g. "user.id", "customer.tier", "items[0].sku").
/// </summary>
public sealed record ApiContextSnapshot(
    IReadOnlyDictionary<string, string> AllKeys,
    IReadOnlyDictionary<string, string> PromotedKeys,
    IReadOnlyDictionary<string, string> DemotedKeys);

/// <summary>
/// Result of combining global/journey/step hints + context into a single
/// object that the prompt builder can inject into the LLM prompt.
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
```

---

## 2. Function spec

```csharp
/// <summary>
/// Builds a combined "prompt influencer" for the current journey step,
/// based on:
///  - the active journey instance (including its template-level hints)
///  - the specific step within that journey
///  - the current API context memory snapshot
///
/// This function does NOT build the full LLM prompt. It only produces a
/// structured description that the prompt builder can embed into its
/// template (e.g. as JSON or formatted text).
///
/// Typical usage:
///  1. Resolve JourneyInstance for this session + step.
///  2. Resolve ApiContextSnapshot for this session from your shared memory.
///  3. Call BuildJourneyPromptInfluence(...).
///  4. Inject the resulting object into your prompt template.
/// </summary>
/// <param name="journeyInstance">
/// The materialised journey instance for the current honeypot/session.
/// Must contain the resolved JourneyTemplate and the current step index.
/// </param>
/// <param name="step">
/// The journey step being executed for this HTTP request.
/// </param>
/// <param name="contextSnapshot">
/// Snapshot of the current API context memory for this session,
/// pre-extracted from your existing context store. May be empty.
/// </param>
/// <param name="fallbackRandomnessSeed">
/// A fallback seed to use if neither the step-level nor journey-level
/// configuration defines a seed. Recommended to be a stable hash of
/// (SessionId + Path + Method + StepIndex).
/// </param>
/// <returns>
/// A JourneyPromptInfluence containing merged hints and selected context
/// keys ready to be embedded into the LLM prompt.
/// </returns>
public JourneyPromptInfluence BuildJourneyPromptInfluence(
    JourneyInstance journeyInstance,
    JourneyStepTemplate step,
    ApiContextSnapshot contextSnapshot,
    string fallbackRandomnessSeed)
{
    if (journeyInstance is null) throw new ArgumentNullException(nameof(journeyInstance));
    if (step is null) throw new ArgumentNullException(nameof(step));
    if (contextSnapshot is null) throw new ArgumentNullException(nameof(contextSnapshot));
    if (string.IsNullOrWhiteSpace(fallbackRandomnessSeed))
        throw new ArgumentException("Fallback randomness seed must not be empty.", nameof(fallbackRandomnessSeed));

    var template = journeyInstance.Template;
    var journeyHints = template.PromptHints ?? new JourneyPromptHints();
    var stepHints = step.PromptHints ?? new JourneyStepPromptHints();

    // 1. Determine randomness seed priority:
    //    StepHints.RandomnessSeed > fallbackRandomnessSeed
    //    (You can also later add: journey-level seed if you want.)
    var randomnessSeed = !string.IsNullOrWhiteSpace(stepHints.RandomnessSeed)
        ? stepHints.RandomnessSeed!
        : fallbackRandomnessSeed;

    // 2. Compute promoted/demoted context based on hints.
    //    If PromoteKeys/DemoteKeys are not specified, use the snapshot
    //    as-is (or rely on how you constructed ApiContextSnapshot).
    var promoted = contextSnapshot.PromotedKeys;
    var demoted = contextSnapshot.DemotedKeys;

    if (stepHints.PromoteKeys is { Count: > 0 })
    {
        var filtered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in stepHints.PromoteKeys!)
        {
            if (contextSnapshot.AllKeys.TryGetValue(key, out var value))
            {
                filtered[key] = value;
            }
        }

        if (filtered.Count > 0)
        {
            promoted = filtered;
        }
    }

    if (stepHints.DemoteKeys is { Count: > 0 })
    {
        var filtered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in stepHints.DemoteKeys!)
        {
            if (contextSnapshot.AllKeys.TryGetValue(key, out var value))
            {
                filtered[key] = value;
            }
        }

        if (filtered.Count > 0)
        {
            demoted = filtered;
        }
    }

    var highlightFields = stepHints.HighlightFields ?? Array.Empty<string>();
    var lureFields = stepHints.LureFields ?? Array.Empty<string>();

    // 3. Raw step hints so the prompt builder can embed the whole object
    //    directly into the LLM prompt if desired (e.g. as JSON).
    var rawStepHints = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
    {
        ["ContextKeys"] = stepHints.ContextKeys ?? Array.Empty<string>(),
        ["HighlightFields"] = highlightFields,
        ["PromoteKeys"] = stepHints.PromoteKeys ?? Array.Empty<string>(),
        ["DemoteKeys"] = stepHints.DemoteKeys ?? Array.Empty<string>(),
        ["LureFields"] = lureFields,
        ["RandomnessSeed"] = randomnessSeed,
    };

    if (!string.IsNullOrWhiteSpace(stepHints.Tone))
    {
        rawStepHints["Tone"] = stepHints.Tone!;
    }

    if (!string.IsNullOrWhiteSpace(stepHints.AdditionalInstructions))
    {
        rawStepHints["AdditionalInstructions"] = stepHints.AdditionalInstructions!;
    }

    var influence = new JourneyPromptInfluence(
        JourneyName: template.Name,
        Modality: template.Modality,
        Scenario: journeyHints.Scenario,
        DataStyle: journeyHints.DataStyle,
        RiskFlavor: journeyHints.RiskFlavor,
        RandomnessProfile: journeyHints.RandomnessProfile,
        StepDescription: step.Description,
        Tone: stepHints.Tone,
        RandomnessSeed: randomnessSeed,
        PromotedContext: promoted,
        DemotedContext: demoted,
        HighlightFields: highlightFields,
        LureFields: lureFields,
        RawStepHints: rawStepHints);

    return influence;
}
```

---

## 3. How you’d use it in your prompt builder

Inside your existing LLM request builder (pseudo-ish):

```csharp
var journeyInstance = journeyResolver.GetForSession(sessionId);
var stepTemplate = journeyResolver.ResolveStepForRequest(journeyInstance, httpMethod, path);
var contextSnapshot = contextProvider.BuildSnapshotForSession(sessionId);

var fallbackSeed = SeedHelper.BuildSeed(journeyInstance.SessionId, httpMethod, path, journeyInstance.CurrentStepIndex);

var influence = BuildJourneyPromptInfluence(
    journeyInstance,
    stepTemplate,
    contextSnapshot,
    fallbackSeed);

// Then inject into your existing prompt:

var prompt = $"""
You are generating MOCK DATA for a honeypot API.

High-level scenario: {influence.Scenario ?? "generic application"}
Modality: {influence.Modality}
Journey: {influence.JourneyName}
Step description: {influence.StepDescription ?? "unspecified"}

Data style: {influence.DataStyle ?? "realistic but synthetic data"}
Risk flavor: {influence.RiskFlavor ?? "permissions, roles, access control"}

Randomness profile: {influence.RandomnessProfile ?? "medium-variation-but-consistent-ids"}
Stable randomness seed for this step: {influence.RandomnessSeed}

The following context keys SHOULD stay consistent in this session:
{FormatKeyValues(influence.PromotedContext)}

These context keys are lower priority and MAY change or be omitted:
{FormatKeyValues(influence.DemotedContext)}

Fields to highlight or enrich if used:
{string.Join(", ", influence.HighlightFields)}

You MAY include tempting but harmless fields named:
{string.Join(", ", influence.LureFields)}

Step-specific hints (JSON):
{JsonSerializer.Serialize(influence.RawStepHints)}

Generate ONLY JSON matching this shape:
{shapeJson}
""";
```

`FormatKeyValues` can just dump `"user.id: 1234"` style lines.

---

This gives you a clean, testable seam:

* config → `JourneyTemplate` / `JourneyStepTemplate`
* runtime variables → `JourneyInstance`
* context memory → `ApiContextSnapshot`
* **this function** → `JourneyPromptInfluence`
* prompt builder → uses `JourneyPromptInfluence` to bias the LLM

You can evolve the internals later (add more hint fields, different seeding rules, etc.) without changing the public function signature.
