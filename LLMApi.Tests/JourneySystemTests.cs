using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using mostlylucid.mockllmapi;
using mostlylucid.mockllmapi.Models;
using mostlylucid.mockllmapi.Services;

namespace LLMApi.Tests;

public class JourneySystemTests
{
    private readonly Mock<ILogger<JourneyRegistry>> _registryLoggerMock;
    private readonly Mock<ILogger<JourneySessionManager>> _sessionLoggerMock;
    private readonly IMemoryCache _memoryCache;

    public JourneySystemTests()
    {
        _registryLoggerMock = new Mock<ILogger<JourneyRegistry>>();
        _sessionLoggerMock = new Mock<ILogger<JourneySessionManager>>();
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
    }

    private IOptionsMonitor<LLMockApiOptions> CreateOptionsMonitor(LLMockApiOptions options)
    {
        var mock = new Mock<IOptionsMonitor<LLMockApiOptions>>();
        mock.Setup(m => m.CurrentValue).Returns(options);
        return mock.Object;
    }

    #region JourneyRegistry Tests

    [Fact]
    public void JourneyRegistry_IsEnabled_WhenConfigured()
    {
        // Arrange
        var options = new LLMockApiOptions
        {
            Journeys = new JourneysConfig { Enabled = true }
        };
        var registry = new JourneyRegistry(_registryLoggerMock.Object, CreateOptionsMonitor(options));

        // Act & Assert
        Assert.True(registry.IsEnabled);
    }

    [Fact]
    public void JourneyRegistry_IsDisabled_WhenNotConfigured()
    {
        // Arrange
        var options = new LLMockApiOptions { Journeys = null };
        var registry = new JourneyRegistry(_registryLoggerMock.Object, CreateOptionsMonitor(options));

        // Act & Assert
        Assert.False(registry.IsEnabled);
    }

    [Fact]
    public void JourneyRegistry_RegisterJourney_AddsJourneySuccessfully()
    {
        // Arrange
        var options = new LLMockApiOptions();
        var registry = new JourneyRegistry(_registryLoggerMock.Object, CreateOptionsMonitor(options));
        var journey = new JourneyTemplate(
            Name: "test-journey",
            Modality: JourneyModality.Rest,
            Weight: 1.0,
            PromptHints: null,
            Steps: new List<JourneyStepTemplate>
            {
                new("GET", "/api/users", null, null, "Get users", null)
            });

        // Act
        registry.RegisterJourney(journey);
        var retrieved = registry.GetJourney("test-journey");

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("test-journey", retrieved.Name);
        Assert.Single(retrieved.Steps);
    }

    [Fact]
    public void JourneyRegistry_GetJourneysByModality_ReturnsCorrectJourneys()
    {
        // Arrange
        var options = new LLMockApiOptions();
        var registry = new JourneyRegistry(_registryLoggerMock.Object, CreateOptionsMonitor(options));

        registry.RegisterJourney(new JourneyTemplate("rest-1", JourneyModality.Rest, 1.0, null,
            new List<JourneyStepTemplate> { new("GET", "/api", null, null, null, null) }));
        registry.RegisterJourney(new JourneyTemplate("graphql-1", JourneyModality.GraphQL, 1.0, null,
            new List<JourneyStepTemplate> { new("POST", "/graphql", null, null, null, null) }));
        registry.RegisterJourney(new JourneyTemplate("rest-2", JourneyModality.Rest, 1.0, null,
            new List<JourneyStepTemplate> { new("POST", "/api", null, null, null, null) }));

        // Act
        var restJourneys = registry.GetJourneysByModality(JourneyModality.Rest);
        var graphqlJourneys = registry.GetJourneysByModality(JourneyModality.GraphQL);

        // Assert
        Assert.Equal(2, restJourneys.Count);
        Assert.Single(graphqlJourneys);
    }

    [Fact]
    public void JourneyRegistry_RemoveJourney_RemovesJourneySuccessfully()
    {
        // Arrange
        var options = new LLMockApiOptions();
        var registry = new JourneyRegistry(_registryLoggerMock.Object, CreateOptionsMonitor(options));
        registry.RegisterJourney(new JourneyTemplate("to-remove", JourneyModality.Rest, 1.0, null,
            new List<JourneyStepTemplate> { new("GET", "/api", null, null, null, null) }));

        // Act
        var removed = registry.RemoveJourney("to-remove");
        var retrieved = registry.GetJourney("to-remove");

        // Assert
        Assert.True(removed);
        Assert.Null(retrieved);
    }

    [Fact]
    public void JourneyRegistry_SelectRandomJourney_RespectsWeights()
    {
        // Arrange
        var options = new LLMockApiOptions();
        var registry = new JourneyRegistry(_registryLoggerMock.Object, CreateOptionsMonitor(options));

        // High weight journey
        registry.RegisterJourney(new JourneyTemplate("high-weight", JourneyModality.Rest, 100.0, null,
            new List<JourneyStepTemplate> { new("GET", "/api", null, null, null, null) }));
        // Very low weight journey
        registry.RegisterJourney(new JourneyTemplate("low-weight", JourneyModality.Rest, 0.001, null,
            new List<JourneyStepTemplate> { new("GET", "/api", null, null, null, null) }));

        // Act - select many times
        var selections = new Dictionary<string, int> { { "high-weight", 0 }, { "low-weight", 0 } };
        var random = new Random(42); // Seeded for reproducibility
        for (var i = 0; i < 100; i++)
        {
            var selected = registry.SelectRandomJourney(random: random);
            if (selected != null)
                selections[selected.Name]++;
        }

        // Assert - high weight should be selected most of the time
        Assert.True(selections["high-weight"] > selections["low-weight"]);
    }

    [Fact]
    public void JourneyRegistry_LoadsFromConfiguration()
    {
        // Arrange
        var options = new LLMockApiOptions
        {
            Journeys = new JourneysConfig
            {
                Enabled = true,
                Journeys = new List<JourneyTemplateConfig>
                {
                    new()
                    {
                        Name = "config-journey",
                        Modality = "Rest",
                        Weight = 2.0,
                        Steps = new List<JourneyStepConfig>
                        {
                            new() { Method = "GET", Path = "/api/test", Description = "Test step" }
                        }
                    }
                }
            }
        };
        var registry = new JourneyRegistry(_registryLoggerMock.Object, CreateOptionsMonitor(options));

        // Act
        var journey = registry.GetJourney("config-journey");

        // Assert
        Assert.NotNull(journey);
        Assert.Equal(JourneyModality.Rest, journey.Modality);
        Assert.Equal(2.0, journey.Weight);
        Assert.Single(journey.Steps);
    }

    #endregion

    #region JourneySessionManager Tests

    [Fact]
    public void JourneySessionManager_CreateJourneyInstance_CreatesSuccessfully()
    {
        // Arrange
        var options = new LLMockApiOptions { ContextExpirationMinutes = 15 };
        var registry = new JourneyRegistry(_registryLoggerMock.Object, CreateOptionsMonitor(options));
        registry.RegisterJourney(new JourneyTemplate("test-journey", JourneyModality.Rest, 1.0, null,
            new List<JourneyStepTemplate>
            {
                new("GET", "/api/users/{{userId}}", null, null, "Get user", null),
                new("POST", "/api/orders", null, null, "Create order", null)
            }));

        var sessionManager = new JourneySessionManager(
            _sessionLoggerMock.Object,
            CreateOptionsMonitor(options),
            registry,
            _memoryCache);

        // Act
        var instance = sessionManager.CreateJourneyInstance(
            "session-123",
            "test-journey",
            new Dictionary<string, string> { { "userId", "456" } });

        // Assert
        Assert.NotNull(instance);
        Assert.Equal("session-123", instance.SessionId);
        Assert.Equal("test-journey", instance.Template.Name);
        Assert.Equal(0, instance.CurrentStepIndex);
        Assert.False(instance.IsComplete);
        Assert.Equal(2, instance.ResolvedSteps.Count);
        Assert.Equal("/api/users/456", instance.ResolvedSteps[0].Path); // Variable resolved
    }

    [Fact]
    public void JourneySessionManager_AdvanceJourney_ProgressesSteps()
    {
        // Arrange
        var options = new LLMockApiOptions { ContextExpirationMinutes = 15 };
        var registry = new JourneyRegistry(_registryLoggerMock.Object, CreateOptionsMonitor(options));
        registry.RegisterJourney(new JourneyTemplate("test-journey", JourneyModality.Rest, 1.0, null,
            new List<JourneyStepTemplate>
            {
                new("GET", "/api/step1", null, null, "Step 1", null),
                new("GET", "/api/step2", null, null, "Step 2", null),
                new("GET", "/api/step3", null, null, "Step 3", null)
            }));

        var sessionManager = new JourneySessionManager(
            _sessionLoggerMock.Object,
            CreateOptionsMonitor(options),
            registry,
            _memoryCache);

        sessionManager.CreateJourneyInstance("session-123", "test-journey");

        // Act
        var step1 = sessionManager.GetJourneyForSession("session-123");
        sessionManager.AdvanceJourney("session-123");
        var step2 = sessionManager.GetJourneyForSession("session-123");
        sessionManager.AdvanceJourney("session-123");
        var step3 = sessionManager.GetJourneyForSession("session-123");
        sessionManager.AdvanceJourney("session-123");
        var completed = sessionManager.GetJourneyForSession("session-123");

        // Assert
        Assert.Equal(0, step1!.CurrentStepIndex);
        Assert.Equal(1, step2!.CurrentStepIndex);
        Assert.Equal(2, step3!.CurrentStepIndex);
        Assert.True(completed!.IsComplete);
    }

    [Fact]
    public void JourneySessionManager_GetJourneyForSession_ReturnsNullForUnknownSession()
    {
        // Arrange
        var options = new LLMockApiOptions { ContextExpirationMinutes = 15 };
        var registry = new JourneyRegistry(_registryLoggerMock.Object, CreateOptionsMonitor(options));
        var sessionManager = new JourneySessionManager(
            _sessionLoggerMock.Object,
            CreateOptionsMonitor(options),
            registry,
            _memoryCache);

        // Act
        var result = sessionManager.GetJourneyForSession("unknown-session");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void JourneySessionManager_EndJourney_RemovesSession()
    {
        // Arrange
        var options = new LLMockApiOptions { ContextExpirationMinutes = 15 };
        var registry = new JourneyRegistry(_registryLoggerMock.Object, CreateOptionsMonitor(options));
        registry.RegisterJourney(new JourneyTemplate("test-journey", JourneyModality.Rest, 1.0, null,
            new List<JourneyStepTemplate> { new("GET", "/api", null, null, null, null) }));

        var sessionManager = new JourneySessionManager(
            _sessionLoggerMock.Object,
            CreateOptionsMonitor(options),
            registry,
            _memoryCache);

        sessionManager.CreateJourneyInstance("session-123", "test-journey");

        // Act
        var ended = sessionManager.EndJourney("session-123");
        var afterEnd = sessionManager.GetJourneyForSession("session-123");

        // Assert
        Assert.True(ended);
        Assert.Null(afterEnd);
    }

    [Fact]
    public void JourneySessionManager_ResolveStepForRequest_MatchesCurrentStep()
    {
        // Arrange
        var options = new LLMockApiOptions { ContextExpirationMinutes = 15 };
        var registry = new JourneyRegistry(_registryLoggerMock.Object, CreateOptionsMonitor(options));
        registry.RegisterJourney(new JourneyTemplate("test-journey", JourneyModality.Rest, 1.0, null,
            new List<JourneyStepTemplate>
            {
                new("GET", "/api/users", null, null, "Get users", null),
                new("POST", "/api/orders", null, null, "Create order", null)
            }));

        var sessionManager = new JourneySessionManager(
            _sessionLoggerMock.Object,
            CreateOptionsMonitor(options),
            registry,
            _memoryCache);

        var instance = sessionManager.CreateJourneyInstance("session-123", "test-journey");

        // Act
        var matchingStep = sessionManager.ResolveStepForRequest(instance, "GET", "/api/users");
        var nonMatchingStep = sessionManager.ResolveStepForRequest(instance, "DELETE", "/api/users");

        // Assert
        Assert.NotNull(matchingStep);
        Assert.Equal("Get users", matchingStep.Description);
        Assert.Null(nonMatchingStep);
    }

    #endregion

    #region JourneyPromptInfluencer Tests

    [Fact]
    public void JourneyPromptInfluencer_BuildInfluence_CreatesCorrectInfluence()
    {
        // Arrange
        var influencer = new JourneyPromptInfluencer();
        var template = new JourneyTemplate(
            "test-journey",
            JourneyModality.Rest,
            1.0,
            new JourneyPromptHints("E-commerce scenario", "Varied data", "Payment info", "medium"),
            new List<JourneyStepTemplate>
            {
                new("GET", "/api/products", null, null, "Browse products",
                    new JourneyStepPromptHints(
                        HighlightFields: new List<string> { "name", "price" },
                        LureFields: new List<string> { "internalSku" },
                        Tone: "engaging"))
            });

        var instance = new JourneyInstance(
            "session-123",
            template,
            new Dictionary<string, string> { { "userId", "456" } },
            template.Steps,
            0);

        var contextSnapshot = new ApiContextSnapshot(
            new Dictionary<string, string> { { "lastProductId", "789" } },
            new Dictionary<string, string>(),
            new Dictionary<string, string>());

        // Act
        var influence = influencer.BuildJourneyPromptInfluence(
            instance,
            instance.CurrentStep!,
            contextSnapshot,
            "fallback-seed");

        // Assert
        Assert.Equal("test-journey", influence.JourneyName);
        Assert.Equal(JourneyModality.Rest, influence.Modality);
        Assert.Equal("E-commerce scenario", influence.Scenario);
        Assert.Equal("Browse products", influence.StepDescription);
        Assert.Equal("engaging", influence.Tone);
        Assert.Contains("name", influence.HighlightFields);
        Assert.Contains("internalSku", influence.LureFields);
    }

    [Fact]
    public void JourneyPromptInfluencer_GenerateRandomnessSeed_IsConsistent()
    {
        // Act
        var seed1 = JourneyPromptInfluencer.GenerateRandomnessSeed("session-1", "GET", "/api/users", 0);
        var seed2 = JourneyPromptInfluencer.GenerateRandomnessSeed("session-1", "GET", "/api/users", 0);
        var seed3 = JourneyPromptInfluencer.GenerateRandomnessSeed("session-1", "GET", "/api/users", 1);

        // Assert
        Assert.Equal(seed1, seed2); // Same inputs = same seed
        Assert.NotEqual(seed1, seed3); // Different step = different seed
    }

    [Fact]
    public void JourneyPromptInfluencer_FormatInfluenceForPrompt_CreatesReadableOutput()
    {
        // Arrange
        var influence = new JourneyPromptInfluence(
            JourneyName: "test-journey",
            Modality: JourneyModality.Rest,
            Scenario: "E-commerce",
            DataStyle: "Realistic",
            RiskFlavor: "Payment",
            RandomnessProfile: "medium",
            StepDescription: "Browse products",
            Tone: "engaging",
            RandomnessSeed: "abc123",
            PromotedContext: new Dictionary<string, string> { { "userId", "123" } },
            DemotedContext: new Dictionary<string, string>(),
            HighlightFields: new List<string> { "name" },
            LureFields: new List<string> { "secret" },
            RawStepHints: new Dictionary<string, object>());

        // Act
        var formatted = JourneyPromptInfluencer.FormatInfluenceForPrompt(influence);

        // Assert
        Assert.Contains("Journey: test-journey", formatted);
        Assert.Contains("Modality: Rest", formatted);
        Assert.Contains("Scenario: E-commerce", formatted);
        Assert.Contains("Step: Browse products", formatted);
        Assert.Contains("userId: 123", formatted);
    }

    #endregion

    #region JourneyConfig Tests

    [Fact]
    public void JourneyTemplateConfig_ToRecord_ConvertsCorrectly()
    {
        // Arrange
        var config = new JourneyTemplateConfig
        {
            Name = "test-journey",
            Modality = "GraphQL",
            Weight = 2.5,
            PromptHints = new JourneyPromptHintsConfig
            {
                Scenario = "Social platform",
                DataStyle = "Rich nested data"
            },
            Steps = new List<JourneyStepConfig>
            {
                new()
                {
                    Method = "POST",
                    Path = "/graphql",
                    Description = "Query users",
                    PromptHints = new JourneyStepPromptHintsConfig
                    {
                        Tone = "professional",
                        HighlightFields = new List<string> { "id", "name" }
                    }
                }
            }
        };

        // Act
        var record = config.ToRecord();

        // Assert
        Assert.Equal("test-journey", record.Name);
        Assert.Equal(JourneyModality.GraphQL, record.Modality);
        Assert.Equal(2.5, record.Weight);
        Assert.Equal("Social platform", record.PromptHints?.Scenario);
        Assert.Single(record.Steps);
        Assert.Equal("professional", record.Steps[0].PromptHints?.Tone);
    }

    [Fact]
    public void JourneyTemplateConfig_ToRecord_HandlesInvalidModality()
    {
        // Arrange
        var config = new JourneyTemplateConfig
        {
            Name = "test",
            Modality = "InvalidModality",
            Steps = new List<JourneyStepConfig>
            {
                new() { Method = "GET", Path = "/api" }
            }
        };

        // Act
        var record = config.ToRecord();

        // Assert
        Assert.Equal(JourneyModality.Other, record.Modality);
    }

    #endregion

    #region JourneyInstance Tests

    [Fact]
    public void JourneyInstance_CurrentStep_ReturnsCorrectStep()
    {
        // Arrange
        var steps = new List<JourneyStepTemplate>
        {
            new("GET", "/api/step1", null, null, "Step 1", null),
            new("GET", "/api/step2", null, null, "Step 2", null)
        };
        var template = new JourneyTemplate("test", JourneyModality.Rest, 1.0, null, steps);
        var instance = new JourneyInstance("session-1", template, new Dictionary<string, string>(), steps, 1);

        // Act & Assert
        Assert.Equal("Step 2", instance.CurrentStep?.Description);
    }

    [Fact]
    public void JourneyInstance_IsComplete_WhenStepIndexExceedsSteps()
    {
        // Arrange
        var steps = new List<JourneyStepTemplate>
        {
            new("GET", "/api/step1", null, null, "Step 1", null)
        };
        var template = new JourneyTemplate("test", JourneyModality.Rest, 1.0, null, steps);
        var instance = new JourneyInstance("session-1", template, new Dictionary<string, string>(), steps, 1);

        // Act & Assert
        Assert.True(instance.IsComplete);
        Assert.Null(instance.CurrentStep);
    }

    [Fact]
    public void JourneyInstance_AdvanceStep_ReturnsNewInstance()
    {
        // Arrange
        var steps = new List<JourneyStepTemplate>
        {
            new("GET", "/api/step1", null, null, "Step 1", null),
            new("GET", "/api/step2", null, null, "Step 2", null)
        };
        var template = new JourneyTemplate("test", JourneyModality.Rest, 1.0, null, steps);
        var instance = new JourneyInstance("session-1", template, new Dictionary<string, string>(), steps, 0);

        // Act
        var advanced = instance.AdvanceStep();

        // Assert
        Assert.Equal(0, instance.CurrentStepIndex); // Original unchanged
        Assert.Equal(1, advanced.CurrentStepIndex); // New instance advanced
    }

    #endregion
}
