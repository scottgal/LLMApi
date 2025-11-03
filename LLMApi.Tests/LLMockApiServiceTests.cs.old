using System.Text;
using System.Text.Json;
using System.Net.Http;
using System.Threading;
using mostlylucid.mockllmapi;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LLMApi.Tests;

public class LLMockApiServiceTests
{
    private class CountingHandler : HttpMessageHandler
    {
        private int _count;
        public int Count => _count;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var n = Interlocked.Increment(ref _count);
            var json = $"{{\"choices\":[{{\"message\":{{\"content\":\"resp-{n}\"}}}}]}}";
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
    private class MockHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new HttpClient();
    }

    private class CountingHandlerFactory
    {
        private int _count;
        public int Count => _count;
        public HttpClient CreateClient()
        {
            var handler = new LocalHandler(this);
            return new HttpClient(handler) { BaseAddress = new Uri("http://example.com/v1/") };
        }
        private class LocalHandler : HttpMessageHandler
        {
            private readonly CountingHandlerFactory _factory;
            public LocalHandler(CountingHandlerFactory factory) => _factory = factory;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var n = Interlocked.Increment(ref _factory._count);
                var json = $"{{\"choices\":[{{\"message\":{{\"content\":\"resp-{n}\"}}}}]}}";
                var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                return Task.FromResult(response);
            }
        }
    }

    private class TestHttpClientFactory : IHttpClientFactory
    {
        private readonly CountingHandlerFactory _factory;
        public TestHttpClientFactory(CountingHandlerFactory factory) => _factory = factory;
        public HttpClient CreateClient(string name) => _factory.CreateClient();
        public int Count => _factory.Count;
    }

    private LLMockApiService CreateService(LLMockApiOptions? options = null)
    {
        options ??= new LLMockApiOptions();
        var optionsWrapper = Options.Create(options);
        var httpClientFactory = new MockHttpClientFactory();
        var logger = NullLogger<LLMockApiService>.Instance;
        return new LLMockApiService(optionsWrapper, httpClientFactory, logger);
    }

    private LLMockApiService CreateServiceWithFactory(TestHttpClientFactory factory, LLMockApiOptions? options = null)
    {
        options ??= new LLMockApiOptions { BaseUrl = "http://example.com/v1/", TimeoutSeconds = 5 };
        var optionsWrapper = Options.Create(options);
        var logger = NullLogger<LLMockApiService>.Instance;
        return new LLMockApiService(optionsWrapper, factory, logger);
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
    [Fact]
    public void SchemaHeader_Added_WhenConfigEnabled_AndShapeExists()
    {
        var options = new LLMockApiOptions { IncludeShapeInResponse = true };
        var service = CreateService(options);
        var ctx = new DefaultHttpContext();
        var shape = "{\"id\":0}";

        service.TryAddSchemaHeader(ctx, shape);

        Assert.True(ctx.Response.Headers.ContainsKey("X-Response-Schema"));
        Assert.Equal(shape, ctx.Response.Headers["X-Response-Schema"].ToString());
    }

    [Fact]
    public void SchemaHeader_Added_WhenQueryParamPresent()
    {
        var options = new LLMockApiOptions { IncludeShapeInResponse = false };
        var service = CreateService(options);
        var ctx = new DefaultHttpContext();
        ctx.Request.QueryString = new QueryString("?includeSchema=true");
        var shape = "{\"name\":\"string\"}";

        service.TryAddSchemaHeader(ctx, shape);

        Assert.True(ctx.Response.Headers.ContainsKey("X-Response-Schema"));
        Assert.Equal(shape, ctx.Response.Headers["X-Response-Schema"].ToString());
    }

    [Fact]
    public void SchemaHeader_NotAdded_WhenDisabled_AndNoQuery()
    {
        var options = new LLMockApiOptions { IncludeShapeInResponse = false };
        var service = CreateService(options);
        var ctx = new DefaultHttpContext();
        var shape = "{\"price\":0.0}";

        service.TryAddSchemaHeader(ctx, shape);

        Assert.False(ctx.Response.Headers.ContainsKey("X-Response-Schema"));
    }

    [Fact]
    public void SchemaHeader_NotAdded_WhenNoShapeProvided()
    {
        var options = new LLMockApiOptions { IncludeShapeInResponse = true };
        var service = CreateService(options);
        var ctx = new DefaultHttpContext();

        service.TryAddSchemaHeader(ctx, null);

        Assert.False(ctx.Response.Headers.ContainsKey("X-Response-Schema"));
    }

    [Fact]
    public void SchemaHeader_Omitted_ForLargeShapes()
    {
        var options = new LLMockApiOptions { IncludeShapeInResponse = true };
        var service = CreateService(options);
        var ctx = new DefaultHttpContext();
        var largeShape = new string('x', 4001);

        // Should not throw and should not add header
        service.TryAddSchemaHeader(ctx, largeShape);

        Assert.False(ctx.Response.Headers.ContainsKey("X-Response-Schema"));
    }

    [Fact]
    public async Task Caching_PrimesOnce_ThenServesFromCache_NoExtraFetches()
    {
        // Arrange
        var counterFactory = new CountingHandlerFactory();
        var httpFactory = new TestHttpClientFactory(counterFactory);
        var service = CreateServiceWithFactory(httpFactory, new LLMockApiOptions { MaxCachePerKey = 5 });
        var method = "GET";
        var path = "/api/mock/test?x=1";
        string? body = null;
        var shape = "{\"a\":\"string\"}";

        // Act - first call should prime 3 and serve 1
        var r1 = await service.GetResponseWithCachingAsync(method, path, body, shape, cacheCount: 3);
        var countAfterFirst = httpFactory.Count;
        // Second call should not trigger new upstream fetches (still serving from initial primed cache)
        var r2 = await service.GetResponseWithCachingAsync(method, path, body, shape, cacheCount: 3);
        Assert.Equal(countAfterFirst, httpFactory.Count);
        // Third call serves last cached item; background refill may start, so we do not assert count here
        var r3 = await service.GetResponseWithCachingAsync(method, path, body, shape, cacheCount: 3);

        // Assert
        Assert.Equal(3, countAfterFirst);
        Assert.NotEqual(r1, r2);
        Assert.NotEqual(r2, r3);
    }

    [Fact]
    public async Task Caching_Depletes_TriggersBackgroundRefill()
    {
        // Arrange
        var counterFactory = new CountingHandlerFactory();
        var httpFactory = new TestHttpClientFactory(counterFactory);
        var service = CreateServiceWithFactory(httpFactory, new LLMockApiOptions { MaxCachePerKey = 5 });
        var method = "GET";
        var path = "/api/mock/test?y=2";
        string? body = null;
        var shape = "{\"b\":\"string\"}";

        // Deplete cache (N=2 for speed)
        var a = await service.GetResponseWithCachingAsync(method, path, body, shape, cacheCount: 2);
        var b = await service.GetResponseWithCachingAsync(method, path, body, shape, cacheCount: 2);
        Assert.True(httpFactory.Count >= 2);

        // This call empties queue and schedules background refill
        var c = await service.GetResponseWithCachingAsync(method, path, body, shape, cacheCount: 2);

        // Wait for refill to happen (up to ~1s)
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (httpFactory.Count < 4 && sw.Elapsed < TimeSpan.FromSeconds(2))
        {
            await Task.Delay(50);
        }
        Assert.True(httpFactory.Count >= 4, $"Expected background refill to fetch 2 more, got {httpFactory.Count}");

        // Next call should use cache (no new fetch during call)
        var before = httpFactory.Count;
        var d = await service.GetResponseWithCachingAsync(method, path, body, shape, cacheCount: 2);
        Assert.Equal(before, httpFactory.Count);
    }

    [Fact]
    public void ExtractShapeAndCacheCount_SanitizesAndParses()
    {
        // Arrange
        var service = CreateService();
        var ctx = new DefaultHttpContext();
        ctx.Request.ContentType = "application/json";
        // Manually craft JSON to ensure $cache property included
        var body = "{\"shape\":{\"id\":\"string\",\"name\":\"string\",\"$cache\":3}}";

        // Act
        var sanitized = service.ExtractShapeAndCacheCount(ctx.Request, body, out var cacheCount);

        // Assert
        Assert.Equal(3, cacheCount);
        Assert.NotNull(sanitized);
        Assert.DoesNotContain("$cache", sanitized);
        Assert.Contains("\"id\"", sanitized);
        Assert.Contains("\"name\"", sanitized);
    }
}
