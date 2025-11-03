using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using mostlylucid.mockllmapi;
using mostlylucid.mockllmapi.Models;
using mostlylucid.mockllmapi.Services;
using Xunit;

namespace LLMApi.Tests;

public class SignalRManagementEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SignalRManagementEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Ensure SignalR services are registered for testing
                services.AddLLMockSignalR(options =>
                {
                    options.BaseUrl = "http://localhost:11434/v1/";
                    options.ModelName = "llama3";
                });
            });
        });
    }

    [Fact]
    public async Task ListContexts_ReturnsEmptyList_WhenNoContextsExist()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/mock/contexts");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ContextListResponse>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(result);
        Assert.NotNull(result.Contexts);
        Assert.Equal(0, result.Count);
    }

    [Fact]
    public async Task CreateContext_WithValidData_ReturnsSuccess()
    {
        // Arrange
        var client = _factory.CreateClient();
        var context = new
        {
            name = "test-context",
            description = "Test description",
            method = "GET",
            path = "/test"
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/mock/contexts", context);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("test-context", content);
        Assert.Contains("registered successfully", content);

        // Cleanup
        await client.DeleteAsync("/api/mock/contexts/test-context");
    }

    [Fact]
    public async Task CreateContext_WithEmptyName_ReturnsBadRequest()
    {
        // Arrange
        var client = _factory.CreateClient();
        var context = new
        {
            name = "",
            description = "Test description"
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/mock/contexts", context);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateContext_WithDuplicateName_ReturnsConflict()
    {
        // Arrange
        var client = _factory.CreateClient();
        var context = new
        {
            name = "duplicate-test",
            description = "Test description"
        };

        // Act
        var response1 = await client.PostAsJsonAsync("/api/mock/contexts", context);
        var response2 = await client.PostAsJsonAsync("/api/mock/contexts", context);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, response2.StatusCode);

        // Cleanup
        await client.DeleteAsync("/api/mock/contexts/duplicate-test");
    }

    [Fact]
    public async Task GetContext_WithExistingContext_ReturnsContext()
    {
        // Arrange
        var client = _factory.CreateClient();
        var context = new
        {
            name = "get-test",
            description = "Test description",
            method = "GET",
            path = "/test"
        };

        await client.PostAsJsonAsync("/api/mock/contexts", context);

        // Act
        var response = await client.GetAsync("/api/mock/contexts/get-test");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("get-test", content);
        Assert.Contains("Test description", content);

        // Cleanup
        await client.DeleteAsync("/api/mock/contexts/get-test");
    }

    [Fact]
    public async Task GetContext_WithNonExistentContext_ReturnsNotFound()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/mock/contexts/nonexistent");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteContext_WithExistingContext_ReturnsSuccess()
    {
        // Arrange
        var client = _factory.CreateClient();
        var context = new
        {
            name = "delete-test",
            description = "Test description"
        };

        await client.PostAsJsonAsync("/api/mock/contexts", context);

        // Act
        var response = await client.DeleteAsync("/api/mock/contexts/delete-test");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("deleted successfully", content);
    }

    [Fact]
    public async Task DeleteContext_WithNonExistentContext_ReturnsNotFound()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.DeleteAsync("/api/mock/contexts/nonexistent");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateListDelete_CompleteWorkflow_Succeeds()
    {
        // Arrange
        var client = _factory.CreateClient();
        var context1 = new { name = "workflow1", description = "First context" };
        var context2 = new { name = "workflow2", description = "Second context" };

        // Act & Assert - Create
        var createResponse1 = await client.PostAsJsonAsync("/api/mock/contexts", context1);
        var createResponse2 = await client.PostAsJsonAsync("/api/mock/contexts", context2);
        Assert.Equal(HttpStatusCode.OK, createResponse1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, createResponse2.StatusCode);

        // Act & Assert - List
        var listResponse = await client.GetAsync("/api/mock/contexts");
        listResponse.EnsureSuccessStatusCode();
        var listContent = await listResponse.Content.ReadAsStringAsync();
        Assert.Contains("workflow1", listContent);
        Assert.Contains("workflow2", listContent);

        // Act & Assert - Get Individual
        var getResponse = await client.GetAsync("/api/mock/contexts/workflow1");
        getResponse.EnsureSuccessStatusCode();
        var getContent = await getResponse.Content.ReadAsStringAsync();
        Assert.Contains("First context", getContent);

        // Act & Assert - Delete
        var deleteResponse1 = await client.DeleteAsync("/api/mock/contexts/workflow1");
        var deleteResponse2 = await client.DeleteAsync("/api/mock/contexts/workflow2");
        Assert.Equal(HttpStatusCode.OK, deleteResponse1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, deleteResponse2.StatusCode);

        // Verify deletion
        var listAfterDelete = await client.GetAsync("/api/mock/contexts");
        var listAfterContent = await listAfterDelete.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ContextListResponse>(listAfterContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(result);

        // Note: There might be other contexts from config, so we just check these specific ones don't exist
        Assert.DoesNotContain("workflow1", listAfterContent);
        Assert.DoesNotContain("workflow2", listAfterContent);
    }

    [Fact]
    public async Task CreateContext_WithShapeProvided_StoresShape()
    {
        // Arrange
        var client = _factory.CreateClient();
        var context = new
        {
            name = "shape-test",
            description = "Test with shape",
            shape = "{\"id\":0,\"name\":\"string\"}",
            isJsonSchema = false
        };

        // Act
        var createResponse = await client.PostAsJsonAsync("/api/mock/contexts", context);
        var getResponse = await client.GetAsync("/api/mock/contexts/shape-test");

        // Assert
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        getResponse.EnsureSuccessStatusCode();
        var content = await getResponse.Content.ReadAsStringAsync();
        Assert.Contains("id", content);
        Assert.Contains("name", content);

        // Cleanup
        await client.DeleteAsync("/api/mock/contexts/shape-test");
    }

    private class ContextListResponse
    {
        public List<HubContextConfig> Contexts { get; set; } = new();
        public int Count { get; set; }
    }
}
