using System.Text;
using System.Text.Json;
using mostlylucid.mockllmapi;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LLMApi.Tests;

public class LLMockApiServiceTests
{
    private class MockHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new HttpClient();
    }

    private LLMockApiService CreateService(LLMockApiOptions? options = null)
    {
        options ??= new LLMockApiOptions();
        var optionsWrapper = Options.Create(options);
        var httpClientFactory = new MockHttpClientFactory();
        var logger = NullLogger<LLMockApiService>.Instance;
        return new LLMockApiService(optionsWrapper, httpClientFactory, logger);
    }

    [Fact]
    public async Task ReadBodyAsync_WithEmptyBody_ReturnsEmptyString()
    {
        // Arrange
        var service = CreateService();
        var context = new DefaultHttpContext();
        context.Request.ContentLength = 0;

        // Act
        var result = await service.ReadBodyAsync(context.Request);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task ReadBodyAsync_WithJsonBody_ReturnsBodyContent()
    {
        // Arrange
        var service = CreateService();
        var context = new DefaultHttpContext();
        var bodyContent = "{\"test\":\"value\"}";
        var bodyBytes = Encoding.UTF8.GetBytes(bodyContent);
        context.Request.Body = new MemoryStream(bodyBytes);
        context.Request.ContentLength = bodyBytes.Length;

        // Act
        var result = await service.ReadBodyAsync(context.Request);

        // Assert
        Assert.Equal(bodyContent, result);
    }

    [Fact]
    public void ExtractShape_FromQueryParameter_ReturnsShapeValue()
    {
        // Arrange
        var service = CreateService();
        var context = new DefaultHttpContext();
        var shapeJson = "{\"id\":\"string\"}";
        context.Request.QueryString = new QueryString($"?shape={Uri.EscapeDataString(shapeJson)}");

        // Act
        var result = service.ExtractShape(context.Request, null);

        // Assert
        Assert.Equal(shapeJson, result);
    }

    [Fact]
    public void ExtractShape_FromHeader_ReturnsShapeValue()
    {
        // Arrange
        var service = CreateService();
        var context = new DefaultHttpContext();
        var shapeJson = "{\"id\":\"string\"}";
        context.Request.Headers["X-Response-Shape"] = shapeJson;

        // Act
        var result = service.ExtractShape(context.Request, null);

        // Assert
        Assert.Equal(shapeJson, result);
    }

    [Fact]
    public void ExtractShape_FromBodyProperty_ReturnsShapeValue()
    {
        // Arrange
        var service = CreateService();
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";
        var body = "{\"shape\":{\"id\":\"string\",\"name\":\"string\"},\"data\":\"test\"}";

        // Act
        var result = service.ExtractShape(context.Request, body);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("\"id\"", result);
        Assert.Contains("\"name\"", result);
    }

    [Fact]
    public void BuildPrompt_IncludesAllRequestDetails()
    {
        // Arrange
        var service = CreateService();
        var method = "POST";
        var path = "/api/mock/users?limit=10";
        var body = "{\"name\":\"test\"}";

        // Act
        var result = service.BuildPrompt(method, path, body, null, streaming: false);

        // Assert
        Assert.Contains("POST", result);
        Assert.Contains("/api/mock/users?limit=10", result);
        Assert.Contains("{\"name\":\"test\"}", result);
        Assert.Contains("COMPLETELY DIFFERENT", result);
        Assert.Contains("creative", result);
    }

    [Fact]
    public void BuildPrompt_WithShape_IncludesShapeInstructions()
    {
        // Arrange
        var service = CreateService();
        var method = "GET";
        var path = "/api/mock/products";
        var shape = "{\"id\":\"string\",\"price\":0.0}";

        // Act
        var result = service.BuildPrompt(method, path, null, shape, streaming: false);

        // Assert
        Assert.Contains("SHAPE REQUIREMENT", result);
        Assert.Contains(shape, result);
        Assert.Contains("strictly conform", result);
    }

    [Fact]
    public void BuildPrompt_IncludesRandomSeed()
    {
        // Arrange
        var service = CreateService();
        var method = "GET";
        var path = "/api/mock/test";

        // Act
        var result1 = service.BuildPrompt(method, path, null, null, streaming: false);
        var result2 = service.BuildPrompt(method, path, null, null, streaming: false);

        // Assert - Each prompt should have different random seed
        Assert.NotEqual(result1, result2);
        Assert.Contains("Random seed:", result1);
        Assert.Contains("Random seed:", result2);
    }

    [Fact]
    public void BuildChatRequest_IncludesAllParameters()
    {
        // Arrange
        var service = CreateService();
        var prompt = "test prompt";

        // Act
        var result = service.BuildChatRequest(prompt, stream: true);
        var json = JsonSerializer.Serialize(result);
        var doc = JsonDocument.Parse(json);

        // Assert
        Assert.Equal("llama3", doc.RootElement.GetProperty("model").GetString());
        Assert.True(doc.RootElement.GetProperty("stream").GetBoolean());
        Assert.Equal(1.2, doc.RootElement.GetProperty("temperature").GetDouble());

        var messages = doc.RootElement.GetProperty("messages");
        Assert.Equal(1, messages.GetArrayLength());
        Assert.Equal("user", messages[0].GetProperty("role").GetString());
        Assert.Equal("test prompt", messages[0].GetProperty("content").GetString());
    }

    [Fact]
    public void Options_CustomModelName_UsedInRequest()
    {
        // Arrange
        var options = new LLMockApiOptions { ModelName = "mixtral" };
        var service = CreateService(options);

        // Act
        var result = service.BuildChatRequest("test", false);
        var json = JsonSerializer.Serialize(result);
        var doc = JsonDocument.Parse(json);

        // Assert
        Assert.Equal("mixtral", doc.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public void Options_CustomTemperature_UsedInRequest()
    {
        // Arrange
        var options = new LLMockApiOptions { Temperature = 0.5 };
        var service = CreateService(options);

        // Act
        var result = service.BuildChatRequest("test", false);
        var json = JsonSerializer.Serialize(result);
        var doc = JsonDocument.Parse(json);

        // Assert
        Assert.Equal(0.5, doc.RootElement.GetProperty("temperature").GetDouble());
    }

    [Fact]
    public void Options_CustomBaseUrl_ReflectedInClient()
    {
        // Arrange
        var customUrl = "http://custom-llm:8080/v1/";
        var options = new LLMockApiOptions { BaseUrl = customUrl };
        var service = CreateService(options);

        // Act
        using var client = service.CreateHttpClient();

        // Assert
        Assert.Equal(customUrl, client.BaseAddress?.ToString());
    }

    [Fact]
    public void Options_CustomPromptTemplate_UsedInPrompt()
    {
        // Arrange
        var template = "Custom: {method} {path} {body} seed:{randomSeed}";
        var options = new LLMockApiOptions { CustomPromptTemplate = template };
        var service = CreateService(options);

        // Act
        var result = service.BuildPrompt("GET", "/test", "body", null, streaming: false);

        // Assert
        Assert.Contains("Custom: GET /test body", result);
        Assert.Contains("seed:", result);
    }
}
