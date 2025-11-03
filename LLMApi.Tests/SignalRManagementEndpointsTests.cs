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
    public async Task ListContexts_ReturnsConfiguredContexts_FromAppsettings()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act - appsettings.json has 2 contexts configured (weather and cars)
        var response = await client.GetAsync("/api/mock/contexts");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ContextListResponse>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(result);
        Assert.NotNull(result.Contexts);
        Assert.Equal(2, result.Count); // weather and cars from appsettings
        Assert.Contains(result.Contexts, c => c.Name == "weather");
        Assert.Contains(result.Contexts, c => c.Name == "cars");
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

    [Fact]
    public async Task CreateContext_WithIsActiveFalse_StartsInStoppedState()
    {
        // Arrange
        var client = _factory.CreateClient();
        var context = new
        {
            name = "inactive-test",
            description = "Test inactive context",
            isActive = false
        };

        // Act
        var createResponse = await client.PostAsJsonAsync("/api/mock/contexts", context);
        var getResponse = await client.GetAsync("/api/mock/contexts/inactive-test");

        // Assert
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        getResponse.EnsureSuccessStatusCode();
        var content = await getResponse.Content.ReadAsStringAsync();
        var contextData = JsonSerializer.Deserialize<HubContextConfig>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(contextData);
        Assert.False(contextData.IsActive);

        // Cleanup
        await client.DeleteAsync("/api/mock/contexts/inactive-test");
    }

    [Fact]
    public async Task StartContext_WithStoppedContext_ActivatesContext()
    {
        // Arrange
        var client = _factory.CreateClient();
        var context = new
        {
            name = "start-test",
            description = "Test start functionality",
            isActive = false
        };

        await client.PostAsJsonAsync("/api/mock/contexts", context);

        // Act
        var startResponse = await client.PostAsync("/api/mock/contexts/start-test/start", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
        var content = await startResponse.Content.ReadAsStringAsync();
        Assert.Contains("started successfully", content);

        // Verify context is now active
        var getResponse = await client.GetAsync("/api/mock/contexts/start-test");
        var contextContent = await getResponse.Content.ReadAsStringAsync();
        var contextData = JsonSerializer.Deserialize<HubContextConfig>(contextContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(contextData);
        Assert.True(contextData.IsActive);

        // Cleanup
        await client.DeleteAsync("/api/mock/contexts/start-test");
    }

    [Fact]
    public async Task StopContext_WithActiveContext_DeactivatesContext()
    {
        // Arrange
        var client = _factory.CreateClient();
        var context = new
        {
            name = "stop-test",
            description = "Test stop functionality",
            isActive = true
        };

        await client.PostAsJsonAsync("/api/mock/contexts", context);

        // Act
        var stopResponse = await client.PostAsync("/api/mock/contexts/stop-test/stop", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, stopResponse.StatusCode);
        var content = await stopResponse.Content.ReadAsStringAsync();
        Assert.Contains("stopped successfully", content);

        // Verify context is now inactive
        var getResponse = await client.GetAsync("/api/mock/contexts/stop-test");
        var contextContent = await getResponse.Content.ReadAsStringAsync();
        var contextData = JsonSerializer.Deserialize<HubContextConfig>(contextContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(contextData);
        Assert.False(contextData.IsActive);

        // Cleanup
        await client.DeleteAsync("/api/mock/contexts/stop-test");
    }

    [Fact]
    public async Task StartContext_WithNonExistentContext_ReturnsNotFound()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.PostAsync("/api/mock/contexts/nonexistent/start", null);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task StopContext_WithNonExistentContext_ReturnsNotFound()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.PostAsync("/api/mock/contexts/nonexistent/stop", null);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ContextLifecycle_StartStopToggle_WorksCorrectly()
    {
        // Arrange
        var client = _factory.CreateClient();
        var context = new
        {
            name = "lifecycle-test",
            description = "Test lifecycle management"
        };

        await client.PostAsJsonAsync("/api/mock/contexts", context);

        // Act & Assert - Stop
        var stopResponse = await client.PostAsync("/api/mock/contexts/lifecycle-test/stop", null);
        Assert.Equal(HttpStatusCode.OK, stopResponse.StatusCode);

        var getAfterStop = await client.GetAsync("/api/mock/contexts/lifecycle-test");
        var contextAfterStop = JsonSerializer.Deserialize<HubContextConfig>(
            await getAfterStop.Content.ReadAsStringAsync(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.False(contextAfterStop?.IsActive);

        // Act & Assert - Start
        var startResponse = await client.PostAsync("/api/mock/contexts/lifecycle-test/start", null);
        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);

        var getAfterStart = await client.GetAsync("/api/mock/contexts/lifecycle-test");
        var contextAfterStart = JsonSerializer.Deserialize<HubContextConfig>(
            await getAfterStart.Content.ReadAsStringAsync(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.True(contextAfterStart?.IsActive);

        // Cleanup
        await client.DeleteAsync("/api/mock/contexts/lifecycle-test");
    }

    [Fact]
    public async Task GetContext_ReturnsConnectionCount()
    {
        // Arrange
        var client = _factory.CreateClient();
        var context = new
        {
            name = "connection-test",
            description = "Test connection count"
        };

        await client.PostAsJsonAsync("/api/mock/contexts", context);

        // Act
        var response = await client.GetAsync("/api/mock/contexts/connection-test");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var contextData = JsonSerializer.Deserialize<HubContextConfig>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(contextData);
        Assert.True(contextData.ConnectionCount >= 0); // ConnectionCount should be 0 or higher

        // Cleanup
        await client.DeleteAsync("/api/mock/contexts/connection-test");
    }

    private class ContextListResponse
    {
        public List<HubContextConfig> Contexts { get; set; } = new();
        public int Count { get; set; }
    }
}
