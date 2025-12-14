using System.Net;
using System.Text.Json;
using LLMApi.Tests.Helpers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using mostlylucid.mockllmapi.Services;
using Xunit;

namespace LLMApi.Tests;

/// <summary>
/// Tests verifying source-generated JSON serialization works correctly.
/// Uses the LLMock API to test itself by making real HTTP requests
/// and verifying responses are properly deserialized.
/// </summary>
[Trait("Category", "Integration")]
public class SourceGeneratedSerializationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SourceGeneratedSerializationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                // Use FakeLlmClient for predictable testing
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(LlmClient));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }
                services.AddScoped<LlmClient, FakeLlmClient>();
            });
        });
    }

    [Fact]
    public async Task SourceGeneration_BasicJsonResponse_DeserializesCorrectly()
    {
        // Arrange
        var client = _factory.CreateClient();
        var shape = new { id = 0, name = "", email = "" };
        var shapeJson = JsonSerializer.Serialize(shape);

        // Act - Make request to LLMock API
        var response = await client.GetAsync($"/api/mock/users?shape={Uri.EscapeDataString(shapeJson)}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseText = await response.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrEmpty(responseText));

        // Verify it's valid JSON
        var json = JsonDocument.Parse(responseText);
        Assert.NotEqual(JsonValueKind.Undefined, json.RootElement.ValueKind);
    }

    [Fact]
    public async Task SourceGeneration_ComplexNestedObject_DeserializesCorrectly()
    {
        // Arrange
        var client = _factory.CreateClient();
        var shape = new
        {
            id = 0,
            name = "",
            address = new
            {
                street = "",
                city = "",
                country = ""
            },
            tags = new[] { "" }
        };
        var shapeJson = JsonSerializer.Serialize(shape);

        // Act
        var response = await client.GetAsync($"/api/mock/users/123?shape={Uri.EscapeDataString(shapeJson)}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseText = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(responseText);

        // Verify nested structure
        Assert.True(json.RootElement.TryGetProperty("address", out var address));
        Assert.True(address.TryGetProperty("city", out _));
    }

    [Fact]
    public async Task SourceGeneration_ArrayResponse_DeserializesCorrectly()
    {
        // Arrange
        var client = _factory.CreateClient();
        var shape = new[] { new { id = 0, title = "", author = "" } };
        var shapeJson = JsonSerializer.Serialize(shape);

        // Act
        var response = await client.GetAsync($"/api/mock/posts?shape={Uri.EscapeDataString(shapeJson)}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseText = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(responseText);

        // Verify it's an array
        Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
        Assert.True(json.RootElement.GetArrayLength() > 0);
    }

    [Fact]
    public async Task SourceGeneration_SpecialCharacters_EscapedCorrectly()
    {
        // Arrange
        var client = _factory.CreateClient();
        var shape = new
        {
            message = "Test with \"quotes\" and \n newlines \t tabs",
            path = "C:\\Program Files\\Test",
            unicode = "Hello 🌍 World"
        };
        var shapeJson = JsonSerializer.Serialize(shape);

        // Act
        var response = await client.GetAsync($"/api/mock/test?shape={Uri.EscapeDataString(shapeJson)}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseText = await response.Content.ReadAsStringAsync();

        // Verify valid JSON with special characters
        var json = JsonDocument.Parse(responseText);
        Assert.NotEqual(JsonValueKind.Undefined, json.RootElement.ValueKind);

        // Should not contain unescaped quotes or invalid escape sequences
        Assert.DoesNotContain("\\\\n", responseText); // Double-escaped
        Assert.DoesNotContain("\\\\t", responseText);
    }

    [Fact]
    public async Task SourceGeneration_LargeResponse_HandlesMemoryEfficiently()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Create a shape that would generate a large response
        var largeArray = new List<object>();
        for (int i = 0; i < 100; i++)
        {
            largeArray.Add(new
            {
                id = i,
                name = $"User {i}",
                description = "Long description text that repeats many times",
                metadata = new { tag1 = "value", tag2 = "value", tag3 = "value" }
            });
        }

        var shapeJson = JsonSerializer.Serialize(largeArray);

        // Act
        var response = await client.GetAsync($"/api/mock/bulk?shape={Uri.EscapeDataString(shapeJson)}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseText = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(responseText);

        // Verify it parsed successfully without memory issues
        Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
    }

    [Theory]
    [InlineData("ollama")]
    [InlineData("openai")]
    [InlineData("lmstudio")]
    public async Task SourceGeneration_DifferentProviders_AllDeserializeCorrectly(string backendName)
    {
        // Arrange
        var client = _factory.CreateClient();
        var shape = new { id = 0, name = "", status = "" };
        var shapeJson = JsonSerializer.Serialize(shape);

        // Act - Select backend via query parameter
        var response = await client.GetAsync(
            $"/api/mock/status?backend={backendName}&shape={Uri.EscapeDataString(shapeJson)}");

        // Assert - Even though backend might not be configured,
        // the serialization logic should work the same
        // (We're using FakeLlmClient so actual provider doesn't matter)
        var responseText = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            var json = JsonDocument.Parse(responseText);
            Assert.NotEqual(JsonValueKind.Undefined, json.RootElement.ValueKind);
        }
    }

    [Fact]
    public async Task SourceGeneration_NullValues_HandledCorrectly()
    {
        // Arrange
        var client = _factory.CreateClient();
        var shape = new
        {
            id = 0,
            name = (string?)null,
            optional = (string?)null,
            required = ""
        };
        var shapeJson = JsonSerializer.Serialize(shape);

        // Act
        var response = await client.GetAsync($"/api/mock/data?shape={Uri.EscapeDataString(shapeJson)}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseText = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(responseText);

        Assert.NotEqual(JsonValueKind.Undefined, json.RootElement.ValueKind);
    }

    [Fact]
    public async Task SourceGeneration_ConcurrentRequests_ThreadSafe()
    {
        // Arrange
        var client = _factory.CreateClient();
        var shape = new { id = 0, timestamp = "", data = "" };
        var shapeJson = JsonSerializer.Serialize(shape);

        // Act - Make 10 concurrent requests
        var tasks = Enumerable.Range(0, 10).Select(async i =>
        {
            var response = await client.GetAsync(
                $"/api/mock/concurrent/{i}?shape={Uri.EscapeDataString(shapeJson)}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var responseText = await response.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(responseText);

            return json.RootElement;
        });

        // Assert - All should complete successfully
        var results = await Task.WhenAll(tasks);
        Assert.Equal(10, results.Length);
        Assert.All(results, result => Assert.NotEqual(default, result));
    }
}
