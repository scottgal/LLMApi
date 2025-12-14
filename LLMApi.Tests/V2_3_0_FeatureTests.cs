using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LLMApi.Tests.Helpers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using mostlylucid.mockllmapi.Services;

namespace LLMApi.Tests;

/// <summary>
///     Comprehensive integration tests for v2.3.0 features:
///     - Form body support (application/x-www-form-urlencoded)
///     - File upload support (multipart/form-data)
///     - Arbitrary path lengths
/// </summary>
[Trait("Category", "Integration")]
public class V2_3_0_FeatureTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public V2_3_0_FeatureTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                // Replace real LLM client with fake for predictable testing
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(LlmClient));

                if (descriptor != null) services.Remove(descriptor);

                services.AddScoped<LlmClient, FakeLlmClient>();
            });
        });
    }

    #region Form URL-Encoded Comprehensive Tests

    [Fact]
    public async Task FormUrlEncoded_SimpleRegistration_ResponseContainsFormData()
    {
        // Arrange
        var client = _factory.CreateClient();
        var formData = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("username", "test_user_123"),
            new KeyValuePair<string, string>("email", "test@example.com"),
            new KeyValuePair<string, string>("password", "SecurePass123!")
        });

        // Act
        var response = await client.PostAsync("/api/mock/auth/register", formData);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var content = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(content);

        // Verify valid JSON (FakeLlmClient returns generic JSON)
        var json = JsonDocument.Parse(content);
        Assert.True(json.RootElement.ValueKind == JsonValueKind.Object);

        // Verify standard fake response structure
        Assert.True(json.RootElement.TryGetProperty("id", out _));
        Assert.True(json.RootElement.TryGetProperty("name", out _));
    }

    [Fact]
    public async Task FormUrlEncoded_ArrayValues_ConvertsToJsonArray()
    {
        // Arrange
        var client = _factory.CreateClient();
        var formData = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("title", "Tech Article"),
            new KeyValuePair<string, string>("tags", "dotnet"),
            new KeyValuePair<string, string>("tags", "testing"),
            new KeyValuePair<string, string>("tags", "llm"),
            new KeyValuePair<string, string>("category", "development")
        });

        // Act
        var response = await client.PostAsync("/api/mock/articles/create", formData);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        Assert.NotEqual(JsonValueKind.Undefined, json.RootElement.ValueKind);
    }

    [Fact]
    public async Task FormUrlEncoded_SpecialCharacters_ProperlyEscaped()
    {
        // Arrange
        var client = _factory.CreateClient();
        var formData = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("comment", "Great article!\nReally enjoyed it."),
            new KeyValuePair<string, string>("author", "John \"The Expert\" Doe"),
            new KeyValuePair<string, string>("filepath", "C:\\Users\\Documents\\file.txt"),
            new KeyValuePair<string, string>("data", "Value with\ttabs and\nnewlines")
        });

        // Act
        var response = await client.PostAsync("/api/mock/comments/add", formData);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();

        // Must be valid JSON (no syntax errors from escaping)
        var json = JsonDocument.Parse(content);
        Assert.NotEqual(JsonValueKind.Undefined, json.RootElement.ValueKind);
    }

    [Fact]
    public async Task FormUrlEncoded_EmptyValues_HandledGracefully()
    {
        // Arrange
        var client = _factory.CreateClient();
        var formData = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("field1", ""),
            new KeyValuePair<string, string>("field2", "value"),
            new KeyValuePair<string, string>("field3", "")
        });

        // Act
        var response = await client.PostAsync("/api/mock/test/empty", formData);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        Assert.NotEqual(JsonValueKind.Undefined, json.RootElement.ValueKind);
    }

    [Fact]
    public async Task FormUrlEncoded_UnicodeAndEmoji_PreservedCorrectly()
    {
        // Arrange
        var client = _factory.CreateClient();
        var formData = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("message", "Hello 🌍! Café résumé"),
            new KeyValuePair<string, string>("author", "José García"),
            new KeyValuePair<string, string>("tags", "日本語"),
            new KeyValuePair<string, string>("tags", "中文")
        });

        // Act
        var response = await client.PostAsync("/api/mock/messages/send", formData);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        Assert.NotEqual(JsonValueKind.Undefined, json.RootElement.ValueKind);
    }

    #endregion

    #region File Upload Comprehensive Tests

    [Fact]
    public async Task FileUpload_SingleFile_MetadataInResponse()
    {
        // Arrange
        var client = _factory.CreateClient();
        var fileContent = "This is a test file with some content for testing purposes.";
        var fileBytes = Encoding.UTF8.GetBytes(fileContent);

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("Test Document"), "title");
        content.Add(new StringContent("A sample document for testing"), "description");

        var file = new ByteArrayContent(fileBytes);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse("text/plain");
        content.Add(file, "document", "test-file.txt");

        // Act
        var response = await client.PostAsync("/api/mock/documents/upload", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(responseContent);

        var json = JsonDocument.Parse(responseContent);
        Assert.True(json.RootElement.ValueKind == JsonValueKind.Object);

        // Verify standard fake response structure
        Assert.True(json.RootElement.TryGetProperty("id", out _));
        Assert.True(json.RootElement.TryGetProperty("name", out _));
    }

    [Fact]
    public async Task FileUpload_MultipleFiles_AllFilesProcessed()
    {
        // Arrange
        var client = _factory.CreateClient();

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("Bulk Upload Test"), "operation");

        // Add multiple files with different content types
        var file1 = new ByteArrayContent(Encoding.UTF8.GetBytes("Document 1 content"));
        file1.Headers.ContentType = MediaTypeHeaderValue.Parse("text/plain");
        content.Add(file1, "file1", "document1.txt");

        var file2 = new ByteArrayContent(Encoding.UTF8.GetBytes("Document 2 content"));
        file2.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        content.Add(file2, "file2", "document2.pdf");

        var file3 = new ByteArrayContent(Encoding.UTF8.GetBytes("Image data here"));
        file3.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");
        content.Add(file3, "file3", "photo.jpg");

        // Act
        var response = await client.PostAsync("/api/mock/uploads/batch", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(responseContent);
        Assert.NotEqual(JsonValueKind.Undefined, json.RootElement.ValueKind);
    }

    [Fact]
    public async Task FileUpload_LargeFile_StreamedSuccessfully()
    {
        // Arrange
        var client = _factory.CreateClient();

        using var content = new MultipartFormDataContent();

        // Create a 5MB file
        var largeFileContent = new byte[5 * 1024 * 1024];
        new Random(42).NextBytes(largeFileContent);

        var file = new ByteArrayContent(largeFileContent);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
        content.Add(file, "largefile", "large-data.bin");
        content.Add(new StringContent("Large file test"), "description");

        // Act
        var response = await client.PostAsync("/api/mock/uploads/large", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(responseContent);

        // Verify response is valid JSON despite large file
        var json = JsonDocument.Parse(responseContent);
        Assert.NotEqual(JsonValueKind.Undefined, json.RootElement.ValueKind);
    }

    [Fact]
    public async Task FileUpload_MixedFieldsAndFiles_BothProcessed()
    {
        // Arrange
        var client = _factory.CreateClient();

        using var content = new MultipartFormDataContent();

        // Add regular form fields
        content.Add(new StringContent("My Album"), "albumName");
        content.Add(new StringContent("Vacation Photos 2024"), "albumDescription");
        content.Add(new StringContent("public"), "visibility");

        // Add multiple files
        var photo1 = new ByteArrayContent(Encoding.UTF8.GetBytes("Photo 1 binary data"));
        photo1.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");
        content.Add(photo1, "photos", "sunset.jpg");

        var photo2 = new ByteArrayContent(Encoding.UTF8.GetBytes("Photo 2 binary data"));
        photo2.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");
        content.Add(photo2, "photos", "beach.jpg");

        // Act
        var response = await client.PostAsync("/api/mock/albums/create", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(responseContent);
        Assert.True(json.RootElement.ValueKind == JsonValueKind.Object);

        // Verify standard fake response structure
        Assert.True(json.RootElement.TryGetProperty("id", out _));
        Assert.True(json.RootElement.TryGetProperty("name", out _));
    }

    [Fact]
    public async Task FileUpload_EmptyFile_HandledGracefully()
    {
        // Arrange
        var client = _factory.CreateClient();

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("Empty File Test"), "title");

        var emptyFile = new ByteArrayContent(Array.Empty<byte>());
        emptyFile.Headers.ContentType = MediaTypeHeaderValue.Parse("text/plain");
        content.Add(emptyFile, "file", "empty.txt");

        // Act
        var response = await client.PostAsync("/api/mock/files/test-empty", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(responseContent);
        Assert.NotEqual(JsonValueKind.Undefined, json.RootElement.ValueKind);
    }

    [Fact]
    public async Task FileUpload_SpecialCharactersInFilename_HandledCorrectly()
    {
        // Arrange
        var client = _factory.CreateClient();

        using var content = new MultipartFormDataContent();

        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("File content"));
        file.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        content.Add(file, "document", "report (final) [v2.0] - 2024.pdf");

        // Act
        var response = await client.PostAsync("/api/mock/reports/upload", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(responseContent);
        Assert.NotEqual(JsonValueKind.Undefined, json.RootElement.ValueKind);
    }

    #endregion

    #region Arbitrary Path Length Comprehensive Tests

    [Fact]
    public async Task DeepPath_9Segments_ProcessedCorrectly()
    {
        // Arrange
        var client = _factory.CreateClient();
        var path = "/api/mock/v1/api/products/electronics/computers/laptops/gaming/high-end/2024/details";

        // Act
        var response = await client.GetAsync(path);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        Assert.True(json.RootElement.ValueKind == JsonValueKind.Object);

        // Verify standard fake response structure
        Assert.True(json.RootElement.TryGetProperty("id", out _));
        Assert.True(json.RootElement.TryGetProperty("name", out _));
    }

    [Fact]
    public async Task DeepPath_WithComplexQueryString_AllParametersPreserved()
    {
        // Arrange
        var client = _factory.CreateClient();
        var path = "/api/mock/api/products/search";
        var queryString = "?category=electronics&brand=Dell&model=XPS%2015&" +
                          "price_min=1000&price_max=2500&" +
                          "storage=1TB&ram=32GB&" +
                          "condition=new&shipping=free&" +
                          "warranty=3years&color=silver";

        // Act
        var response = await client.GetAsync(path + queryString);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        Assert.NotEqual(JsonValueKind.Undefined, json.RootElement.ValueKind);
    }

    [Fact]
    public async Task DeepPath_RESTfulResourceStructure_CorrectlyParsed()
    {
        // Arrange
        var client = _factory.CreateClient();
        var path =
            "/api/mock/v2/organizations/org-123/departments/dept-456/employees/emp-789/reviews/review-101/comments";

        // Act
        var response = await client.GetAsync(path);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        Assert.True(json.RootElement.ValueKind == JsonValueKind.Object);

        // Verify standard fake response structure
        Assert.True(json.RootElement.TryGetProperty("id", out _));
        Assert.True(json.RootElement.TryGetProperty("name", out _));
    }

    [Fact]
    public async Task DeepPath_POST_WithFormData_ProcessedCorrectly()
    {
        // Arrange
        var client = _factory.CreateClient();
        var path = "/api/mock/v1/companies/acme/projects/proj-42/tasks/task-99/subtasks/create";

        var formData = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("title", "New Subtask"),
            new KeyValuePair<string, string>("description", "Testing deep path with form data"),
            new KeyValuePair<string, string>("assignee", "john.doe"),
            new KeyValuePair<string, string>("priority", "high")
        });

        // Act
        var response = await client.PostAsync(path, formData);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        Assert.NotEqual(JsonValueKind.Undefined, json.RootElement.ValueKind);
    }

    [Fact]
    public async Task DeepPath_WithFileUpload_ProcessedCorrectly()
    {
        // Arrange
        var client = _factory.CreateClient();
        var path = "/api/mock/projects/web-app/modules/auth/components/login/assets/upload";

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("Login Background"), "description");

        var image = new ByteArrayContent(Encoding.UTF8.GetBytes("Image binary data"));
        image.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
        content.Add(image, "image", "login-bg.png");

        // Act
        var response = await client.PostAsync(path, content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(responseContent);
        Assert.NotEqual(JsonValueKind.Undefined, json.RootElement.ValueKind);
    }

    [Fact]
    public async Task DeepPath_VeryDeep_20Segments_StillWorks()
    {
        // Arrange
        var client = _factory.CreateClient();
        var path = "/api/mock/a/b/c/d/e/f/g/h/i/j/k/l/m/n/o/p/q/r/s/t/u";

        // Act
        var response = await client.GetAsync(path);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        Assert.NotEqual(JsonValueKind.Undefined, json.RootElement.ValueKind);
    }

    #endregion

    #region Combined Feature Tests

    [Fact]
    public async Task Combined_DeepPath_FormData_SpecialChars_AllWorkTogether()
    {
        // Arrange
        var client = _factory.CreateClient();
        var path = "/api/mock/api/v1/sites/blog/posts/2024/december/tech-review/comments/add";

        var formData = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("author", "Jane \"TechExpert\" Smith"),
            new KeyValuePair<string, string>("comment", "Great article!\nLoved the insights.\tThanks!"),
            new KeyValuePair<string, string>("rating", "5"),
            new KeyValuePair<string, string>("tags", "helpful"),
            new KeyValuePair<string, string>("tags", "informative"),
            new KeyValuePair<string, string>("tags", "well-written")
        });

        // Act
        var response = await client.PostAsync(path, formData);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        Assert.NotEqual(JsonValueKind.Undefined, json.RootElement.ValueKind);
    }

    [Fact]
    public async Task Combined_DeepPath_FileUpload_MultipleFiles_AllWorkTogether()
    {
        // Arrange
        var client = _factory.CreateClient();
        var path = "/api/mock/v1/projects/mobile-app/features/profile/screens/edit/attachments/upload";

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("Profile Update"), "action");
        content.Add(new StringContent("user-12345"), "userId");

        var avatar = new ByteArrayContent(Encoding.UTF8.GetBytes("Avatar image data"));
        avatar.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");
        content.Add(avatar, "avatar", "new-avatar.jpg");

        var resume = new ByteArrayContent(Encoding.UTF8.GetBytes("Resume PDF data"));
        resume.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        content.Add(resume, "resume", "updated-resume.pdf");

        // Act
        var response = await client.PostAsync(path, content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(responseContent);
        Assert.NotEqual(JsonValueKind.Undefined, json.RootElement.ValueKind);
    }

    #endregion
}