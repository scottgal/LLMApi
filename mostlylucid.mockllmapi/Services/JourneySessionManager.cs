using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using mostlylucid.mockllmapi.Models;

namespace mostlylucid.mockllmapi.Services;

/// <summary>
/// Manages active journey instances for sessions. Handles:
/// - Creating new journey instances from templates
/// - Resolving template variables ({{...}})
/// - Tracking step progression
/// - Session-to-journey mapping with auto-expiration
/// </summary>
public class JourneySessionManager
{
    private readonly ILogger<JourneySessionManager> _logger;
    private readonly IOptionsMonitor<LLMockApiOptions> _options;
    private readonly JourneyRegistry _journeyRegistry;
    private readonly IMemoryCache _cache;
    private static readonly Regex TemplateTokenRegex = new(@"\{\{(\w+)\}\}", RegexOptions.Compiled);

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

    private string GetCacheKey(string sessionId) => $"journey:session:{sessionId}";

    private TimeSpan GetExpiration()
    {
        var minutes = _options.CurrentValue.ContextExpirationMinutes;
        return TimeSpan.FromMinutes(Math.Max(5, Math.Min(1440, minutes)));
    }

    /// <summary>
    /// Gets the active journey instance for a session, if any.
    /// </summary>
    public JourneyInstance? GetJourneyForSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return null;

        return _cache.Get<JourneyInstance>(GetCacheKey(sessionId));
    }

    /// <summary>
    /// Creates a new journey instance for a session using a specific journey template.
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
    /// Creates a new journey instance for a session using a journey template.
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
        {
            foreach (var kvp in defaultVars)
                mergedVariables[kvp.Key] = kvp.Value;
        }

        if (variables != null)
        {
            foreach (var kvp in variables)
                mergedVariables[kvp.Key] = kvp.Value;
        }

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
            CurrentStepIndex: 0);

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
    /// Creates a journey instance for a session by selecting a random journey.
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
    /// Advances the journey to the next step.
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
    /// Resolves the step that matches a given HTTP request path and method.
    /// Returns the matching step or null if no match found.
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
    /// Ends a journey for a session.
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
    /// Gets all active journey sessions (for management APIs).
    /// Note: This is limited by what's in cache and may not be complete.
    /// </summary>
    public IReadOnlyList<(string SessionId, string JourneyName, int CurrentStep, int TotalSteps, bool IsComplete)> GetActiveSessions()
    {
        // Note: IMemoryCache doesn't provide enumeration, so we track sessions separately
        // For now, return empty list - full implementation would need a separate tracking structure
        return Array.Empty<(string, string, int, int, bool)>();
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
