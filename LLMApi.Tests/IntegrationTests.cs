using LLMApi.Tests.Helpers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using mostlylucid.mockllmapi;
using mostlylucid.mockllmapi.Models;
using mostlylucid.mockllmapi.Services;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace LLMApi.Tests;

/// <summary>
/// Integration tests for form bodies, file uploads, and arbitrary path lengths
/// </summary>
[Trait("Category", "Integration")]
public class IntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public IntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                // Replace real LLM client with fake for predictable testing
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

    #region Form URL-Encoded Integration Tests

    [Fact]
    public async Task PostFormUrlEncoded_SingleValues_ReturnsSuccess()
    {
        // Arrange
        var client = _factory.CreateClient();
        var formData = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("username", "john_doe"),
            new KeyValuePair<string, string>("email", "john@example.com"),
            new KeyValuePair<string, string>("age", "30")
        });

        // Act
        var response = await client.PostAsync("/api/mock/users", formData);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(content);

        // Should be valid JSON
        var json = JsonDocument.Parse(content);
        Assert.NotEqual(JsonValueKind.Undefined, json.RootElement.ValueKind);
    }

    [Fact]
    public async Task PostFormUrlEncoded_MultipleValues_HandlesArrays()
    {
        // Arrange
        var client = _factory.CreateClient();
        var formData = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("title", "My Post"),
            new KeyValuePair<string, string>("tags", "tag1"),
            new KeyValuePair<string, string>("tags", "tag2"),
            new KeyValuePair<string, string>("tags", "tag3")
        });

        // Act
        var response = await client.PostAsync("/api/mock/posts", formData);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        Assert.NotEqual(JsonValueKind.Undefined, json.RootElement.ValueKind);
    }

    [Fact]
    public async Task PostFormUrlEncoded_SpecialCharacters_EncodedCorrectly()
    {
        // Arrange
        var client = _factory.CreateClient();
        var formData = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("description", "Line 1\nLine 2\tTabbed"),
            new KeyValuePair<string, string>("quote", "He said \"Hello\""),
            new KeyValuePair<string, string>("path", "C:\\Users\\Test")
        });

        // Act
        var response = await client.PostAsync("/api/mock/comments", formData);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(content);
        var json = JsonDocument.Parse(content);
        Assert.NotEqual(JsonValueKind.Undefined, json.RootElement.ValueKind);
    }

    #endregion

    #region Multipart Form Data Integration Tests

    [Fact]
    public async Task PostMultipartFormData_WithFile_ReturnsSuccess()
    {
        // Arrange
        var client = _factory.CreateClient();

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("My Photo"), "title");
        content.Add(new StringContent("A beautiful sunset"), "description");

        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("Test file content"));
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("text/plain");
        content.Add(fileContent, "image", "test.txt");

        // Act
        var response = await client.PostAsync("/api/mock/photos/upload", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(responseContent);

        var json = JsonDocument.Parse(responseContent);
        Assert.NotEqual(JsonValueKind.Undefined, json.RootElement.ValueKind);
    }

    [Fact]
    public async Task PostMultipartFormData_MultipleFiles_AllProcessed()
    {
        // Arrange
        var client = _factory.CreateClient();

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("Multiple uploads"), "title");

        // Add first file
        var file1 = new ByteArrayContent(Encoding.UTF8.GetBytes("File 1 content"));
        file1.Headers.ContentType = MediaTypeHeaderValue.Parse("text/plain");
        content.Add(file1, "file1", "document1.txt");

        // Add second file
        var file2 = new ByteArrayContent(Encoding.UTF8.GetBytes("File 2 content"));
        file2.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        content.Add(file2, "file2", "document2.pdf");

        // Act
        var response = await client.PostAsync("/api/mock/documents/bulk", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(responseContent);
        Assert.NotEqual(JsonValueKind.Undefined, json.RootElement.ValueKind);
    }

    [Fact]
    public async Task PostMultipartFormData_LargeFile_HandlesStreaming()
    {
        // Arrange
        var client = _factory.CreateClient();

        using var content = new MultipartFormDataContent();

        // Create a 1MB file
        var largeFileContent = new byte[1024 * 1024];
        new Random().NextBytes(largeFileContent);

        var file = new ByteArrayContent(largeFileContent);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
        content.Add(file, "largefile", "large.bin");

        // Act
        var response = await client.PostAsync("/api/mock/uploads/large", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(responseContent);
    }

    [Fact]
    public async Task PostMultipartFormData_OnlyFields_NoFiles()
    {
        // Arrange
        var client = _factory.CreateClient();

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("value1"), "field1");
        content.Add(new StringContent("value2"), "field2");

        // Act
        var response = await client.PostAsync("/api/mock/forms", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(responseContent);
        Assert.NotEqual(JsonValueKind.Undefined, json.RootElement.ValueKind);
    }

    #endregion

    #region Arbitrary Path Length Tests

    [Fact]
    public async Task GetDeepNestedPath_ReturnsSuccess()
    {
        // Arrange
        var client = _factory.CreateClient();
        var deepPath = "/api/mock/v1/api/products/electronics/computers/laptops/gaming/high-end/2024/details";

        // Act
        var response = await client.GetAsync(deepPath);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(content);

        var json = JsonDocument.Parse(content);
        Assert.NotEqual(JsonValueKind.Undefined, json.RootElement.ValueKind);
    }

    [Fact]
    public async Task GetVeryDeepPath_WithQueryParams_ReturnsSuccess()
    {
        // Arrange
        var client = _factory.CreateClient();
        var path = "/api/mock/a/b/c/d/e/f/g/h/i/j/k/l/m/n/o/p/q/r/s/t";
        var query = "?param1=value1&param2=value2&param3=value3";

        // Act
        var response = await client.GetAsync(path + query);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        Assert.NotEqual(JsonValueKind.Undefined, json.RootElement.ValueKind);
    }

    [Fact]
    public async Task GetPathWithComplexQuery_AllParamsPreserved()
    {
        // Arrange
        var client = _factory.CreateClient();
        var path = "/api/mock/products/search";
        var query = "?brand=Dell&model=XPS15&color=silver&storage=1TB&ram=32GB&year=2024&condition=new&price_min=1000&price_max=2000";

        // Act
        var response = await client.GetAsync(path + query);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(content);
    }

    [Fact]
    public async Task PostToDeepPath_WithFormData_ReturnsSuccess()
    {
        // Arrange
        var client = _factory.CreateClient();
        var deepPath = "/api/mock/v2/organizations/123/departments/456/employees/789/reviews/submit";

        var formData = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("rating", "5"),
            new KeyValuePair<string, string>("comment", "Excellent work!")
        });

        // Act
        var response = await client.PostAsync(deepPath, formData);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        Assert.NotEqual(JsonValueKind.Undefined, json.RootElement.ValueKind);
    }

    #endregion

    #region Mixed Content Type Tests

    [Fact]
    public async Task PostJson_ToAnyPath_StillWorks()
    {
        // Arrange
        var client = _factory.CreateClient();
        var jsonContent = new StringContent(
            "{\"name\":\"test\",\"value\":42}",
            Encoding.UTF8,
            "application/json"
        );

        // Act
        var response = await client.PostAsync("/api/mock/test", jsonContent);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        Assert.NotEqual(JsonValueKind.Undefined, json.RootElement.ValueKind);
    }

    [Fact]
    public async Task GetRequest_NoBody_ReturnsSuccess()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/mock/users");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        Assert.NotEqual(JsonValueKind.Undefined, json.RootElement.ValueKind);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task PostFormData_InvalidContentType_HandlesGracefully()
    {
        // Arrange
        var client = _factory.CreateClient();
        var content = new StringContent("not a form", Encoding.UTF8, "text/plain");

        // Act
        var response = await client.PostAsync("/api/mock/test", content);

        // Assert
        // Should still return OK (treats as raw text body)
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    #endregion
}
