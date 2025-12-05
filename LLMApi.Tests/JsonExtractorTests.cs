using mostlylucid.mockllmapi.Services;

namespace LLMApi.Tests;

public class JsonExtractorTests
{
    #region Clean JSON Tests

    [Fact]
    public void ExtractJson_PlainJson_ReturnsUnchanged()
    {
        // Arrange
        var input = """
        {
            "id": 1,
            "name": "test"
        }
        """;

        // Act
        var result = JsonExtractor.ExtractJson(input);

        // Assert
        Assert.Contains("\"id\"", result);
        Assert.Contains("\"name\"", result);
    }

    [Fact]
    public void ExtractJson_JsonArray_ReturnsUnchanged()
    {
        // Arrange
        var input = """
        [
            {"id": 1},
            {"id": 2}
        ]
        """;

        // Act
        var result = JsonExtractor.ExtractJson(input);

        // Assert
        Assert.StartsWith("[", result.Trim());
        Assert.Contains("\"id\"", result);
    }

    #endregion

    #region Markdown Code Block Tests

    [Fact]
    public void ExtractJson_MarkdownJsonBlock_ExtractsJson()
    {
        // Arrange
        var input = """
        Here's the JSON you requested:
        ```json
        {
            "users": [
                {"id": 1, "name": "Alice"}
            ]
        }
        ```
        Hope this helps!
        """;

        // Act
        var result = JsonExtractor.ExtractJson(input);

        // Assert
        Assert.Contains("\"users\"", result);
        Assert.Contains("\"Alice\"", result);
        Assert.DoesNotContain("Here's the JSON", result);
        Assert.DoesNotContain("Hope this helps", result);
    }

    [Fact]
    public void ExtractJson_MarkdownPlainBlock_ExtractsJson()
    {
        // Arrange
        var input = """
        ```
        {
            "id": 1,
            "value": "test"
        }
        ```
        """;

        // Act
        var result = JsonExtractor.ExtractJson(input);

        // Assert
        Assert.Contains("\"id\"", result);
        Assert.Contains("\"value\"", result);
        Assert.DoesNotContain("```", result);
    }

    [Fact]
    public void ExtractJson_MultipleCodeBlocks_ExtractsFirstJson()
    {
        // Arrange
        var input = """
        First block:
        ```json
        {"id": 1}
        ```

        Second block:
        ```json
        {"id": 2}
        ```
        """;

        // Act
        var result = JsonExtractor.ExtractJson(input);

        // Assert
        Assert.Contains("\"id\"", result);
        Assert.Contains("1", result);
        // Should extract first valid JSON block
    }

    #endregion

    #region Mixed Content Tests

    [Fact]
    public void ExtractJson_TextBeforeJson_ExtractsJson()
    {
        // Arrange
        var input = """
        Here is your data:
        {
            "result": "success"
        }
        """;

        // Act
        var result = JsonExtractor.ExtractJson(input);

        // Assert
        Assert.Contains("\"result\"", result);
        Assert.Contains("\"success\"", result);
    }

    [Fact]
    public void ExtractJson_TextAfterJson_ExtractsJson()
    {
        // Arrange
        var input = """
        {
            "status": "ok"
        }
        This is some trailing text.
        """;

        // Act
        var result = JsonExtractor.ExtractJson(input);

        // Assert
        Assert.Contains("\"status\"", result);
        Assert.Contains("\"ok\"", result);
    }

    [Fact]
    public void ExtractJson_TextSurroundingJson_ExtractsJson()
    {
        // Arrange
        var input = """
        Prefix text
        {
            "data": "value"
        }
        Suffix text
        """;

        // Act
        var result = JsonExtractor.ExtractJson(input);

        // Assert
        Assert.Contains("\"data\"", result);
        Assert.Contains("\"value\"", result);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ExtractJson_EmptyString_ReturnsEmpty()
    {
        // Arrange
        var input = "";

        // Act
        var result = JsonExtractor.ExtractJson(input);

        // Assert
        Assert.True(string.IsNullOrWhiteSpace(result));
    }

    [Fact]
    public void ExtractJson_WhitespaceOnly_ReturnsEmpty()
    {
        // Arrange
        var input = "   \n\t  ";

        // Act
        var result = JsonExtractor.ExtractJson(input);

        // Assert
        Assert.True(string.IsNullOrWhiteSpace(result));
    }

    [Fact]
    public void ExtractJson_NoJson_ReturnsOriginal()
    {
        // Arrange
        var input = "This is just plain text with no JSON at all.";

        // Act
        var result = JsonExtractor.ExtractJson(input);

        // Assert
        // Should return original if no JSON found
        Assert.Equal(input, result);
    }

    [Fact]
    public void ExtractJson_NestedBraces_HandlesCorrectly()
    {
        // Arrange
        var input = """
        {
            "outer": {
                "inner": {
                    "value": "nested"
                }
            }
        }
        """;

        // Act
        var result = JsonExtractor.ExtractJson(input);

        // Assert
        Assert.Contains("\"outer\"", result);
        Assert.Contains("\"inner\"", result);
        Assert.Contains("\"nested\"", result);
    }

    [Fact]
    public void ExtractJson_StringsWithBraces_HandlesCorrectly()
    {
        // Arrange
        var input = """
        {
            "template": "Hello {name}, welcome to {place}!"
        }
        """;

        // Act
        var result = JsonExtractor.ExtractJson(input);

        // Assert
        Assert.Contains("\"template\"", result);
        Assert.Contains("{name}", result);
        Assert.Contains("{place}", result);
    }

    [Fact]
    public void ExtractJson_EscapedQuotes_HandlesCorrectly()
    {
        // Arrange
        var input = """
        {
            "message": "She said \"hello\" to me"
        }
        """;

        // Act
        var result = JsonExtractor.ExtractJson(input);

        // Assert
        Assert.Contains("\"message\"", result);
        Assert.Contains("\\\"hello\\\"", result);
    }

    #endregion

    #region Complex Real-World Scenarios

    [Fact]
    public void ExtractJson_LLMExplanationWithJson_ExtractsJson()
    {
        // Arrange
        var input = """
        I've generated the following GraphQL response for your query:

        ```json
        {
            "users": [
                {
                    "id": 1,
                    "name": "Alice Smith",
                    "email": "alice@example.com"
                },
                {
                    "id": 2,
                    "name": "Bob Jones",
                    "email": "bob@example.com"
                }
            ]
        }
        ```

        This includes realistic user data with varied names and email addresses.
        """;

        // Act
        var result = JsonExtractor.ExtractJson(input);

        // Assert
        Assert.Contains("\"users\"", result);
        Assert.Contains("\"Alice Smith\"", result);
        Assert.Contains("\"Bob Jones\"", result);
        Assert.DoesNotContain("I've generated", result);
        Assert.DoesNotContain("This includes", result);
    }

    [Fact]
    public void ExtractJson_MultilineStringsInJson_HandlesCorrectly()
    {
        // Arrange
        var input = """
        {
            "description": "Line 1\nLine 2\nLine 3",
            "id": 1
        }
        """;

        // Act
        var result = JsonExtractor.ExtractJson(input);

        // Assert
        Assert.Contains("\"description\"", result);
        Assert.Contains("\"id\"", result);
    }

    [Fact]
    public void ExtractJson_JsonWithArrays_HandlesCorrectly()
    {
        // Arrange
        var input = """
        Some prefix text
        {
            "items": [1, 2, 3, 4, 5],
            "names": ["Alice", "Bob", "Carol"],
            "nested": [
                {"id": 1},
                {"id": 2}
            ]
        }
        Some suffix text
        """;

        // Act
        var result = JsonExtractor.ExtractJson(input);

        // Assert
        Assert.Contains("\"items\"", result);
        Assert.Contains("\"names\"", result);
        Assert.Contains("\"nested\"", result);
        Assert.Contains("\"Alice\"", result);
    }

    #endregion

    #region Greedy Regex Fix Tests

    [Fact]
    public void ExtractJson_MultipleJsonObjectsInText_ExtractsFirst()
    {
        // Arrange - This was a bug where greedy regex would match from first { to last }
        // capturing everything in between as invalid JSON
        var input = "Here is the first response: {\"id\": 1} and here is another one {\"id\": 2}";

        // Act
        var result = JsonExtractor.ExtractJson(input);

        // Assert - Should extract the first complete valid JSON object
        Assert.Contains("\"id\"", result);
        // The result should be valid JSON
        var parsed = System.Text.Json.JsonDocument.Parse(result);
        Assert.NotNull(parsed);
    }

    [Fact]
    public void ExtractJson_TextWithBracesBeforeJson_ExtractsCorrectJson()
    {
        // Arrange - Text has curly braces that aren't JSON
        var input = "Use the function like {this} but the actual response is {\"data\": \"value\"}";

        // Act
        var result = JsonExtractor.ExtractJson(input);

        // Assert - Should extract the actual JSON, not get confused by {this}
        Assert.Contains("\"data\"", result);
        Assert.Contains("\"value\"", result);
    }

    [Fact]
    public void ExtractJson_BalancedBracketsWithEscapedQuotes_HandlesCorrectly()
    {
        // Arrange - Complex JSON with escaped quotes that could confuse simple parsing
        var input = """
        Prefix {
            "text": "He said \"hello\" and she said \"goodbye\"",
            "count": 5
        } suffix
        """;

        // Act
        var result = JsonExtractor.ExtractJson(input);

        // Assert
        var parsed = System.Text.Json.JsonDocument.Parse(result);
        Assert.NotNull(parsed);
        Assert.Contains("\"text\"", result);
        Assert.Contains("\\\"hello\\\"", result);
    }

    [Fact]
    public void ExtractJson_ArraysWithMultipleObjects_ExtractsComplete()
    {
        // Arrange
        var input = """
        The response is: [{"id": 1, "name": "Alice"}, {"id": 2, "name": "Bob"}]
        End of response.
        """;

        // Act
        var result = JsonExtractor.ExtractJson(input);

        // Assert - Should extract the complete array, not just first object
        var parsed = System.Text.Json.JsonDocument.Parse(result);
        Assert.Equal(System.Text.Json.JsonValueKind.Array, parsed.RootElement.ValueKind);
        Assert.Equal(2, parsed.RootElement.GetArrayLength());
    }

    #endregion
}
