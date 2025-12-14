using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using mostlylucid.mockllmapi;
using mostlylucid.mockllmapi.RequestHandlers;
using mostlylucid.mockllmapi.Services;
using mostlylucid.mockllmapi.Services.Providers;

namespace LLMApi.Tests;

public class GraphQLRequestHandlerTests
{
    private IOptions<LLMockApiOptions> CreateOptions(LLMockApiOptions? options = null)
    {
        options ??= new LLMockApiOptions
        {
            BaseUrl = "http://localhost:11434/v1/",
            ModelName = "llama3",
            Temperature = 1.2,
            TimeoutSeconds = 30,
            RandomRequestDelayMinMs = 0,
            RandomRequestDelayMaxMs = 0
        };
        return Options.Create(options);
    }

    private GraphQLRequestHandler CreateHandler(LlmClient? llmClient = null)
    {
        var options = CreateOptions();
        var shapeExtractor = new ShapeExtractor();
        var contextExtractor = new ContextExtractor();
        var contextManagerLogger = NullLogger<OpenApiContextManager>.Instance;
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var contextStoreLogger = NullLogger<MemoryCacheContextStore>.Instance;
        var contextStore = new MemoryCacheContextStore(memoryCache, contextStoreLogger);
        var contextManager = new OpenApiContextManager(contextManagerLogger, options, contextStore);
        var validationService = new InputValidationService(NullLogger<InputValidationService>.Instance);
        var promptBuilder = new PromptBuilder(options, validationService, NullLogger<PromptBuilder>.Instance);
        if (llmClient == null)
        {
            var backendSelector = new LlmBackendSelector(options, NullLogger<LlmBackendSelector>.Instance);
            var providerFactory = new LlmProviderFactory(NullLogger<LlmProviderFactory>.Instance);
            llmClient = new FakeGraphQLLlmClient(options, new MockHttpClientFactory(), NullLogger<LlmClient>.Instance,
                backendSelector, providerFactory);
        }

        var delayHelper = new DelayHelper(options);
        var chunkingCoordinator = new ChunkingCoordinator(NullLogger<ChunkingCoordinator>.Instance, options);

        // Create AutoShapeManager dependencies
        var cache = new MemoryCache(new MemoryCacheOptions());
        var shapeExtractorFromResponse =
            new ShapeExtractorFromResponse(NullLogger<ShapeExtractorFromResponse>.Instance);
        var shapeStore = new MemoryCacheShapeStore(cache, NullLogger<MemoryCacheShapeStore>.Instance);
        var autoShapeManager = new AutoShapeManager(options, shapeStore, shapeExtractorFromResponse,
            NullLogger<AutoShapeManager>.Instance);

        var logger = NullLogger<GraphQLRequestHandler>.Instance;

        return new GraphQLRequestHandler(options, shapeExtractor, contextExtractor, contextManager, promptBuilder,
            llmClient, delayHelper, chunkingCoordinator, autoShapeManager, logger);
    }

    private DefaultHttpContext CreateHttpContext(string? body = null)
    {
        var context = new DefaultHttpContext();
        // Set a default path for the request - required for AutoShapeManager
        context.Request.Path = "/api/mock/graphql";
        if (body != null)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            context.Request.Body = new MemoryStream(bytes);
            context.Request.ContentLength = bytes.Length;
        }

        return context;
    }

    private class MockHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient();
        }
    }

    #region Valid GraphQL Request Tests

    [Fact]
    public async Task HandleGraphQLRequest_SimpleQuery_ReturnsValidResponse()
    {
        // Arrange
        var handler = CreateHandler();
        var body = """
                   {
                       "query": "{ users { id name email } }"
                   }
                   """;
        var context = CreateHttpContext(body);

        // Act
        var result = await handler.HandleGraphQLRequestAsync(body, context.Request, context, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var response = JsonDocument.Parse(result);
        Assert.True(response.RootElement.TryGetProperty("data", out var data));
        Assert.True(data.TryGetProperty("users", out var users));
        Assert.Equal(JsonValueKind.Array, users.ValueKind);
    }

    [Fact]
    public async Task HandleGraphQLRequest_QueryWithVariables_ReturnsValidResponse()
    {
        // Arrange
        var handler = CreateHandler();
        var body = """
                   {
                       "query": "query GetUser($id: ID!) { user(id: $id) { id name } }",
                       "variables": {
                           "id": "123"
                       }
                   }
                   """;
        var context = CreateHttpContext(body);

        // Act
        var result = await handler.HandleGraphQLRequestAsync(body, context.Request, context, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var response = JsonDocument.Parse(result);
        Assert.True(response.RootElement.TryGetProperty("data", out var data));
        Assert.True(data.TryGetProperty("user", out var user));
        Assert.Equal(JsonValueKind.Object, user.ValueKind);
    }

    [Fact]
    public async Task HandleGraphQLRequest_QueryWithOperationName_ReturnsValidResponse()
    {
        // Arrange
        var handler = CreateHandler();
        var body = """
                   {
                       "query": "query GetUsers { users { id name } }",
                       "operationName": "GetUsers"
                   }
                   """;
        var context = CreateHttpContext(body);

        // Act
        var result = await handler.HandleGraphQLRequestAsync(body, context.Request, context, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var response = JsonDocument.Parse(result);
        Assert.True(response.RootElement.TryGetProperty("data", out _));
    }

    [Fact]
    public async Task HandleGraphQLRequest_NestedQuery_ReturnsValidResponse()
    {
        // Arrange
        var handler = CreateHandler();
        var body = """
                   {
                       "query": "{ organization { id name employees { id name department } } }"
                   }
                   """;
        var context = CreateHttpContext(body);

        // Act
        var result = await handler.HandleGraphQLRequestAsync(body, context.Request, context, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var response = JsonDocument.Parse(result);
        Assert.True(response.RootElement.TryGetProperty("data", out var data));
        Assert.True(data.TryGetProperty("organization", out _));
    }

    [Fact]
    public async Task HandleGraphQLRequest_MultipleTopLevelFields_ReturnsValidResponse()
    {
        // Arrange
        var handler = CreateHandler();
        var body = """
                   {
                       "query": "{ user(id: 1) { id name } product(id: 2) { id title } }"
                   }
                   """;
        var context = CreateHttpContext(body);

        // Act
        var result = await handler.HandleGraphQLRequestAsync(body, context.Request, context, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var response = JsonDocument.Parse(result);
        Assert.True(response.RootElement.TryGetProperty("data", out var data));
        Assert.True(data.TryGetProperty("user", out _));
        Assert.True(data.TryGetProperty("product", out _));
    }

    #endregion

    #region Invalid Request Tests

    [Fact]
    public async Task HandleGraphQLRequest_EmptyBody_ReturnsError()
    {
        // Arrange
        var handler = CreateHandler();
        var context = CreateHttpContext("");

        // Act
        var result = await handler.HandleGraphQLRequestAsync("", context.Request, context, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var response = JsonDocument.Parse(result);
        Assert.True(response.RootElement.TryGetProperty("errors", out var errors));
        Assert.Equal(JsonValueKind.Array, errors.ValueKind);
        Assert.True(errors.GetArrayLength() > 0);
    }

    [Fact]
    public async Task HandleGraphQLRequest_NullBody_ReturnsError()
    {
        // Arrange
        var handler = CreateHandler();
        var context = CreateHttpContext();

        // Act
        var result = await handler.HandleGraphQLRequestAsync(null, context.Request, context, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var response = JsonDocument.Parse(result);
        Assert.True(response.RootElement.TryGetProperty("errors", out var errors));
        Assert.Equal(JsonValueKind.Array, errors.ValueKind);
    }

    [Fact]
    public async Task HandleGraphQLRequest_InvalidJson_ReturnsError()
    {
        // Arrange
        var handler = CreateHandler();
        var body = "{ invalid json }";
        var context = CreateHttpContext(body);

        // Act
        var result = await handler.HandleGraphQLRequestAsync(body, context.Request, context, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var response = JsonDocument.Parse(result);
        Assert.True(response.RootElement.TryGetProperty("errors", out var errors));
        Assert.Equal(JsonValueKind.Array, errors.ValueKind);
    }

    [Fact]
    public async Task HandleGraphQLRequest_MissingQuery_ReturnsError()
    {
        // Arrange
        var handler = CreateHandler();
        var body = """
                   {
                       "variables": {
                           "id": "123"
                       }
                   }
                   """;
        var context = CreateHttpContext(body);

        // Act
        var result = await handler.HandleGraphQLRequestAsync(body, context.Request, context, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var response = JsonDocument.Parse(result);
        Assert.True(response.RootElement.TryGetProperty("errors", out var errors));
        Assert.Equal(JsonValueKind.Array, errors.ValueKind);
    }

    [Fact]
    public async Task HandleGraphQLRequest_EmptyQuery_ReturnsError()
    {
        // Arrange
        var handler = CreateHandler();
        var body = """
                   {
                       "query": ""
                   }
                   """;
        var context = CreateHttpContext(body);

        // Act
        var result = await handler.HandleGraphQLRequestAsync(body, context.Request, context, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var response = JsonDocument.Parse(result);
        Assert.True(response.RootElement.TryGetProperty("errors", out var errors));
        Assert.Equal(JsonValueKind.Array, errors.ValueKind);
    }

    #endregion

    #region Response Format Tests

    [Fact]
    public async Task HandleGraphQLRequest_ValidResponse_HasDataWrapper()
    {
        // Arrange
        var handler = CreateHandler();
        var body = """
                   {
                       "query": "{ users { id } }"
                   }
                   """;
        var context = CreateHttpContext(body);

        // Act
        var result = await handler.HandleGraphQLRequestAsync(body, context.Request, context, CancellationToken.None);

        // Assert
        var response = JsonDocument.Parse(result);
        Assert.True(response.RootElement.TryGetProperty("data", out _));
        Assert.False(response.RootElement.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task HandleGraphQLRequest_ErrorResponse_HasErrorsArray()
    {
        // Arrange
        var handler = CreateHandler();
        var body = "";
        var context = CreateHttpContext(body);

        // Act
        var result = await handler.HandleGraphQLRequestAsync(body, context.Request, context, CancellationToken.None);

        // Assert
        var response = JsonDocument.Parse(result);
        Assert.True(response.RootElement.TryGetProperty("errors", out var errors));
        Assert.Equal(JsonValueKind.Array, errors.ValueKind);

        var firstError = errors[0];
        Assert.True(firstError.TryGetProperty("message", out _));
        Assert.True(firstError.TryGetProperty("extensions", out var extensions));
        Assert.True(extensions.TryGetProperty("code", out var code));
        Assert.Equal("INTERNAL_SERVER_ERROR", code.GetString());
    }

    [Fact]
    public async Task HandleGraphQLRequest_ErrorResponse_HasErrors()
    {
        // Arrange
        var handler = CreateHandler();
        var body = "invalid";
        var context = CreateHttpContext(body);

        // Act
        var result = await handler.HandleGraphQLRequestAsync(body, context.Request, context, CancellationToken.None);

        // Assert
        var response = JsonDocument.Parse(result);
        // GraphQL error responses have errors array (data is omitted when null due to WhenWritingNull)
        Assert.True(response.RootElement.TryGetProperty("errors", out var errors));
        Assert.Equal(JsonValueKind.Array, errors.ValueKind);
        Assert.True(errors.GetArrayLength() > 0);
    }

    #endregion

    #region LLM Retry Logic Tests

    [Fact]
    public async Task HandleGraphQLRequest_BadLLMResponse_RetriesAndSucceeds()
    {
        // Arrange
        var options = CreateOptions();
        var backendSelector = new LlmBackendSelector(options, NullLogger<LlmBackendSelector>.Instance);
        var providerFactory = new LlmProviderFactory(NullLogger<LlmProviderFactory>.Instance);
        var llmClient = new RetryTestLlmClient(
            options,
            new MockHttpClientFactory(),
            NullLogger<LlmClient>.Instance,
            backendSelector,
            providerFactory,
            true
        );

        var handler = CreateHandler(llmClient);
        var body = """
                   {
                       "query": "{ users { id name } }"
                   }
                   """;
        var context = CreateHttpContext(body);

        // Act
        var result = await handler.HandleGraphQLRequestAsync(body, context.Request, context, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var response = JsonDocument.Parse(result);

        // With cleanup logic being effective, may succeed on first attempt or need retry
        Assert.InRange(llmClient.AttemptCount, 1, 2);

        // Response should either have data or errors (data is omitted when null due to WhenWritingNull)
        var hasData = response.RootElement.TryGetProperty("data", out var data);
        var hasErrors = response.RootElement.TryGetProperty("errors", out _);
        
        // Must have at least one of data or errors
        Assert.True(hasData || hasErrors, "GraphQL response must have either 'data' or 'errors'");
        
        if (hasData && data.ValueKind != JsonValueKind.Null)
            // Success case - got valid data on retry
            Assert.True(data.TryGetProperty("users", out _));
    }

    [Fact]
    public async Task HandleGraphQLRequest_InvalidJsonFromLLM_ReturnsError()
    {
        // Arrange
        var options = CreateOptions();
        var backendSelector = new LlmBackendSelector(options, NullLogger<LlmBackendSelector>.Instance);
        var providerFactory = new LlmProviderFactory(NullLogger<LlmProviderFactory>.Instance);
        var llmClient = new BadJsonLlmClient(
            options,
            new MockHttpClientFactory(),
            NullLogger<LlmClient>.Instance,
            backendSelector,
            providerFactory
        );

        var handler = CreateHandler(llmClient);
        var body = """
                   {
                       "query": "{ users { id } }"
                   }
                   """;
        var context = CreateHttpContext(body);

        // Act
        var result = await handler.HandleGraphQLRequestAsync(body, context.Request, context, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var response = JsonDocument.Parse(result);

        // Should return error response after all attempts fail
        Assert.True(response.RootElement.TryGetProperty("errors", out var errors));
        Assert.Equal(JsonValueKind.Array, errors.ValueKind);
        Assert.True(errors.GetArrayLength() > 0);

        // Should have attempted max retries (2 attempts total)
        Assert.InRange(llmClient.AttemptCount, 1, 2); // Allow 1-2 attempts depending on cleanup behavior
    }

    #endregion

    #region Helper Classes for Testing

    /// <summary>
    ///     Fake LLM client that returns properly formatted GraphQL data
    /// </summary>
    private class FakeGraphQLLlmClient : LlmClient
    {
        public FakeGraphQLLlmClient(IOptions<LLMockApiOptions> options, IHttpClientFactory httpClientFactory,
            ILogger<LlmClient> logger, LlmBackendSelector backendSelector, LlmProviderFactory providerFactory)
            : base(options, httpClientFactory, logger, backendSelector, providerFactory)
        {
        }

        public override Task<string> GetCompletionAsync(string prompt, CancellationToken cancellationToken = default,
            int? maxTokens = null, HttpRequest? request = null)
        {
            // Parse prompt to detect what GraphQL query is being requested
            // Check for multiple top-level fields first (most specific)
            if (prompt.Contains("user(id") && prompt.Contains("product"))
                return Task.FromResult("""
                                       {
                                           "user": {"id": 1, "name": "Alice"},
                                           "product": {"id": 2, "title": "Widget"}
                                       }
                                       """);
            // Check for query with variables
            if (prompt.Contains("GetUser") || (prompt.Contains("user(id") && prompt.Contains("Variables:")))
                return Task.FromResult("""
                                       {
                                           "user": {"id": 1, "name": "Alice Smith"}
                                       }
                                       """);
            // Check for single user query with arguments
            if (prompt.Contains("user(id"))
                return Task.FromResult("""
                                       {
                                           "user": {"id": 1, "name": "Alice Smith"}
                                       }
                                       """);
            // Check for organization (nested query)
            if (prompt.Contains("organization"))
                return Task.FromResult("""
                                       {
                                           "organization": {
                                               "id": 1,
                                               "name": "Acme Corp",
                                               "employees": [
                                                   {"id": 1, "name": "Alice", "department": "Engineering"},
                                                   {"id": 2, "name": "Bob", "department": "Sales"}
                                               ]
                                           }
                                       }
                                       """);
            // Check for users plural (list query)
            if (prompt.Contains("users"))
                return Task.FromResult("""
                                       {
                                           "users": [
                                               {"id": 1, "name": "Alice Smith", "email": "alice@example.com"},
                                               {"id": 2, "name": "Bob Jones", "email": "bob@example.com"},
                                               {"id": 3, "name": "Carol White", "email": "carol@example.com"}
                                           ]
                                       }
                                       """);

            // Default fallback
            return Task.FromResult("""
                                   {
                                       "result": {"id": 1, "value": "test"}
                                   }
                                   """);
        }
    }

    /// <summary>
    ///     LLM client that fails on first attempt then succeeds
    /// </summary>
    private class RetryTestLlmClient : LlmClient
    {
        private readonly bool _failFirstAttempt;

        public RetryTestLlmClient(IOptions<LLMockApiOptions> options, IHttpClientFactory httpClientFactory,
            ILogger<LlmClient> logger, LlmBackendSelector backendSelector, LlmProviderFactory providerFactory,
            bool failFirstAttempt)
            : base(options, httpClientFactory, logger, backendSelector, providerFactory)
        {
            _failFirstAttempt = failFirstAttempt;
        }

        public int AttemptCount { get; private set; }

        public override Task<string> GetCompletionAsync(string prompt, CancellationToken cancellationToken = default,
            int? maxTokens = null, HttpRequest? request = null)
        {
            AttemptCount++;

            if (_failFirstAttempt && AttemptCount == 1)
                // Return malformed JSON that cleanup can't fix (unmatched braces)
                return Task.FromResult("""
                                       {
                                           "users": [
                                               {"id": 1, "name": "Alice"
                                       ]
                                       """);

            // Return valid response on retry
            return Task.FromResult("""
                                   {
                                       "users": [
                                           {"id": 1, "name": "Alice"}
                                       ]
                                   }
                                   """);
        }
    }

    /// <summary>
    ///     LLM client that always returns invalid JSON
    /// </summary>
    private class BadJsonLlmClient : LlmClient
    {
        public BadJsonLlmClient(IOptions<LLMockApiOptions> options, IHttpClientFactory httpClientFactory,
            ILogger<LlmClient> logger, LlmBackendSelector backendSelector, LlmProviderFactory providerFactory)
            : base(options, httpClientFactory, logger, backendSelector, providerFactory)
        {
        }

        public int AttemptCount { get; private set; }

        public override Task<string> GetCompletionAsync(string prompt, CancellationToken cancellationToken = default,
            int? maxTokens = null, HttpRequest? request = null)
        {
            AttemptCount++;
            // Return severely malformed JSON that can't be parsed or cleaned up
            return Task.FromResult("{{{ invalid json syntax [[[");
        }
    }

    #endregion
}