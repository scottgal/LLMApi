using Microsoft.Extensions.Primitives;
using System.Text.Json;

namespace LLMApi.Tests;

/// <summary>
/// Tests for form body handling (application/x-www-form-urlencoded and multipart/form-data)
/// These tests verify that manual JSON construction works correctly for .NET 10 compatibility
/// </summary>
public class FormBodyHandlingTests
{
    #region Form URL-Encoded Tests

    [Fact]
    public void FormUrlEncoded_SingleValues_CreatesValidJson()
    {
        // Arrange
        var formData = new Dictionary<string, string>
        {
            ["username"] = "john_doe",
            ["email"] = "john@example.com",
            ["age"] = "30"
        };

        // Act - Simulate what ReadFormUrlEncodedAsync does
        var jsonParts = new List<string>();
        foreach (var kvp in formData)
        {
            var key = EscapeJsonString(kvp.Key);
            var value = EscapeJsonString(kvp.Value);
            jsonParts.Add($"{key}:{value}");
        }
        var result = "{" + string.Join(",", jsonParts) + "}";

        // Assert
        Assert.NotNull(result);
        var json = JsonDocument.Parse(result);
        Assert.Equal("john_doe", json.RootElement.GetProperty("username").GetString());
        Assert.Equal("john@example.com", json.RootElement.GetProperty("email").GetString());
        Assert.Equal("30", json.RootElement.GetProperty("age").GetString());
    }

    [Fact]
    public void FormUrlEncoded_MultipleValues_CreatesArray()
    {
        // Arrange
        var tags = new[] { "nature", "sunset", "beautiful" };

        // Act - Create JSON array manually
        var escapedTags = tags.Select(t => EscapeJsonString(t));
        var tagsJson = "[" + string.Join(",", escapedTags) + "]";

        var json = "{\"tags\":" + tagsJson + ",\"title\":\"My Photo\"}";

        // Assert
        var parsed = JsonDocument.Parse(json);
        Assert.Equal("My Photo", parsed.RootElement.GetProperty("title").GetString());

        var tagsElement = parsed.RootElement.GetProperty("tags");
        Assert.Equal(JsonValueKind.Array, tagsElement.ValueKind);
        Assert.Equal(3, tagsElement.GetArrayLength());
        Assert.Equal("nature", tagsElement[0].GetString());
        Assert.Equal("sunset", tagsElement[1].GetString());
        Assert.Equal("beautiful", tagsElement[2].GetString());
    }

    [Fact]
    public void FormUrlEncoded_SpecialCharacters_EscapedCorrectly()
    {
        // Arrange
        var formData = new Dictionary<string, string>
        {
            ["description"] = "Line 1\nLine 2\tTabbed",
            ["quote"] = "He said \"Hello\"",
            ["path"] = "C:\\Users\\Test"
        };

        // Act
        var jsonParts = formData.Select(kvp =>
            $"{EscapeJsonString(kvp.Key)}:{EscapeJsonString(kvp.Value)}");
        var result = "{" + string.Join(",", jsonParts) + "}";

        // Assert
        var json = JsonDocument.Parse(result);
        Assert.Equal("Line 1\nLine 2\tTabbed", json.RootElement.GetProperty("description").GetString());
        Assert.Equal("He said \"Hello\"", json.RootElement.GetProperty("quote").GetString());
        Assert.Equal("C:\\Users\\Test", json.RootElement.GetProperty("path").GetString());
    }

    #endregion

    #region Multipart Form Data Tests

    [Fact]
    public void MultipartFormData_FileMetadata_CreatesValidJson()
    {
        // Arrange
        var fileName = "test.txt";
        var contentType = "text/plain";
        var size = 12345L;
        var processed = true;
        var bytesRead = 12345L;

        // Act - Simulate what ReadMultipartFormAsync creates for file info
        var fileJson = "{" +
            $"\"fieldName\":\"upload\"," +
            $"\"fileName\":{EscapeJsonString(fileName)}," +
            $"\"contentType\":{EscapeJsonString(contentType)}," +
            $"\"size\":{size}," +
            $"\"processed\":{processed.ToString().ToLower()}," +
            $"\"actualBytesRead\":{bytesRead}" +
            "}";

        // Assert
        var json = JsonDocument.Parse(fileJson);
        Assert.Equal("upload", json.RootElement.GetProperty("fieldName").GetString());
        Assert.Equal(fileName, json.RootElement.GetProperty("fileName").GetString());
        Assert.Equal(contentType, json.RootElement.GetProperty("contentType").GetString());
        Assert.Equal(size, json.RootElement.GetProperty("size").GetInt64());
        Assert.True(json.RootElement.GetProperty("processed").GetBoolean());
        Assert.Equal(bytesRead, json.RootElement.GetProperty("actualBytesRead").GetInt64());
    }

    [Fact]
    public void MultipartFormData_WithFieldsAndFiles_CreatesValidJson()
    {
        // Arrange
        var fields = new Dictionary<string, string>
        {
            ["title"] = "My Upload",
            ["description"] = "Test file"
        };

        var file1 = "{\"fieldName\":\"image\",\"fileName\":\"photo.jpg\",\"contentType\":\"image/jpeg\",\"size\":54321,\"processed\":true,\"actualBytesRead\":54321}";
        var file2 = "{\"fieldName\":\"document\",\"fileName\":\"report.pdf\",\"contentType\":\"application/pdf\",\"size\":12345,\"processed\":true,\"actualBytesRead\":12345}";

        // Act
        var fieldsParts = fields.Select(kvp =>
            $"{EscapeJsonString(kvp.Key)}:{EscapeJsonString(kvp.Value)}");
        var fieldsJson = "{" + string.Join(",", fieldsParts) + "}";

        var filesJson = "[" + file1 + "," + file2 + "]";

        var result = $"{{\"fields\":{fieldsJson},\"files\":{filesJson}}}";

        // Assert
        var json = JsonDocument.Parse(result);

        var fieldsElement = json.RootElement.GetProperty("fields");
        Assert.Equal("My Upload", fieldsElement.GetProperty("title").GetString());
        Assert.Equal("Test file", fieldsElement.GetProperty("description").GetString());

        var filesElement = json.RootElement.GetProperty("files");
        Assert.Equal(2, filesElement.GetArrayLength());
        Assert.Equal("photo.jpg", filesElement[0].GetProperty("fileName").GetString());
        Assert.Equal("report.pdf", filesElement[1].GetProperty("fileName").GetString());
    }

    #endregion

    #region JSON Escaping Tests

    [Theory]
    [InlineData("simple", "\"simple\"")]
    [InlineData("with \"quotes\"", "\"with \\\"quotes\\\"\"")]
    [InlineData("path\\test", "\"path\\\\test\"")]
    [InlineData("line1\nline2", "\"line1\\nline2\"")]
    [InlineData("tab\there", "\"tab\\there\"")]
    [InlineData("carriage\rreturn", "\"carriage\\rreturn\"")]
    [InlineData("", "\"\"")]
    public void EscapeJsonString_VariousInputs_EscapesCorrectly(string input, string expected)
    {
        // Act
        var result = EscapeJsonString(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void EscapeJsonString_ComplexMixed_CreatesValidJson()
    {
        // Arrange
        var input = "User: \"John\"\nPath: C:\\Users\\Test\tAge: 30";

        // Act
        var escaped = EscapeJsonString(input);
        var json = $"{{\"data\":{escaped}}}";

        // Assert
        var parsed = JsonDocument.Parse(json);
        Assert.Equal(input, parsed.RootElement.GetProperty("data").GetString());
    }

    #endregion

    #region Helper Methods

    private static string EscapeJsonString(string str)
    {
        return "\"" + str
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t")
            + "\"";
    }

    #endregion
}
