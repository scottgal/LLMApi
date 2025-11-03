using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Hosting;
using mostlylucid.mockllmapi;

namespace LLMApi.Tests;

public class HttpSamplesSmokeTests : IClassFixture<HttpSamplesSmokeTests.CustomFactory>
{
    public class CustomFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                // Re-register the named HttpClient used by LLMockApi to a fake handler
                services.AddHttpClient("LLMockApi").ConfigurePrimaryHttpMessageHandler(() => new FakeCompletionsHandler());

                // Keep requests fast and deterministic
                services.PostConfigure<LLMockApiOptions>(opts =>
                {
                    opts.BaseUrl = "http://example.com/v1/";
                    opts.ModelName = "stub";
                    opts.TimeoutSeconds = 2;
                    opts.EnableVerboseLogging = false;
                });
            });
        }
    }

    private readonly CustomFactory _factory;
    public HttpSamplesSmokeTests(CustomFactory factory) => _factory = factory;

    private static readonly string UberShapeEncoded = "%7B%22%24cache%22%3A3%2C%22users%22%3A%5B%7B%22id%22%3A0%2C%22name%22%3A%22string%22%2C%22email%22%3A%22string%22%2C%22isActive%22%3Atrue%7D%5D%2C%22meta%22%3A%7B%22total%22%3A0%2C%22next%22%3A%22string%22%7D%7D";
    private static readonly string UberShapeSanitized = "{\"users\":[{\"id\":0,\"name\":\"string\",\"email\":\"string\",\"isActive\":true}],\"meta\":{\"total\":0,\"next\":\"string\"}}";

    [Fact]
    public async Task Uber_Get_Includes_Schema_Header_And_Returns_Json()
    {
        var client = _factory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/mock/users?limit=2&offset=0&includeSchema=true&shape={UberShapeEncoded}");
        req.Headers.Accept.Clear();
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var res = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("application/json", res.Content.Headers.ContentType?.MediaType);

        // X-Response-Schema should be present and equal to sanitized shape (without $cache)
        Assert.True(res.Headers.TryGetValues("X-Response-Schema", out var values));
        var schema = values!.First();
        Assert.Equal(UberShapeSanitized, schema);

        var body = await res.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(body));
        // Body should be valid JSON produced by the fake LLM backend
        using var _ = System.Text.Json.JsonDocument.Parse(body);
    }

    [Fact]
    public async Task Header_Shape_Get_Works_And_Echoes_Schema()
    {
        var client = _factory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/mock/products/featured?includeSchema=true");
        req.Headers.Accept.Clear();
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.TryAddWithoutValidation("X-Response-Shape", "{\"products\":[{\"sku\":\"string\",\"price\":0.0,\"tags\":[\"string\"]}],\"count\":0}");

        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.True(res.Headers.TryGetValues("X-Response-Schema", out var values));
        Assert.Equal("{\"products\":[{\"sku\":\"string\",\"price\":0.0,\"tags\":[\"string\"]}],\"count\":0}", values!.First());
    }

    [Fact]
    public async Task Post_Order_Succeeds()
    {
        var client = _factory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/mock/orders");
        req.Headers.Accept.Clear();
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Content = new StringContent("{\"customerId\":\"cus_123\",\"items\":[{\"sku\":\"ABC-001\",\"qty\":2}]}", Encoding.UTF8, "application/json");

        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(body));
    }

    private class FakeCompletionsHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
        {
            // Return an Ollama-like chat/completions payload with a JSON string as content
            var payload = "{\"choices\":[{\"message\":{\"content\":\"{\\\"ok\\\":true,\\\"source\\\":\\\"fake-llm\\\"}\"}}]}";
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(resp);
        }
    }
}
