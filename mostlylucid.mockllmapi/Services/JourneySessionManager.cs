using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using mostlylucid.mockllmapi.Models;

namespace mostlylucid.mockllmapi.Services;

/// <summary>
///     Manages active journey instances for sessions. Handles:
///     - Creating new journey instances from templates
///     - Resolving template variables ({{...}})
///     - Tracking step progression
///     - Session-to-journey mapping with auto-expiration
/// </summary>
public class JourneySessionManager
{
    private static readonly Regex TemplateTokenRegex = new(@"\{\{(\w+)\}\}", RegexOptions.Compiled);
    private readonly IMemoryCache _cache;
    private readonly JourneyRegistry _journeyRegistry;
    private readonly ILogger<JourneySessionManager> _logger;
    private readonly IOptionsMonitor<LLMockApiOptions> _options;

    public JourneySessionManager(
        ILogger<JourneySessionManager> logger,
        IOptionsMonitor<LLMockApiOptions> options,
        JourneyRegistry journeyRegistry,
        IMemoryCache cache)
    {
        _logger = logger;
        _options = options;
        _journeyRegistry = journeyRegistry;
        _cache = cache;
    }

    private string GetCacheKey(string sessionId)
    {
        return $"journey:session:{sessionId}";
    }

    private TimeSpan GetExpiration()
    {
        var minutes = _options.CurrentValue.ContextExpirationMinutes;
        return TimeSpan.FromMinutes(Math.Max(5, Math.Min(1440, minutes)));
    }

    /// <summary>
    ///     Gets the active journey instance for a session, if any.
    /// </summary>
    public JourneyInstance? GetJourneyForSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return null;

        return _cache.Get<JourneyInstance>(GetCacheKey(sessionId));
    }

    /// <summary>
    ///     Creates a new journey instance for a session using a specific journey template.
    /// </summary>
    public JourneyInstance CreateJourneyInstance(
        string sessionId,
        string journeyName,
        Dictionary<string, string>? variables = null)
    {
        var template = _journeyRegistry.GetJourney(journeyName)
                       ?? throw new ArgumentException($"Journey '{journeyName}' not found.", nameof(journeyName));

        return CreateJourneyInstance(sessionId, template, variables);
    }

    /// <summary>
    ///     Creates a new journey instance for a session using a journey template.
    /// </summary>
    public JourneyInstance CreateJourneyInstance(
        string sessionId,
        JourneyTemplate template,
        Dictionary<string, string>? variables = null)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("Session ID cannot be empty.", nameof(sessionId));

        // Merge default variables with provided variables
        var mergedVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var defaultVars = _options.CurrentValue.Journeys?.DefaultVariables;
        if (defaultVars != null)
            foreach (var kvp in defaultVars)
                mergedVariables[kvp.Key] = kvp.Value;

        if (variables != null)
            foreach (var kvp in variables)
                mergedVariables[kvp.Key] = kvp.Value;

        // Add session ID as a variable
        mergedVariables["sessionId"] = sessionId;

        // Resolve all steps with variables
        var resolvedSteps = template.Steps
            .Select(step => ResolveStepVariables(step, mergedVariables))
            .ToList()
            .AsReadOnly();

        var instance = new JourneyInstance(
            sessionId,
            template,
            mergedVariables,
            resolvedSteps,
            0);

        // Store in cache
        var cacheOptions = new MemoryCacheEntryOptions()
            .SetSlidingExpiration(GetExpiration());

        _cache.Set(GetCacheKey(sessionId), instance, cacheOptions);

        _logger.LogInformation(
            "Created journey instance '{JourneyName}' for session '{SessionId}' with {StepCount} steps",
            template.Name, sessionId, resolvedSteps.Count);

        return instance;
    }

    /// <summary>
    ///     Creates a journey instance for a session by selecting a random journey.
    /// </summary>
    public JourneyInstance? CreateRandomJourneyInstance(
        string sessionId,
        JourneyModality? modality = null,
        Dictionary<string, string>? variables = null)
    {
        var template = _journeyRegistry.SelectRandomJourney(modality);
        if (template == null)
        {
            _logger.LogWarning("No journey templates available for modality {Modality}", modality);
            return null;
        }

        return CreateJourneyInstance(sessionId, template, variables);
    }

    /// <summary>
    ///     Advances the journey to the next step.
    /// </summary>
    public JourneyInstance? AdvanceJourney(string sessionId)
    {
        var instance = GetJourneyForSession(sessionId);
        if (instance == null)
        {
            _logger.LogWarning("No journey found for session '{SessionId}'", sessionId);
            return null;
        }

        if (instance.IsComplete)
        {
            _logger.LogDebug("Journey '{JourneyName}' for session '{SessionId}' is already complete",
                instance.Template.Name, sessionId);
            return instance;
        }

        var advanced = instance.AdvanceStep();

        var cacheOptions = new MemoryCacheEntryOptions()
            .SetSlidingExpiration(GetExpiration());

        _cache.Set(GetCacheKey(sessionId), advanced, cacheOptions);

        _logger.LogDebug(
            "Advanced journey '{JourneyName}' for session '{SessionId}' to step {StepIndex}/{TotalSteps}",
            advanced.Template.Name, sessionId, advanced.CurrentStepIndex + 1, advanced.ResolvedSteps.Count);

        return advanced;
    }

    /// <summary>
    ///     Resolves the step that matches a given HTTP request path and method.
    ///     Returns the matching step or null if no match found.
    /// </summary>
    public JourneyStepTemplate? ResolveStepForRequest(
        JourneyInstance instance,
        string method,
        string path)
    {
        if (instance.IsComplete)
            return null;

        var currentStep = instance.CurrentStep;
        if (currentStep == null)
            return null;

        // Check if current step matches
        if (StepMatchesRequest(currentStep, method, path))
            return currentStep;

        // Optionally look ahead for matching steps (flexible journey progression)
        for (var i = instance.CurrentStepIndex + 1; i < instance.ResolvedSteps.Count; i++)
        {
            var step = instance.ResolvedSteps[i];
            if (StepMatchesRequest(step, method, path))
                return step;
        }

        return null;
    }

    /// <summary>
    ///     Ends a journey for a session.
    /// </summary>
    public bool EndJourney(string sessionId)
    {
        var key = GetCacheKey(sessionId);
        var instance = _cache.Get<JourneyInstance>(key);

        if (instance != null)
        {
            _cache.Remove(key);
            _logger.LogInformation("Ended journey '{JourneyName}' for session '{SessionId}'",
                instance.Template.Name, sessionId);
            return true;
        }

        return false;
    }

    /// <summary>
    ///     Gets all active journey sessions (for management APIs).
    ///     Note: This is limited by what's in cache and may not be complete.
    /// </summary>
    public IReadOnlyList<(string SessionId, string JourneyName, int CurrentStep, int TotalSteps, bool IsComplete)>
        GetActiveSessions()
    {
        // Note: IMemoryCache doesn't provide enumeration, so we track sessions separately
        // For now, return empty list - full implementation would need a separate tracking structure
        return Array.Empty<(string, string, int, int, bool)>();
    }

    /// <summary>
    ///     Gets journey state data to store in API context's SharedData.
    ///     This allows journey state to persist across requests via context.
    ///     Keys: journey.id, journey.name, journey.step, journey.totalSteps, journey.modality, journey.isComplete
    ///     Note: Multiple journeys can be tracked by using journey.{id}.* prefixed keys.
    /// </summary>
    public Dictionary<string, string> GetJourneyStateForContext(JourneyInstance instance)
    {
        var journeyId = instance.SessionId;
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Store with journey ID prefix for multi-journey support
            [$"journey.{journeyId}.name"] = instance.Template.Name,
            [$"journey.{journeyId}.step"] = instance.CurrentStepIndex.ToString(),
            [$"journey.{journeyId}.totalSteps"] = instance.ResolvedSteps.Count.ToString(),
            [$"journey.{journeyId}.modality"] = instance.Template.Modality.ToString(),
            [$"journey.{journeyId}.isComplete"] = instance.IsComplete.ToString().ToLowerInvariant(),
            [$"journey.{journeyId}.stepDescription"] = instance.CurrentStep?.Description ?? "",
            [$"journey.{journeyId}.stepPath"] = instance.CurrentStep?.Path ?? "",
            [$"journey.{journeyId}.stepMethod"] = instance.CurrentStep?.Method ?? "",
            // Also store as "current" journey for simple access (last updated journey)
            ["journey.id"] = journeyId,
            ["journey.name"] = instance.Template.Name,
            ["journey.step"] = instance.CurrentStepIndex.ToString(),
            ["journey.totalSteps"] = instance.ResolvedSteps.Count.ToString(),
            ["journey.modality"] = instance.Template.Modality.ToString(),
            ["journey.isComplete"] = instance.IsComplete.ToString().ToLowerInvariant(),
            ["journey.stepDescription"] = instance.CurrentStep?.Description ?? "",
            ["journey.stepPath"] = instance.CurrentStep?.Path ?? "",
            ["journey.stepMethod"] = instance.CurrentStep?.Method ?? ""
        };
    }

    /// <summary>
    ///     Restores a journey instance from context SharedData if journey state was stored.
    ///     Returns null if no journey state is stored or if the journey template no longer exists.
    ///     Supports both legacy format (journey.name) and new format (journey.{id}.name).
    /// </summary>
    public JourneyInstance? RestoreJourneyFromContext(
        string journeyId,
        IReadOnlyDictionary<string, string> sharedData)
    {
        if (string.IsNullOrWhiteSpace(journeyId))
            return null;

        // First try to find journey state by specific ID (journey.{id}.name)
        string? journeyName = null;
        string? stepStr = null;

        if (sharedData.TryGetValue($"journey.{journeyId}.name", out var idSpecificName) &&
            !string.IsNullOrWhiteSpace(idSpecificName))
        {
            journeyName = idSpecificName;
            sharedData.TryGetValue($"journey.{journeyId}.step", out stepStr);
        }
        // Fall back to legacy format (journey.name) if ID matches stored journey.id
        else if (sharedData.TryGetValue("journey.id", out var storedId) &&
                 storedId == journeyId &&
                 sharedData.TryGetValue("journey.name", out var legacyName) &&
                 !string.IsNullOrWhiteSpace(legacyName))
        {
            journeyName = legacyName;
            sharedData.TryGetValue("journey.step", out stepStr);
        }
        // Legacy support: if journeyId not found, try legacy format
        else if (sharedData.TryGetValue("journey.name", out var fallbackName) &&
                 !string.IsNullOrWhiteSpace(fallbackName))
        {
            journeyName = fallbackName;
            sharedData.TryGetValue("journey.step", out stepStr);
        }

        if (string.IsNullOrWhiteSpace(journeyName))
            return null;

        if (!int.TryParse(stepStr, out var stepIndex))
            stepIndex = 0;

        // Check if we already have this journey in cache
        var existing = GetJourneyForSession(journeyId);
        if (existing != null && existing.Template.Name == journeyName)
            // Verify step index matches, otherwise restore from context
            if (existing.CurrentStepIndex == stepIndex)
                return existing;

        // Try to restore from template
        var template = _journeyRegistry.GetJourney(journeyName);
        if (template == null)
        {
            _logger.LogWarning("Cannot restore journey '{JourneyName}' - template not found", journeyName);
            return null;
        }

        // Extract variables from context (anything not journey.* prefixed)
        var variables = sharedData
            .Where(kvp => !kvp.Key.StartsWith("journey.", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

        // Resolve all steps with variables
        var resolvedSteps = template.Steps
            .Select(step => ResolveStepVariables(step, variables))
            .ToList()
            .AsReadOnly();

        var instance = new JourneyInstance(
            journeyId,
            template,
            variables,
            resolvedSteps,
            stepIndex);

        // Store in cache
        var cacheOptions = new MemoryCacheEntryOptions()
            .SetSlidingExpiration(GetExpiration());

        _cache.Set(GetCacheKey(journeyId), instance, cacheOptions);

        _logger.LogInformation(
            "Restored journey '{JourneyName}' (ID: {JourneyId}) at step {StepIndex}/{TotalSteps}",
            template.Name, journeyId, stepIndex, resolvedSteps.Count);

        return instance;
    }

    /// <summary>
    ///     Gets or creates a journey for a session. If journeyName is specified, starts that journey.
    ///     If startRandom is true and no journey is active, selects a random journey.
    ///     If context SharedData contains journey state, restores from that.
    /// </summary>
    public JourneyInstance? GetOrCreateJourney(
        string sessionId,
        string? journeyName,
        bool startRandom,
        JourneyModality? modality,
        IReadOnlyDictionary<string, string>? contextSharedData,
        Dictionary<string, string>? variables = null)
    {
        // 1. If specific journey requested, start it
        if (!string.IsNullOrWhiteSpace(journeyName))
        {
            var template = _journeyRegistry.GetJourney(journeyName);
            if (template != null) return CreateJourneyInstance(sessionId, template, variables);
            _logger.LogWarning("Journey '{JourneyName}' not found", journeyName);
        }

        // 2. Check for existing journey in cache
        var existing = GetJourneyForSession(sessionId);
        if (existing != null)
            return existing;

        // 3. Try to restore from context SharedData
        if (contextSharedData != null && contextSharedData.Count > 0)
        {
            var restored = RestoreJourneyFromContext(sessionId, contextSharedData);
            if (restored != null)
                return restored;
        }

        // 4. If startRandom, select a random journey
        if (startRandom) return CreateRandomJourneyInstance(sessionId, modality, variables);

        return null;
    }

    private JourneyStepTemplate ResolveStepVariables(
        JourneyStepTemplate step,
        IReadOnlyDictionary<string, string> variables)
    {
        return new JourneyStepTemplate(
            step.Method,
            ResolveTemplate(step.Path, variables),
            step.ShapeJson != null ? ResolveTemplate(step.ShapeJson, variables) : null,
            step.BodyTemplateJson != null ? ResolveTemplate(step.BodyTemplateJson, variables) : null,
            step.Description,
            step.PromptHints);
    }

    private string ResolveTemplate(string template, IReadOnlyDictionary<string, string> variables)
    {
        return TemplateTokenRegex.Replace(template, match =>
        {
            var key = match.Groups[1].Value;
            return variables.TryGetValue(key, out var value) ? value : match.Value;
        });
    }

    private bool StepMatchesRequest(JourneyStepTemplate step, string method, string path)
    {
        // Method match (case-insensitive)
        if (!step.Method.Equals(method, StringComparison.OrdinalIgnoreCase))
            return false;

        // Path match - support wildcards
        var pattern = "^" + Regex.Escape(step.Path)
            .Replace(@"\*\*", ".*")
            .Replace(@"\*", "[^/]*") + "$";

        return Regex.IsMatch(path, pattern, RegexOptions.IgnoreCase);
    }
}