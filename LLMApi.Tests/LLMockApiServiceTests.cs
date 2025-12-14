using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using mostlylucid.mockllmapi;
using mostlylucid.mockllmapi.Models;
using mostlylucid.mockllmapi.RequestHandlers;
using mostlylucid.mockllmapi.Services;
using mostlylucid.mockllmapi.Services.Providers;
using mostlylucid.mockllmapi.Services.Tools;

namespace LLMApi.Tests;

public class LLMockApiServiceTests
{
    private IOptions<LLMockApiOptions> CreateOptions(LLMockApiOptions? options = null)
    {
        options ??= new LLMockApiOptions();
        return Options.Create(options);
    }

    private LLMockApiService CreateService(LLMockApiOptions? options = null)
    {
        var opts = CreateOptions(options);
        var httpClientFactory = new MockHttpClientFactory();
        var logger = NullLogger<LLMockApiService>.Instance;

        var shapeExtractor = new ShapeExtractor();
        var contextExtractor = new ContextExtractor();
        var journeyExtractor = new JourneyExtractor();
        var memoryCache = new MemoryCache(
            new MemoryCacheOptions());
        var contextStore = new MemoryCacheContextStore(memoryCache, NullLogger<MemoryCacheContextStore>.Instance);
        var contextManager = new OpenApiContextManager(NullLogger<OpenApiContextManager>.Instance, opts, contextStore);
        var validationService = new InputValidationService(NullLogger<InputValidationService>.Instance);
        var promptBuilder = new PromptBuilder(opts, validationService, NullLogger<PromptBuilder>.Instance);
        var backendSelector = new LlmBackendSelector(opts, NullLogger<LlmBackendSelector>.Instance);
        var providerFactory = new LlmProviderFactory(NullLogger<LlmProviderFactory>.Instance);
        var llmClient = new LlmClient(opts, httpClientFactory, NullLogger<LlmClient>.Instance, backendSelector,
            providerFactory);
        var cacheManager = new CacheManager(opts, NullLogger<CacheManager>.Instance);
        var delayHelper = new DelayHelper(opts);
        var chunkingCoordinator = new ChunkingCoordinator(NullLogger<ChunkingCoordinator>.Instance, opts);
        var rateLimitService = new RateLimitService(opts);
        var batchingCoordinator =
            new BatchingCoordinator(llmClient, rateLimitService, opts, NullLogger<BatchingCoordinator>.Instance);

        // Journey system (minimal setup for tests)
        var optionsMonitor = new Mock<IOptionsMonitor<LLMockApiOptions>>();
        optionsMonitor.Setup(m => m.CurrentValue).Returns(opts.Value);
        var journeyRegistry = new JourneyRegistry(NullLogger<JourneyRegistry>.Instance, optionsMonitor.Object);
        var journeySessionManager = new JourneySessionManager(NullLogger<JourneySessionManager>.Instance,
            optionsMonitor.Object, journeyRegistry, memoryCache);
        var journeyPromptInfluencer = new JourneyPromptInfluencer();

        // Tool system (minimal setup for tests)
        var toolExecutors = new IToolExecutor[] { };
        var toolRegistry = new ToolRegistry(toolExecutors, opts, NullLogger<ToolRegistry>.Instance);
        var toolOrchestrator =
            new ToolOrchestrator(toolRegistry, memoryCache, opts, NullLogger<ToolOrchestrator>.Instance);

        // AutoShape system
        var shapeExtractorFromResponse =
            new ShapeExtractorFromResponse(NullLogger<ShapeExtractorFromResponse>.Instance);
        var shapeStore = new MemoryCacheShapeStore(memoryCache, NullLogger<MemoryCacheShapeStore>.Instance);
        var autoShapeManager = new AutoShapeManager(opts, shapeStore, shapeExtractorFromResponse,
            NullLogger<AutoShapeManager>.Instance);

        var regularHandler = new RegularRequestHandler(
            opts, shapeExtractor, contextExtractor, journeyExtractor, contextManager, journeySessionManager,
            journeyPromptInfluencer,
            promptBuilder, llmClient, cacheManager, delayHelper,
            chunkingCoordinator, rateLimitService, batchingCoordinator, toolOrchestrator, autoShapeManager,
            NullLogger<RegularRequestHandler>.Instance);

        var streamingHandler = new StreamingRequestHandler(
            opts, shapeExtractor, contextExtractor, contextManager, promptBuilder, llmClient, delayHelper,
            chunkingCoordinator, autoShapeManager, NullLogger<StreamingRequestHandler>.Instance);

        return new LLMockApiService(regularHandler, streamingHandler);
    }

    private class MockHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient();
        }
    }

    #region Body Reading Tests

    [Fact]
    public async Task ReadBodyAsync_EmptyBody_ReturnsEmptyString()
    {
        // Arrange
        var service = CreateService();
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream();

        // Act
        var result = await service.ReadBodyAsync(context.Request);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task ReadBodyAsync_WithJsonContent_ReturnsContent()
    {
        // Arrange
        var service = CreateService();
        var context = new DefaultHttpContext();
        var content = "{\"test\":\"value\"}";
        var bytes = Encoding.UTF8.GetBytes(content);
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;

        // Act
        var result = await service.ReadBodyAsync(context.Request);

        // Assert
        Assert.Equal(content, result);
    }

    #endregion

    #region ShapeExtractor Tests

    [Fact]
    public void ShapeExtractor_ExtractFromQueryParam()
    {
        // Arrange
        var extractor = new ShapeExtractor();
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?shape={\"id\":0}");

        // Act
        var shapeInfo = extractor.ExtractShapeInfo(context.Request, null);

        // Assert
        Assert.NotNull(shapeInfo.Shape);
        Assert.Contains("id", shapeInfo.Shape);
    }

    [Fact]
    public void ShapeExtractor_ExtractFromHeader()
    {
        // Arrange
        var extractor = new ShapeExtractor();
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Response-Shape"] = "{\"name\":\"string\"}";

        // Act
        var shapeInfo = extractor.ExtractShapeInfo(context.Request, null);

        // Assert
        Assert.NotNull(shapeInfo.Shape);
        Assert.Contains("name", shapeInfo.Shape);
    }

    [Fact]
    public void ShapeExtractor_ExtractCacheCount()
    {
        // Arrange
        var extractor = new ShapeExtractor();
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Response-Shape"] = "{\"$cache\":3,\"id\":0}";

        // Act
        var shapeInfo = extractor.ExtractShapeInfo(context.Request, null);

        // Assert
        Assert.Equal(3, shapeInfo.CacheCount);
        Assert.NotNull(shapeInfo.Shape);
        Assert.DoesNotContain("$cache", shapeInfo.Shape); // Should be sanitized
    }

    [Fact]
    public void ShapeExtractor_DetectJsonSchema()
    {
        // Arrange
        var extractor = new ShapeExtractor();
        var context = new DefaultHttpContext();
        var jsonSchema = @"{
            ""$schema"": ""http://json-schema.org/draft-07/schema#"",
            ""type"": ""object"",
            ""properties"": {
                ""name"": { ""type"": ""string"" }
            }
        }";
        context.Request.Headers["X-Response-Shape"] = jsonSchema;

        // Act
        var shapeInfo = extractor.ExtractShapeInfo(context.Request, null);

        // Assert
        Assert.True(shapeInfo.IsJsonSchema);
    }

    #endregion

    #region PromptBuilder Tests

    [Fact]
    public void PromptBuilder_IncludesRandomness()
    {
        // Arrange
        var options = CreateOptions();
        var validationService = new InputValidationService(NullLogger<InputValidationService>.Instance);
        var builder = new PromptBuilder(options, validationService, NullLogger<PromptBuilder>.Instance);
        var shapeInfo = new ShapeInfo();

        // Act
        var prompt1 = builder.BuildPrompt("GET", "/test", null, shapeInfo, false);
        var prompt2 = builder.BuildPrompt("GET", "/test", null, shapeInfo, false);

        // Assert - prompts should be different due to random seed and timestamp
        Assert.NotEqual(prompt1, prompt2);
        Assert.Contains("RandomSeed:", prompt1);
        Assert.Contains("Time:", prompt1);
    }

    [Fact]
    public void PromptBuilder_IncludesShape()
    {
        // Arrange
        var options = CreateOptions();
        var validationService = new InputValidationService(NullLogger<InputValidationService>.Instance);
        var builder = new PromptBuilder(options, validationService, NullLogger<PromptBuilder>.Instance);
        var shapeInfo = new ShapeInfo { Shape = "{\"id\":0,\"name\":\"string\"}" };

        // Act
        var prompt = builder.BuildPrompt("GET", "/test", null, shapeInfo, false);

        // Assert
        Assert.Contains("SHAPE:", prompt);
        Assert.Contains(shapeInfo.Shape, prompt);
    }

    [Fact]
    public void PromptBuilder_IncludesJsonSchema()
    {
        // Arrange
        var options = CreateOptions();
        var validationService = new InputValidationService(NullLogger<InputValidationService>.Instance);
        var builder = new PromptBuilder(options, validationService, NullLogger<PromptBuilder>.Instance);
        var shapeInfo = new ShapeInfo
        {
            Shape = @"{""$schema"":""http://json-schema.org/draft-07/schema#"",""type"":""object""}",
            IsJsonSchema = true
        };

        // Act
        var prompt = builder.BuildPrompt("GET", "/test", null, shapeInfo, false);

        // Assert
        Assert.Contains("SCHEMA:", prompt);
        Assert.Contains(shapeInfo.Shape, prompt);
    }

    [Fact]
    public void PromptBuilder_UsesCustomTemplate()
    {
        // Arrange
        var options = CreateOptions(new LLMockApiOptions
        {
            CustomPromptTemplate = "Custom: {method} {path} {randomSeed}"
        });
        var validationService = new InputValidationService(NullLogger<InputValidationService>.Instance);
        var builder = new PromptBuilder(options, validationService, NullLogger<PromptBuilder>.Instance);
        var shapeInfo = new ShapeInfo();

        // Act
        var prompt = builder.BuildPrompt("POST", "/custom", "body", shapeInfo, false);

        // Assert
        Assert.StartsWith("Custom: POST /custom", prompt);
    }

    #endregion

    #region DelayHelper Tests

    [Fact]
    public async Task DelayHelper_NoDelay_CompletesQuickly()
    {
        // Arrange
        var options = CreateOptions(new LLMockApiOptions
        {
            RandomRequestDelayMinMs = 0,
            RandomRequestDelayMaxMs = 0
        });
        var helper = new DelayHelper(options);
        var sw = Stopwatch.StartNew();

        // Act
        await helper.ApplyRequestDelayAsync();

        // Assert
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 50); // Should be near instant
    }

    [Fact]
    public async Task DelayHelper_WithDelay_TakesTime()
    {
        // Arrange
        var options = CreateOptions(new LLMockApiOptions
        {
            RandomRequestDelayMinMs = 50,
            RandomRequestDelayMaxMs = 100
        });
        var helper = new DelayHelper(options);
        var sw = Stopwatch.StartNew();

        // Act
        await helper.ApplyRequestDelayAsync();

        // Assert
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds >= 50);
        Assert.True(sw.ElapsedMilliseconds <= 150); // Allow some overhead
    }

    #endregion
}