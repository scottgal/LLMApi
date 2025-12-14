using System.Reflection;
using System.Text.Json;
using mostlylucid.mockllmapi.Services.Providers;

namespace LLMApi.Tests;

/// <summary>
///     Tests for manual JSON escaping and extraction to avoid .NET 10 reflection serialization
/// </summary>
public class JsonHandlingTests
{
    #region JSON Escaping Tests

    [Theory]
    [InlineData("simple text", "\"simple text\"")]
    [InlineData("text with \"quotes\"", "\"text with \\\"quotes\\\"\"")]
    [InlineData("path\\with\\backslashes", "\"path\\\\with\\\\backslashes\"")]
    [InlineData("line1\nline2", "\"line1\\nline2\"")]
    [InlineData("tab\there", "\"tab\\there\"")]
    [InlineData("carriage\rreturn", "\"carriage\\rreturn\"")]
    [InlineData("", "\"\"")]
    [InlineData("already \"escaped\" and \\backslashed", "\"already \\\"escaped\\\" and \\\\backslashed\"")]
    public void EscapeJsonString_VariousInputs_EscapesCorrectly(string input, string expected)
    {
        // Arrange & Act
        var result = InvokePrivateEscapeMethod(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void EscapeJsonString_ComplexString_PreservesStructure()
    {
        // Arrange
        var input = "User said: \"Hello\"\nPath: C:\\Users\\Test\tAge: 30";

        // Act
        var result = InvokePrivateEscapeMethod(input);

        // Assert
        Assert.StartsWith("\"", result);
        Assert.EndsWith("\"", result);
        Assert.Contains("\\\"Hello\\\"", result);
        Assert.Contains("C:\\\\Users\\\\Test", result);
        Assert.Contains("\\n", result);
        Assert.Contains("\\t", result);
    }

    #endregion

    #region Content Extraction Tests

    [Fact]
    public void ExtractContentFromResponse_ValidOpenAIFormat_ExtractsContent()
    {
        // Arrange
        var response = @"{
            ""id"": ""chatcmpl-123"",
            ""object"": ""chat.completion"",
            ""created"": 1677652288,
            ""choices"": [{
                ""index"": 0,
                ""message"": {
                    ""role"": ""assistant"",
                    ""content"": ""Hello! How can I help you?""
                },
                ""finish_reason"": ""stop""
            }]
        }";

        // Act
        var result = InvokePrivateExtractMethod(response);

        // Assert
        Assert.Equal("Hello! How can I help you?", result);
    }

    [Fact]
    public void ExtractContentFromResponse_JsonWithEscapes_UnescapesCorrectly()
    {
        // Arrange
        var response = @"{
            ""choices"": [{
                ""message"": {
                    ""content"": ""Line 1\nLine 2\tTabbed""
                }
            }]
        }";

        // Act
        var result = InvokePrivateExtractMethod(response);

        // Assert
        Assert.Equal("Line 1\nLine 2\tTabbed", result);
    }

    [Fact]
    public void ExtractContentFromResponse_ComplexJson_ExtractsContent()
    {
        // Arrange
        var response = @"{
            ""id"": ""test"",
            ""choices"": [{
                ""index"": 0,
                ""message"": {
                    ""role"": ""assistant"",
                    ""content"": ""{\""id\"":123,\""name\"":\""John\"",\""email\"":\""john@example.com\""}""
                }
            }]
        }";

        // Act
        var result = InvokePrivateExtractMethod(response);

        // Assert
        Assert.Contains("\"id\":123", result);
        Assert.Contains("\"name\":\"John\"", result);
        Assert.Contains("john@example.com", result);
    }

    [Fact]
    public void ExtractContentFromResponse_MultipleChoices_ExtractsFirstChoice()
    {
        // Arrange
        var response = @"{
            ""choices"": [
                {""message"": {""content"": ""First choice""}},
                {""message"": {""content"": ""Second choice""}}
            ]
        }";

        // Act
        var result = InvokePrivateExtractMethod(response);

        // Assert
        Assert.Equal("First choice", result);
    }

    [Fact]
    public void ExtractContentFromResponse_InvalidFormat_ReturnsEmptyJson()
    {
        // Arrange
        var response = @"{""invalid"": ""format""}";

        // Act
        var result = InvokePrivateExtractMethod(response);

        // Assert
        Assert.Equal("{}", result);
    }

    [Fact]
    public void ExtractContentFromResponse_EmptyContent_ReturnsEmptyString()
    {
        // Arrange
        var response = @"{
            ""choices"": [{
                ""message"": {
                    ""content"": """"
                }
            }]
        }";

        // Act
        var result = InvokePrivateExtractMethod(response);

        // Assert
        Assert.Equal("", result);
    }

    [Fact]
    public void ExtractContentFromResponse_WithQuotesAndBackslashes_UnescapesCorrectly()
    {
        // Arrange
        var response = @"{
            ""choices"": [{
                ""message"": {
                    ""content"": ""He said \""Hello\"" and used C:\\Users\\Path""
                }
            }]
        }";

        // Act
        var result = InvokePrivateExtractMethod(response);

        // Assert
        Assert.Equal("He said \"Hello\" and used C:\\Users\\Path", result);
    }

    #endregion

    #region Manual JSON Construction Tests

    [Fact]
    public void ManualJsonConstruction_FormData_CreatesValidJson()
    {
        // Arrange
        var fields = new Dictionary<string, string>
        {
            ["username"] = "john_doe",
            ["email"] = "john@example.com",
            ["bio"] = "Developer\nEnthusiast"
        };

        // Act
        var jsonParts = fields.Select(kvp =>
            $"{InvokePrivateEscapeMethod(kvp.Key)}:{InvokePrivateEscapeMethod(kvp.Value)}");
        var result = "{" + string.Join(",", jsonParts) + "}";

        // Assert
        var parsed = JsonDocument.Parse(result);
        Assert.Equal("john_doe", parsed.RootElement.GetProperty("username").GetString());
        Assert.Equal("john@example.com", parsed.RootElement.GetProperty("email").GetString());
        Assert.Equal("Developer\nEnthusiast", parsed.RootElement.GetProperty("bio").GetString());
    }

    [Fact]
    public void ManualJsonConstruction_Array_CreatesValidJson()
    {
        // Arrange
        var tags = new[] { "tag1", "tag with spaces", "tag\"with\"quotes" };

        // Act
        var escapedTags = tags.Select(t => InvokePrivateEscapeMethod(t));
        var result = "[" + string.Join(",", escapedTags) + "]";

        // Assert
        var parsed = JsonDocument.Parse(result);
        Assert.Equal(3, parsed.RootElement.GetArrayLength());
        Assert.Equal("tag1", parsed.RootElement[0].GetString());
        Assert.Equal("tag with spaces", parsed.RootElement[1].GetString());
        Assert.Equal("tag\"with\"quotes", parsed.RootElement[2].GetString());
    }

    [Fact]
    public void ManualJsonConstruction_NestedStructure_CreatesValidJson()
    {
        // Arrange
        var fileName = "document.pdf";
        var contentType = "application/pdf";
        var size = 12345L;

        // Act
        var fileJson = $"{{" +
                       $"\"fieldName\":{InvokePrivateEscapeMethod("upload")}," +
                       $"\"fileName\":{InvokePrivateEscapeMethod(fileName)}," +
                       $"\"contentType\":{InvokePrivateEscapeMethod(contentType)}," +
                       $"\"size\":{size}," +
                       $"\"processed\":true" +
                       $"}}";

        // Assert
        var parsed = JsonDocument.Parse(fileJson);
        Assert.Equal("upload", parsed.RootElement.GetProperty("fieldName").GetString());
        Assert.Equal("document.pdf", parsed.RootElement.GetProperty("fileName").GetString());
        Assert.Equal("application/pdf", parsed.RootElement.GetProperty("contentType").GetString());
        Assert.Equal(12345, parsed.RootElement.GetProperty("size").GetInt64());
        Assert.True(parsed.RootElement.GetProperty("processed").GetBoolean());
    }

    #endregion

    #region Edge Cases

    [Theory]
    [InlineData("unicode: café ñ")]
    [InlineData("emoji: 🎉 🚀 ✅")]
    [InlineData("special: @#$%^&*()")]
    [InlineData("mixed: \"Hello\\World\"\n\tEnd")]
    public void EscapeJsonString_EdgeCases_HandlesCorrectly(string input)
    {
        // Act
        var result = InvokePrivateEscapeMethod(input);

        // Assert
        Assert.StartsWith("\"", result);
        Assert.EndsWith("\"", result);

        // Should be valid JSON when used in object
        var json = $"{{\"field\":{result}}}";
        var parsed = JsonDocument.Parse(json);
        Assert.Equal(input, parsed.RootElement.GetProperty("field").GetString());
    }

    [Fact]
    public void ExtractContentFromResponse_RealWorldExample_ExtractsCorrectly()
    {
        // Arrange - Real response from Ollama
        var response = @"{
            ""id"": ""chatcmpl-789"",
            ""object"": ""chat.completion"",
            ""created"": 1699564820,
            ""model"": ""llama3"",
            ""choices"": [{
                ""index"": 0,
                ""message"": {
                    ""role"": ""assistant"",
                    ""content"": ""{\n  \""users\"": [\n    {\n      \""id\"": 34,\n      \""email\"": \""laura.gonzalez@example.com\"",\n      \""fullName\"": \""Laura Gonzalez\""\n    }\n  ]\n}""
                },
                ""finish_reason"": ""stop""
            }],
            ""usage"": {
                ""prompt_tokens"": 100,
                ""completion_tokens"": 50,
                ""total_tokens"": 150
            }
        }";

        // Act
        var result = InvokePrivateExtractMethod(response);

        // Assert
        Assert.Contains("users", result);
        Assert.Contains("laura.gonzalez@example.com", result);
        Assert.Contains("Laura Gonzalez", result);

        // Should be valid JSON
        var parsed = JsonDocument.Parse(result);
        Assert.True(parsed.RootElement.TryGetProperty("users", out _));
    }

    #endregion

    #region Helper Methods

    private static string InvokePrivateEscapeMethod(string input)
    {
        // Use reflection to access private EscapeJsonString method
        var type = typeof(OllamaProvider);
        var method = type.GetMethod("EscapeJsonString",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var result = method.Invoke(null, new object[] { input });
        return result?.ToString() ?? string.Empty;
    }

    private static string InvokePrivateExtractMethod(string input)
    {
        // Use reflection to access private ExtractContentFromResponse method
        var type = typeof(OllamaProvider);
        var method = type.GetMethod("ExtractContentFromResponse",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var result = method.Invoke(null, new object[] { input });
        return result?.ToString() ?? string.Empty;
    }

    #endregion
}