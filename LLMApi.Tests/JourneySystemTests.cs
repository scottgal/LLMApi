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

    #region JourneyExtractor Tests

    [Fact]
    public void JourneyExtractor_ExtractJourneyName_FromQueryParameter()
    {
        // Arrange
        var extractor = new JourneyExtractor();
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.QueryString = new Microsoft.AspNetCore.Http.QueryString("?journey=ecommerce-browse");

        // Act
        var result = extractor.ExtractJourneyName(context.Request, null);

        // Assert
        Assert.Equal("ecommerce-browse", result);
    }

    [Fact]
    public void JourneyExtractor_ExtractJourneyName_FromHeader()
    {
        // Arrange
        var extractor = new JourneyExtractor();
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.Headers["X-Journey"] = "auth-flow";

        // Act
        var result = extractor.ExtractJourneyName(context.Request, null);

        // Assert
        Assert.Equal("auth-flow", result);
    }

    [Fact]
    public void JourneyExtractor_ExtractJourneyName_FromBody()
    {
        // Arrange
        var extractor = new JourneyExtractor();
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.ContentType = "application/json";

        // Act
        var result = extractor.ExtractJourneyName(context.Request, "{\"journey\":\"graphql-exploration\"}");

        // Assert
        Assert.Equal("graphql-exploration", result);
    }

    [Fact]
    public void JourneyExtractor_ExtractJourneyRandom_FromQuery()
    {
        // Arrange
        var extractor = new JourneyExtractor();
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.QueryString = new Microsoft.AspNetCore.Http.QueryString("?journeyRandom=true");

        // Act
        var result = extractor.ExtractJourneyRandom(context.Request);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void JourneyExtractor_ExtractJourneyModality_FromQuery()
    {
        // Arrange
        var extractor = new JourneyExtractor();
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.QueryString = new Microsoft.AspNetCore.Http.QueryString("?journeyModality=GraphQL");

        // Act
        var result = extractor.ExtractJourneyModality(context.Request);

        // Assert
        Assert.Equal("GraphQL", result);
    }

    [Fact]
    public void JourneyExtractor_QueryTakesPrecedenceOverHeader()
    {
        // Arrange
        var extractor = new JourneyExtractor();
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.QueryString = new Microsoft.AspNetCore.Http.QueryString("?journey=from-query");
        context.Request.Headers["X-Journey"] = "from-header";

        // Act
        var result = extractor.ExtractJourneyName(context.Request, null);

        // Assert
        Assert.Equal("from-query", result);
    }

    #endregion

    #region JourneySessionManager Context Integration Tests

    [Fact]
    public void JourneySessionManager_GetJourneyStateForContext_ReturnsCorrectState()
    {
        // Arrange
        var options = new LLMockApiOptions
        {
            Journeys = new JourneysConfig { Enabled = true }
        };
        var optionsMonitor = CreateOptionsMonitor(options);
        var registry = new JourneyRegistry(_registryLoggerMock.Object, optionsMonitor);
        var sessionManager = new JourneySessionManager(_sessionLoggerMock.Object, optionsMonitor, registry, _memoryCache);

        var template = new JourneyTemplate(
            "test-journey",
            JourneyModality.Rest,
            1.0,
            null,
            new List<JourneyStepTemplate>
            {
                new("GET", "/api/step1", null, null, "Step 1", null),
                new("GET", "/api/step2", null, null, "Step 2", null)
            });
        registry.RegisterJourney(template);

        var instance = sessionManager.CreateJourneyInstance("session-123", "test-journey");

        // Act
        var state = sessionManager.GetJourneyStateForContext(instance);

        // Assert
        Assert.Equal("test-journey", state["journey.name"]);
        Assert.Equal("0", state["journey.step"]);
        Assert.Equal("2", state["journey.totalSteps"]);
        Assert.Equal("Rest", state["journey.modality"]);
        Assert.Equal("false", state["journey.isComplete"]);
        Assert.Equal("Step 1", state["journey.stepDescription"]);
    }

    [Fact]
    public void JourneySessionManager_RestoreJourneyFromContext_RestoresSuccessfully()
    {
        // Arrange
        var options = new LLMockApiOptions
        {
            Journeys = new JourneysConfig { Enabled = true }
        };
        var optionsMonitor = CreateOptionsMonitor(options);
        var registry = new JourneyRegistry(_registryLoggerMock.Object, optionsMonitor);
        var sessionManager = new JourneySessionManager(_sessionLoggerMock.Object, optionsMonitor, registry, _memoryCache);

        var template = new JourneyTemplate(
            "test-journey",
            JourneyModality.Rest,
            1.0,
            null,
            new List<JourneyStepTemplate>
            {
                new("GET", "/api/step1", null, null, "Step 1", null),
                new("GET", "/api/step2", null, null, "Step 2", null)
            });
        registry.RegisterJourney(template);

        var sharedData = new Dictionary<string, string>
        {
            ["journey.name"] = "test-journey",
            ["journey.step"] = "1"
        };

        // Act
        var restored = sessionManager.RestoreJourneyFromContext("new-session", sharedData);

        // Assert
        Assert.NotNull(restored);
        Assert.Equal("test-journey", restored.Template.Name);
        Assert.Equal(1, restored.CurrentStepIndex);
        Assert.Equal("Step 2", restored.CurrentStep?.Description);
    }

    [Fact]
    public void JourneySessionManager_GetOrCreateJourney_CreatesNewJourney()
    {
        // Arrange
        var options = new LLMockApiOptions
        {
            Journeys = new JourneysConfig { Enabled = true }
        };
        var optionsMonitor = CreateOptionsMonitor(options);
        var registry = new JourneyRegistry(_registryLoggerMock.Object, optionsMonitor);
        var sessionManager = new JourneySessionManager(_sessionLoggerMock.Object, optionsMonitor, registry, _memoryCache);

        var template = new JourneyTemplate(
            "test-journey",
            JourneyModality.Rest,
            1.0,
            null,
            new List<JourneyStepTemplate>
            {
                new("GET", "/api/step1", null, null, "Step 1", null)
            });
        registry.RegisterJourney(template);

        // Act
        var instance = sessionManager.GetOrCreateJourney(
            "session-123",
            "test-journey",
            startRandom: false,
            modality: null,
            contextSharedData: null);

        // Assert
        Assert.NotNull(instance);
        Assert.Equal("test-journey", instance.Template.Name);
        Assert.Equal(0, instance.CurrentStepIndex);
    }

    [Fact]
    public void JourneySessionManager_GetOrCreateJourney_RestoresFromContextWhenAvailable()
    {
        // Arrange
        var options = new LLMockApiOptions
        {
            Journeys = new JourneysConfig { Enabled = true }
        };
        var optionsMonitor = CreateOptionsMonitor(options);
        var registry = new JourneyRegistry(_registryLoggerMock.Object, optionsMonitor);
        var sessionManager = new JourneySessionManager(_sessionLoggerMock.Object, optionsMonitor, registry, _memoryCache);

        var template = new JourneyTemplate(
            "test-journey",
            JourneyModality.Rest,
            1.0,
            null,
            new List<JourneyStepTemplate>
            {
                new("GET", "/api/step1", null, null, "Step 1", null),
                new("GET", "/api/step2", null, null, "Step 2", null)
            });
        registry.RegisterJourney(template);

        var contextSharedData = new Dictionary<string, string>
        {
            ["journey.name"] = "test-journey",
            ["journey.step"] = "1"
        };

        // Act
        var instance = sessionManager.GetOrCreateJourney(
            "session-123",
            journeyName: null, // No explicit journey specified
            startRandom: false,
            modality: null,
            contextSharedData: contextSharedData);

        // Assert
        Assert.NotNull(instance);
        Assert.Equal("test-journey", instance.Template.Name);
        Assert.Equal(1, instance.CurrentStepIndex); // Restored at step 1
    }

    #endregion

    #region Concurrent Journeys Tests

    [Fact]
    public void JourneyExtractor_ExtractJourneyId_FromQueryParameter()
    {
        // Arrange
        var extractor = new JourneyExtractor();
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.QueryString = new Microsoft.AspNetCore.Http.QueryString("?journeyId=jrn_123456_abc");

        // Act
        var result = extractor.ExtractJourneyId(context.Request, null);

        // Assert
        Assert.Equal("jrn_123456_abc", result);
    }

    [Fact]
    public void JourneyExtractor_ExtractJourneyId_FromHeader()
    {
        // Arrange
        var extractor = new JourneyExtractor();
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.Headers["X-Journey-Id"] = "jrn_789_xyz";

        // Act
        var result = extractor.ExtractJourneyId(context.Request, null);

        // Assert
        Assert.Equal("jrn_789_xyz", result);
    }

    [Fact]
    public void JourneyExtractor_GenerateJourneyId_CreatesUniqueIds()
    {
        // Act
        var id1 = JourneyExtractor.GenerateJourneyId();
        var id2 = JourneyExtractor.GenerateJourneyId();

        // Assert
        Assert.StartsWith("jrn_", id1);
        Assert.StartsWith("jrn_", id2);
        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void JourneySessionManager_ConcurrentJourneys_TrackedSeparately()
    {
        // Arrange
        var options = new LLMockApiOptions
        {
            Journeys = new JourneysConfig { Enabled = true }
        };
        var optionsMonitor = CreateOptionsMonitor(options);
        var registry = new JourneyRegistry(_registryLoggerMock.Object, optionsMonitor);
        var sessionManager = new JourneySessionManager(_sessionLoggerMock.Object, optionsMonitor, registry, _memoryCache);

        // Register two different journey templates
        var ecommerceJourney = new JourneyTemplate(
            "ecommerce-browse",
            JourneyModality.Rest,
            1.0,
            null,
            new List<JourneyStepTemplate>
            {
                new("GET", "/api/products", null, null, "Browse products", null),
                new("GET", "/api/products/*", null, null, "View product", null)
            });

        var authJourney = new JourneyTemplate(
            "auth-flow",
            JourneyModality.Auth,
            1.0,
            null,
            new List<JourneyStepTemplate>
            {
                new("POST", "/api/auth/login", null, null, "Login", null),
                new("GET", "/api/auth/profile", null, null, "Get profile", null)
            });

        registry.RegisterJourney(ecommerceJourney);
        registry.RegisterJourney(authJourney);

        // Act - Start two concurrent journeys with different IDs
        var journey1 = sessionManager.CreateJourneyInstance("journey-1", "ecommerce-browse");
        var journey2 = sessionManager.CreateJourneyInstance("journey-2", "auth-flow");

        // Assert - Both journeys exist and are tracked separately
        Assert.NotNull(journey1);
        Assert.NotNull(journey2);
        Assert.Equal("ecommerce-browse", journey1.Template.Name);
        Assert.Equal("auth-flow", journey2.Template.Name);

        // Verify they can be retrieved separately
        var retrieved1 = sessionManager.GetJourneyForSession("journey-1");
        var retrieved2 = sessionManager.GetJourneyForSession("journey-2");

        Assert.NotNull(retrieved1);
        Assert.NotNull(retrieved2);
        Assert.Equal("ecommerce-browse", retrieved1.Template.Name);
        Assert.Equal("auth-flow", retrieved2.Template.Name);
    }

    [Fact]
    public void JourneySessionManager_ConcurrentJourneys_AdvanceIndependently()
    {
        // Arrange
        var options = new LLMockApiOptions
        {
            Journeys = new JourneysConfig { Enabled = true }
        };
        var optionsMonitor = CreateOptionsMonitor(options);
        var registry = new JourneyRegistry(_registryLoggerMock.Object, optionsMonitor);
        var sessionManager = new JourneySessionManager(_sessionLoggerMock.Object, optionsMonitor, registry, _memoryCache);

        var template = new JourneyTemplate(
            "test-journey",
            JourneyModality.Rest,
            1.0,
            null,
            new List<JourneyStepTemplate>
            {
                new("GET", "/api/step1", null, null, "Step 1", null),
                new("GET", "/api/step2", null, null, "Step 2", null),
                new("GET", "/api/step3", null, null, "Step 3", null)
            });
        registry.RegisterJourney(template);

        // Start two concurrent journeys of the same template
        var journey1 = sessionManager.CreateJourneyInstance("concurrent-1", "test-journey");
        var journey2 = sessionManager.CreateJourneyInstance("concurrent-2", "test-journey");

        // Act - Advance journey1 twice, journey2 once
        sessionManager.AdvanceJourney("concurrent-1");
        sessionManager.AdvanceJourney("concurrent-1");
        sessionManager.AdvanceJourney("concurrent-2");

        // Assert - They should be at different steps
        var updated1 = sessionManager.GetJourneyForSession("concurrent-1");
        var updated2 = sessionManager.GetJourneyForSession("concurrent-2");

        Assert.NotNull(updated1);
        Assert.NotNull(updated2);
        Assert.Equal(2, updated1.CurrentStepIndex); // Advanced twice
        Assert.Equal(1, updated2.CurrentStepIndex); // Advanced once
    }

    [Fact]
    public void JourneySessionManager_GetJourneyStateForContext_IncludesJourneyId()
    {
        // Arrange
        var options = new LLMockApiOptions
        {
            Journeys = new JourneysConfig { Enabled = true }
        };
        var optionsMonitor = CreateOptionsMonitor(options);
        var registry = new JourneyRegistry(_registryLoggerMock.Object, optionsMonitor);
        var sessionManager = new JourneySessionManager(_sessionLoggerMock.Object, optionsMonitor, registry, _memoryCache);

        var template = new JourneyTemplate(
            "test-journey",
            JourneyModality.Rest,
            1.0,
            null,
            new List<JourneyStepTemplate>
            {
                new("GET", "/api/step1", null, null, "Step 1", null)
            });
        registry.RegisterJourney(template);

        var instance = sessionManager.CreateJourneyInstance("my-unique-journey-id", "test-journey");

        // Act
        var state = sessionManager.GetJourneyStateForContext(instance);

        // Assert - Should have both ID-specific and general keys
        Assert.Equal("my-unique-journey-id", state["journey.id"]);
        Assert.Equal("test-journey", state["journey.name"]);
        Assert.Equal("test-journey", state["journey.my-unique-journey-id.name"]);
        Assert.Equal("0", state["journey.my-unique-journey-id.step"]);
    }

    [Fact]
    public void JourneySessionManager_RestoreBySpecificId_WorksCorrectly()
    {
        // Arrange
        var options = new LLMockApiOptions
        {
            Journeys = new JourneysConfig { Enabled = true }
        };
        var optionsMonitor = CreateOptionsMonitor(options);
        var registry = new JourneyRegistry(_registryLoggerMock.Object, optionsMonitor);
        var sessionManager = new JourneySessionManager(_sessionLoggerMock.Object, optionsMonitor, registry, _memoryCache);

        var template = new JourneyTemplate(
            "test-journey",
            JourneyModality.Rest,
            1.0,
            null,
            new List<JourneyStepTemplate>
            {
                new("GET", "/api/step1", null, null, "Step 1", null),
                new("GET", "/api/step2", null, null, "Step 2", null)
            });
        registry.RegisterJourney(template);

        // Create context data with ID-specific journey state
        var sharedData = new Dictionary<string, string>
        {
            ["journey.specific-id.name"] = "test-journey",
            ["journey.specific-id.step"] = "1"
        };

        // Act - Restore using the specific ID
        var restored = sessionManager.RestoreJourneyFromContext("specific-id", sharedData);

        // Assert
        Assert.NotNull(restored);
        Assert.Equal("test-journey", restored.Template.Name);
        Assert.Equal(1, restored.CurrentStepIndex);
        Assert.Equal("specific-id", restored.SessionId);
    }

    #endregion
}
