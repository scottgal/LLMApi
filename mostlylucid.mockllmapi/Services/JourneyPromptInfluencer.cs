using System.Security.Cryptography;
using System.Text;
using mostlylucid.mockllmapi.Models;

namespace mostlylucid.mockllmapi.Services;

/// <summary>
/// Builds prompt influence data from journey instances for use in LLM prompts.
/// </summary>
public class JourneyPromptInfluencer
{
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
    /// </summary>
    /// <param name="journeyInstance">
    /// The materialised journey instance for the current session.
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
        var randomnessSeed = !string.IsNullOrWhiteSpace(stepHints.RandomnessSeed)
            ? stepHints.RandomnessSeed!
            : fallbackRandomnessSeed;

        // 2. Compute promoted/demoted context based on hints.
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

    /// <summary>
    /// Generates a stable randomness seed based on session and request details.
    /// </summary>
    public static string GenerateRandomnessSeed(string sessionId, string method, string path, int stepIndex)
    {
        var input = $"{sessionId}:{method}:{path}:{stepIndex}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..16]; // 16-char hex string
    }

    /// <summary>
    /// Builds an ApiContextSnapshot from the OpenApiContextManager's shared data.
    /// </summary>
    public static ApiContextSnapshot BuildContextSnapshot(
        IReadOnlyDictionary<string, string>? sharedData,
        IReadOnlyList<string>? promoteKeys = null,
        IReadOnlyList<string>? demoteKeys = null)
    {
        var allKeys = sharedData ?? new Dictionary<string, string>();

        var promoted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var demoted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (promoteKeys != null)
        {
            foreach (var key in promoteKeys)
            {
                if (allKeys.TryGetValue(key, out var value))
                    promoted[key] = value;
            }
        }

        if (demoteKeys != null)
        {
            foreach (var key in demoteKeys)
            {
                if (allKeys.TryGetValue(key, out var value))
                    demoted[key] = value;
            }
        }

        return new ApiContextSnapshot(allKeys, promoted, demoted);
    }

    /// <summary>
    /// Formats the journey prompt influence as a string for embedding in prompts.
    /// </summary>
    public static string FormatInfluenceForPrompt(JourneyPromptInfluence influence)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"Journey: {influence.JourneyName}");
        sb.AppendLine($"Modality: {influence.Modality}");

        if (!string.IsNullOrWhiteSpace(influence.Scenario))
            sb.AppendLine($"Scenario: {influence.Scenario}");

        if (!string.IsNullOrWhiteSpace(influence.StepDescription))
            sb.AppendLine($"Step: {influence.StepDescription}");

        if (!string.IsNullOrWhiteSpace(influence.DataStyle))
            sb.AppendLine($"Data Style: {influence.DataStyle}");

        if (!string.IsNullOrWhiteSpace(influence.RiskFlavor))
            sb.AppendLine($"Risk Flavor: {influence.RiskFlavor}");

        if (!string.IsNullOrWhiteSpace(influence.Tone))
            sb.AppendLine($"Tone: {influence.Tone}");

        if (!string.IsNullOrWhiteSpace(influence.RandomnessProfile))
            sb.AppendLine($"Randomness: {influence.RandomnessProfile}");

        sb.AppendLine($"Seed: {influence.RandomnessSeed}");

        if (influence.PromotedContext.Count > 0)
        {
            sb.AppendLine("Promoted Context (MUST be consistent):");
            foreach (var kvp in influence.PromotedContext)
                sb.AppendLine($"  {kvp.Key}: {kvp.Value}");
        }

        if (influence.DemotedContext.Count > 0)
        {
            sb.AppendLine("Demoted Context (may vary):");
            foreach (var kvp in influence.DemotedContext)
                sb.AppendLine($"  {kvp.Key}: {kvp.Value}");
        }

        if (influence.HighlightFields.Count > 0)
            sb.AppendLine($"Highlight Fields: {string.Join(", ", influence.HighlightFields)}");

        if (influence.LureFields.Count > 0)
            sb.AppendLine($"Lure Fields (tempting but harmless): {string.Join(", ", influence.LureFields)}");

        return sb.ToString();
    }
}
