using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using mostlylucid.mockllmapi.Models;

namespace mostlylucid.mockllmapi.Services;

/// <summary>
/// Registry for journey templates. Loads journeys from configuration and provides
/// access to them by name or modality.
/// </summary>
public class JourneyRegistry
{
    private readonly ILogger<JourneyRegistry> _logger;
    private readonly IOptionsMonitor<LLMockApiOptions> _options;
    private readonly object _lock = new();
    private Dictionary<string, JourneyTemplate> _journeysByName = new(StringComparer.OrdinalIgnoreCase);
    private List<JourneyTemplate> _allJourneys = new();
    private bool _initialized;

    public JourneyRegistry(
        ILogger<JourneyRegistry> logger,
        IOptionsMonitor<LLMockApiOptions> options)
    {
        _logger = logger;
        _options = options;
        _options.OnChange(_ => ReloadJourneys());
    }

    /// <summary>
    /// Gets whether journeys are enabled.
    /// </summary>
    public bool IsEnabled => _options.CurrentValue.Journeys?.Enabled ?? false;

    /// <summary>
    /// Gets all registered journey templates.
    /// </summary>
    public IReadOnlyList<JourneyTemplate> GetAllJourneys()
    {
        EnsureInitialized();
        lock (_lock)
        {
            return _allJourneys.ToList().AsReadOnly();
        }
    }

    /// <summary>
    /// Gets a journey template by name.
    /// </summary>
    public JourneyTemplate? GetJourney(string name)
    {
        EnsureInitialized();
        lock (_lock)
        {
            return _journeysByName.TryGetValue(name, out var journey) ? journey : null;
        }
    }

    /// <summary>
    /// Gets all journeys for a specific modality.
    /// </summary>
    public IReadOnlyList<JourneyTemplate> GetJourneysByModality(JourneyModality modality)
    {
        EnsureInitialized();
        lock (_lock)
        {
            return _allJourneys.Where(j => j.Modality == modality).ToList().AsReadOnly();
        }
    }

    /// <summary>
    /// Selects a random journey based on weights, optionally filtered by modality.
    /// </summary>
    public JourneyTemplate? SelectRandomJourney(JourneyModality? modality = null, Random? random = null)
    {
        EnsureInitialized();
        random ??= Random.Shared;

        List<JourneyTemplate> candidates;
        lock (_lock)
        {
            candidates = modality.HasValue
                ? _allJourneys.Where(j => j.Modality == modality.Value).ToList()
                : _allJourneys.ToList();
        }

        if (candidates.Count == 0)
            return null;

        var totalWeight = candidates.Sum(j => j.Weight);
        if (totalWeight <= 0)
            return candidates[random.Next(candidates.Count)];

        var roll = random.NextDouble() * totalWeight;
        var cumulative = 0.0;

        foreach (var journey in candidates)
        {
            cumulative += journey.Weight;
            if (roll <= cumulative)
                return journey;
        }

        return candidates[^1];
    }

    /// <summary>
    /// Registers a journey template programmatically.
    /// </summary>
    public void RegisterJourney(JourneyTemplate journey)
    {
        if (journey == null) throw new ArgumentNullException(nameof(journey));
        if (string.IsNullOrWhiteSpace(journey.Name))
            throw new ArgumentException("Journey name cannot be empty.", nameof(journey));

        lock (_lock)
        {
            _journeysByName[journey.Name] = journey;

            // Remove existing journey with same name and add new one
            _allJourneys.RemoveAll(j => j.Name.Equals(journey.Name, StringComparison.OrdinalIgnoreCase));
            _allJourneys.Add(journey);

            _logger.LogInformation("Registered journey '{JourneyName}' with {StepCount} steps",
                journey.Name, journey.Steps.Count);
        }
    }

    /// <summary>
    /// Removes a journey template by name.
    /// </summary>
    public bool RemoveJourney(string name)
    {
        lock (_lock)
        {
            var removed = _journeysByName.Remove(name);
            if (removed)
            {
                _allJourneys.RemoveAll(j => j.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                _logger.LogInformation("Removed journey '{JourneyName}'", name);
            }
            return removed;
        }
    }

    /// <summary>
    /// Gets journey names and their step counts for management APIs.
    /// </summary>
    public IReadOnlyList<(string Name, JourneyModality Modality, int StepCount, double Weight)> GetJourneySummaries()
    {
        EnsureInitialized();
        lock (_lock)
        {
            return _allJourneys
                .Select(j => (j.Name, j.Modality, j.Steps.Count, j.Weight))
                .ToList()
                .AsReadOnly();
        }
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;

        lock (_lock)
        {
            if (_initialized) return;
            LoadJourneysFromConfig();
            _initialized = true;
        }
    }

    private void ReloadJourneys()
    {
        lock (_lock)
        {
            LoadJourneysFromConfig();
            _logger.LogInformation("Reloaded journeys from configuration");
        }
    }

    private void LoadJourneysFromConfig()
    {
        var config = _options.CurrentValue.Journeys;
        if (config?.Journeys == null || config.Journeys.Count == 0)
        {
            _logger.LogDebug("No journeys configured");
            return;
        }

        var newJourneys = new List<JourneyTemplate>();
        var newByName = new Dictionary<string, JourneyTemplate>(StringComparer.OrdinalIgnoreCase);

        foreach (var journeyConfig in config.Journeys)
        {
            try
            {
                var journey = journeyConfig.ToRecord();
                newJourneys.Add(journey);
                newByName[journey.Name] = journey;
                _logger.LogDebug("Loaded journey '{JourneyName}' with {StepCount} steps",
                    journey.Name, journey.Steps.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load journey '{JourneyName}'", journeyConfig.Name);
            }
        }

        _allJourneys = newJourneys;
        _journeysByName = newByName;
        _logger.LogInformation("Loaded {JourneyCount} journeys from configuration", newJourneys.Count);
    }
}
