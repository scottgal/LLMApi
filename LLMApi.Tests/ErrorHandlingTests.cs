using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using mostlylucid.mockllmapi;
using mostlylucid.mockllmapi.Models;
using mostlylucid.mockllmapi.RequestHandlers;
using mostlylucid.mockllmapi.Services;
using System.Text;
using System.Text.Json;

namespace LLMApi.Tests;

public class ErrorHandlingTests
{
    #region ErrorConfig Model Tests

    [Fact]
    public void ErrorConfig_Constructor_WithStatusCode_SetsStatusCode()
    {
        // Arrange & Act
        var error = new ErrorConfig(404);

        // Assert
        Assert.Equal(404, error.StatusCode);
        Assert.Null(error.Message);
        Assert.Null(error.Details);
    }

    [Fact]
    public void ErrorConfig_Constructor_WithAllParameters_SetsAllProperties()
    {
        // Arrange & Act
        var error = new ErrorConfig(500, "Custom error", "Detailed info");

        // Assert
        Assert.Equal(500, error.StatusCode);
        Assert.Equal("Custom error", error.Message);
        Assert.Equal("Detailed info", error.Details);
    }

    [Fact]
    public void ErrorConfig_GetDefaultMessage_ReturnsCorrectMessages()
    {
        // Arrange & Act & Assert
        Assert.Equal("Bad Request", new ErrorConfig(400).GetDefaultMessage());
        Assert.Equal("Unauthorized", new ErrorConfig(401).GetDefaultMessage());
        Assert.Equal("Forbidden", new ErrorConfig(403).GetDefaultMessage());
        Assert.Equal("Not Found", new ErrorConfig(404).GetDefaultMessage());
        Assert.Equal("Internal Server Error", new ErrorConfig(500).GetDefaultMessage());
        Assert.Equal("Service Unavailable", new ErrorConfig(503).GetDefaultMessage());
        Assert.Equal("Error 999", new ErrorConfig(999).GetDefaultMessage());
    }

    [Fact]
    public void ErrorConfig_GetMessage_UsesCustomMessageWhenProvided()
    {
        // Arrange
        var error = new ErrorConfig(404, "Custom not found");

        // Act & Assert
        Assert.Equal("Custom not found", error.GetMessage());
    }

    [Fact]
    public void ErrorConfig_GetMessage_UsesDefaultWhenNoCustomMessage()
    {
        // Arrange
        var error = new ErrorConfig(404);

        // Act & Assert
        Assert.Equal("Not Found", error.GetMessage());
    }

    [Fact]
    public void ErrorConfig_ToJson_WithoutDetails_ReturnsValidJson()
    {
        // Arrange
        var error = new ErrorConfig(404, "Resource not found");

        // Act
        var json = error.ToJson();

        // Assert
        var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("error", out var errorObj));
        Assert.True(errorObj.TryGetProperty("code", out var code));
        Assert.Equal(404, code.GetInt32());
        Assert.True(errorObj.TryGetProperty("message", out var message));
        Assert.Equal("Resource not found", message.GetString());
        Assert.False(errorObj.TryGetProperty("details", out _));
    }

    [Fact]
    public void ErrorConfig_ToJson_WithDetails_IncludesDetails()
    {
        // Arrange
        var error = new ErrorConfig(500, "Server error", "Stack trace here");

        // Act
        var json = error.ToJson();

        // Assert
        var doc = JsonDocument.Parse(json);
        var errorObj = doc.RootElement.GetProperty("error");
        Assert.True(errorObj.TryGetProperty("details", out var details));
        Assert.Equal("Stack trace here", details.GetString());
    }

    [Fact]
    public void ErrorConfig_ToGraphQLJson_ReturnsGraphQLFormat()
    {
        // Arrange
        var error = new ErrorConfig(401, "Unauthorized access");

        // Act
        var json = error.ToGraphQLJson();

        // Assert
        var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("errors", out var errors));
        Assert.Equal(JsonValueKind.Array, errors.ValueKind);
        Assert.Equal(1, errors.GetArrayLength());

        var firstError = errors[0];
        Assert.True(firstError.TryGetProperty("message", out var message));
        Assert.Equal("Unauthorized access", message.GetString());
        Assert.True(firstError.TryGetProperty("extensions", out var extensions));
        Assert.True(extensions.TryGetProperty("code", out var code));
        Assert.Equal(401, code.GetInt32());

        Assert.True(doc.RootElement.TryGetProperty("data", out var data));
        Assert.Equal(JsonValueKind.Null, data.ValueKind);
    }

    [Fact]
    public void ErrorConfig_ToGraphQLJson_WithDetails_IncludesDetails()
    {
        // Arrange
        var error = new ErrorConfig(400, "Bad query", "Invalid syntax");

        // Act
        var json = error.ToGraphQLJson();

        // Assert
        var doc = JsonDocument.Parse(json);
        var errors = doc.RootElement.GetProperty("errors");
        var firstError = errors[0];
        Assert.True(firstError.TryGetProperty("extensions", out var extensions));
        Assert.True(extensions.TryGetProperty("details", out var details));
        Assert.Equal("Invalid syntax", details.GetString());
    }

    [Fact]
    public void ErrorConfig_ToJson_EscapesSpecialCharacters()
    {
        // Arrange
        var error = new ErrorConfig(500, "Error with \"quotes\" and\nnewlines\tand tabs");

        // Act
        var json = error.ToJson();

        // Assert - Should be valid JSON
        var doc = JsonDocument.Parse(json);
        var message = doc.RootElement.GetProperty("error").GetProperty("message").GetString();
        Assert.Contains("\\\"", json); // Escaped quotes in raw JSON
        Assert.Contains("\\n", json);  // Escaped newline in raw JSON
        Assert.Contains("\\t", json);  // Escaped tab in raw JSON
    }

    #endregion

    #region ShapeExtractor Error Extraction Tests

    [Fact]
    public void ShapeExtractor_ExtractErrorFromQueryParam_Simple()
    {
        // Arrange
        var extractor = new ShapeExtractor();
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?error=404");

        // Act
        var shapeInfo = extractor.ExtractShapeInfo(context.Request, null);

        // Assert
        Assert.NotNull(shapeInfo.ErrorConfig);
        Assert.Equal(404, shapeInfo.ErrorConfig.StatusCode);
    }

    [Fact]
    public void ShapeExtractor_ExtractErrorFromQueryParam_WithMessageAndDetails()
    {
        // Arrange
        var extractor = new ShapeExtractor();
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?error=403&errorMessage=Access%20denied&errorDetails=Insufficient%20permissions");

        // Act
        var shapeInfo = extractor.ExtractShapeInfo(context.Request, null);

        // Assert
        Assert.NotNull(shapeInfo.ErrorConfig);
        Assert.Equal(403, shapeInfo.ErrorConfig.StatusCode);
        Assert.Equal("Access denied", shapeInfo.ErrorConfig.Message);
        Assert.Equal("Insufficient permissions", shapeInfo.ErrorConfig.Details);
    }

    [Fact]
    public void ShapeExtractor_ExtractErrorFromHeaders()
    {
        // Arrange
        var extractor = new ShapeExtractor();
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Error-Code"] = "500";
        context.Request.Headers["X-Error-Message"] = "Server failure";
        context.Request.Headers["X-Error-Details"] = "Database connection lost";

        // Act
        var shapeInfo = extractor.ExtractShapeInfo(context.Request, null);

        // Assert
        Assert.NotNull(shapeInfo.ErrorConfig);
        Assert.Equal(500, shapeInfo.ErrorConfig.StatusCode);
        Assert.Equal("Server failure", shapeInfo.ErrorConfig.Message);
        Assert.Equal("Database connection lost", shapeInfo.ErrorConfig.Details);
    }

    [Fact]
    public void ShapeExtractor_ExtractErrorFromShapeJson_Simple()
    {
        // Arrange
        var extractor = new ShapeExtractor();
        var context = new DefaultHttpContext();
        var shapeJson = """{"$error": 429}""";
        context.Request.QueryString = new QueryString($"?shape={Uri.EscapeDataString(shapeJson)}");

        // Act
        var shapeInfo = extractor.ExtractShapeInfo(context.Request, null);

        // Assert
        Assert.NotNull(shapeInfo.ErrorConfig);
        Assert.Equal(429, shapeInfo.ErrorConfig.StatusCode);
    }

    [Fact]
    public void ShapeExtractor_ExtractErrorFromShapeJson_Complex()
    {
        // Arrange
        var extractor = new ShapeExtractor();
        var context = new DefaultHttpContext();
        var shapeJson = """
        {
            "$error": {
                "code": 422,
                "message": "Validation failed",
                "details": "Email format invalid"
            }
        }
        """;
        context.Request.QueryString = new QueryString($"?shape={Uri.EscapeDataString(shapeJson)}");

        // Act
        var shapeInfo = extractor.ExtractShapeInfo(context.Request, null);

        // Assert
        Assert.NotNull(shapeInfo.ErrorConfig);
        Assert.Equal(422, shapeInfo.ErrorConfig.StatusCode);
        Assert.Equal("Validation failed", shapeInfo.ErrorConfig.Message);
        Assert.Equal("Email format invalid", shapeInfo.ErrorConfig.Details);
    }

    [Fact]
    public void ShapeExtractor_ExtractErrorFromBody_Simple()
    {
        // Arrange
        var extractor = new ShapeExtractor();
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";
        var body = """{"error": 400}""";

        // Act
        var shapeInfo = extractor.ExtractShapeInfo(context.Request, body);

        // Assert
        Assert.NotNull(shapeInfo.ErrorConfig);
        Assert.Equal(400, shapeInfo.ErrorConfig.StatusCode);
    }

    [Fact]
    public void ShapeExtractor_ExtractErrorFromBody_Complex()
    {
        // Arrange
        var extractor = new ShapeExtractor();
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";
        var body = """
        {
            "error": {
                "code": 409,
                "message": "Resource conflict",
                "details": "User already exists"
            }
        }
        """;

        // Act
        var shapeInfo = extractor.ExtractShapeInfo(context.Request, body);

        // Assert
        Assert.NotNull(shapeInfo.ErrorConfig);
        Assert.Equal(409, shapeInfo.ErrorConfig.StatusCode);
        Assert.Equal("Resource conflict", shapeInfo.ErrorConfig.Message);
        Assert.Equal("User already exists", shapeInfo.ErrorConfig.Details);
    }

    [Fact]
    public void ShapeExtractor_ErrorPrecedence_QueryOverridesOthers()
    {
        // Arrange
        var extractor = new ShapeExtractor();
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?error=404");
        context.Request.Headers["X-Error-Code"] = "500";
        context.Request.ContentType = "application/json";
        var body = """{"error": 400}""";

        // Act
        var shapeInfo = extractor.ExtractShapeInfo(context.Request, body);

        // Assert - Query param should win
        Assert.NotNull(shapeInfo.ErrorConfig);
        Assert.Equal(404, shapeInfo.ErrorConfig.StatusCode);
    }

    [Fact]
    public void ShapeExtractor_ErrorPrecedence_HeaderOverridesBody()
    {
        // Arrange
        var extractor = new ShapeExtractor();
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Error-Code"] = "500";
        context.Request.ContentType = "application/json";
        var body = """{"error": 400}""";

        // Act
        var shapeInfo = extractor.ExtractShapeInfo(context.Request, body);

        // Assert - Header should win
        Assert.NotNull(shapeInfo.ErrorConfig);
        Assert.Equal(500, shapeInfo.ErrorConfig.StatusCode);
    }

    [Fact]
    public void ShapeExtractor_SanitizesErrorHints_FromShape()
    {
        // Arrange
        var extractor = new ShapeExtractor();
        var context = new DefaultHttpContext();
        var shapeJson = """
        {
            "$error": 404,
            "id": "string",
            "name": "string"
        }
        """;
        context.Request.QueryString = new QueryString($"?shape={Uri.EscapeDataString(shapeJson)}");

        // Act
        var shapeInfo = extractor.ExtractShapeInfo(context.Request, null);

        // Assert
        Assert.NotNull(shapeInfo.ErrorConfig);
        Assert.NotNull(shapeInfo.Shape);
        Assert.DoesNotContain("$error", shapeInfo.Shape);
        Assert.Contains("\"id\"", shapeInfo.Shape);
        Assert.Contains("\"name\"", shapeInfo.Shape);
    }

    [Fact]
    public void ShapeExtractor_InvalidStatusCode_IgnoresError()
    {
        // Arrange
        var extractor = new ShapeExtractor();
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?error=999999"); // Invalid status code

        // Act
        var shapeInfo = extractor.ExtractShapeInfo(context.Request, null);

        // Assert
        Assert.Null(shapeInfo.ErrorConfig);
    }

    #endregion

    #region Request Handler Error Response Tests

    [Fact]
    public async Task RegularRequestHandler_WithErrorConfig_ReturnsErrorResponse()
    {
        // Arrange
        var options = Options.Create(new LLMockApiOptions
        {
            BaseUrl = "http://localhost:11434/v1/",
            ModelName = "llama3"
        });
        var shapeExtractor = new ShapeExtractor();
        var contextExtractor = new ContextExtractor();
        var contextManager = new OpenApiContextManager(NullLogger<OpenApiContextManager>.Instance, options);
        var promptBuilder = new PromptBuilder(options);
        var llmClient = new FakeLlmClient(options, new MockHttpClientFactory(), NullLogger<LlmClient>.Instance);
        var cacheManager = new CacheManager(options, NullLogger<CacheManager>.Instance);
        var delayHelper = new DelayHelper(options);
        var handler = new RegularRequestHandler(options, shapeExtractor, contextExtractor, contextManager,
            promptBuilder, llmClient, cacheManager, delayHelper, NullLogger<RegularRequestHandler>.Instance);

        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?error=503");
        context.Response.Body = new MemoryStream();

        // Act
        var result = await handler.HandleRequestAsync("GET", "/api/test", null, context.Request, context, CancellationToken.None);

        // Assert
        Assert.Equal(503, context.Response.StatusCode);
        var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("error", out var error));
        Assert.Equal(503, error.GetProperty("code").GetInt32());
        Assert.Equal("Service Unavailable", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task GraphQLRequestHandler_WithErrorConfig_ReturnsGraphQLError()
    {
        // Arrange
        var options = Options.Create(new LLMockApiOptions
        {
            BaseUrl = "http://localhost:11434/v1/",
            ModelName = "llama3"
        });
        var shapeExtractor = new ShapeExtractor();
        var contextExtractor = new ContextExtractor();
        var contextManager = new OpenApiContextManager(NullLogger<OpenApiContextManager>.Instance, options);
        var promptBuilder = new PromptBuilder(options);
        var llmClient = new FakeLlmClient(options, new MockHttpClientFactory(), NullLogger<LlmClient>.Instance);
        var delayHelper = new DelayHelper(options);
        var handler = new GraphQLRequestHandler(options, shapeExtractor, contextExtractor, contextManager,
            promptBuilder, llmClient, delayHelper, NullLogger<GraphQLRequestHandler>.Instance);

        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?error=401&errorMessage=Token%20expired");
        context.Response.Body = new MemoryStream();
        var body = """{"query": "{ users { id } }"}""";

        // Act
        var result = await handler.HandleGraphQLRequestAsync(body, context.Request, context, CancellationToken.None);

        // Assert
        Assert.Equal(401, context.Response.StatusCode);
        var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("errors", out var errors));
        Assert.Equal(1, errors.GetArrayLength());
        var firstError = errors[0];
        Assert.Equal("Token expired", firstError.GetProperty("message").GetString());
        Assert.Equal(401, firstError.GetProperty("extensions").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task StreamingRequestHandler_WithErrorConfig_ReturnsSSEError()
    {
        // Arrange
        var options = Options.Create(new LLMockApiOptions
        {
            BaseUrl = "http://localhost:11434/v1/",
            ModelName = "llama3"
        });
        var shapeExtractor = new ShapeExtractor();
        var contextExtractor = new ContextExtractor();
        var contextManager = new OpenApiContextManager(NullLogger<OpenApiContextManager>.Instance, options);
        var promptBuilder = new PromptBuilder(options);
        var llmClient = new FakeLlmClient(options, new MockHttpClientFactory(), NullLogger<LlmClient>.Instance);
        var delayHelper = new DelayHelper(options);
        var handler = new StreamingRequestHandler(options, shapeExtractor, contextExtractor, contextManager,
            promptBuilder, llmClient, delayHelper, NullLogger<StreamingRequestHandler>.Instance);

        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?error=429&errorMessage=Rate%20limit%20exceeded");
        context.Response.Body = new MemoryStream();

        // Act
        await handler.HandleStreamingRequestAsync("GET", "/api/test", null, context.Request, context, CancellationToken.None);

        // Assert
        Assert.Equal(429, context.Response.StatusCode);
        Assert.Equal("text/event-stream", context.Response.ContentType);

        context.Response.Body.Position = 0;
        var reader = new StreamReader(context.Response.Body);
        var output = await reader.ReadToEndAsync();

        Assert.StartsWith("data: ", output);
        var jsonPart = output.Substring(6).TrimEnd();
        var doc = JsonDocument.Parse(jsonPart);
        Assert.True(doc.RootElement.TryGetProperty("error", out var error));
        Assert.Equal(429, error.GetProperty("code").GetInt32());
    }

    #endregion

    #region Helper Classes

    private class MockHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new HttpClient();
    }

    private class FakeLlmClient : LlmClient
    {
        public FakeLlmClient(IOptions<LLMockApiOptions> options, IHttpClientFactory httpClientFactory,
            Microsoft.Extensions.Logging.ILogger<LlmClient> logger) : base(options, httpClientFactory, logger)
        {
        }

        public override Task<string> GetCompletionAsync(string prompt, CancellationToken cancellationToken = default, int? maxTokens = null)
        {
            return Task.FromResult("""{"id": 1, "name": "Test"}""");
        }
    }

    #endregion
}
