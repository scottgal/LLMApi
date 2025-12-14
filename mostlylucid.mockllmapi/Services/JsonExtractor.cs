using System.Text.Json;
using System.Text.RegularExpressions;
using mostlylucid.mockllmapi.Utilities;

namespace mostlylucid.mockllmapi.Services;

/// <summary>
/// Utility for extracting clean JSON from LLM responses that might include markdown or explanatory text
/// </summary>
public static class JsonExtractor
{
    /// <summary>
    /// Extracts clean JSON from LLM response that might include markdown or explanatory text
    /// </summary>
    public static string ExtractJson(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return response ?? "{}";

        var trimmed = response.Trim();

        // Clean up common LLM artifacts that break JSON parsing
        trimmed = CleanupLlmArtifacts(trimmed);

        // Fix comma-separated objects (should be array)
        if (trimmed.StartsWith("{") && !trimmed.StartsWith("["))
        {
            trimmed = WrapCommaSeparatedObjectsInArray(trimmed);
        }

        // Check if it's already valid JSON
        if ((trimmed.StartsWith("{") && trimmed.EndsWith("}")) ||
            (trimmed.StartsWith("[") && trimmed.EndsWith("]")))
        {
            try
            {
                JsonDocument.Parse(trimmed);
                return trimmed;
            }
            catch
            {
                // If parsing fails, continue with extraction logic
            }
        }

        // Remove markdown code blocks (using cached regex for performance)
        var match = ValidationRegex.JsonMarkdownCodeBlockRegex().Match(response);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        // Try to find JSON object or array in the text using bracket matching
        // This avoids greedy regex issues where text like "here is {a} and {b}" would capture "a} and {b"
        var extractedJson = ExtractBalancedJson(response);
        if (!string.IsNullOrEmpty(extractedJson))
        {
            return extractedJson;
        }

        // Fallback to non-greedy regex patterns (using cached regex for performance)
        // Use *? for non-greedy matching - but this may not find the complete JSON
        var objectMatch = ValidationRegex.JsonObjectRegex().Match(response);
        if (objectMatch.Success)
        {
            // Validate it's actually valid JSON before returning
            try
            {
                JsonDocument.Parse(objectMatch.Value);
                return objectMatch.Value.Trim();
            }
            catch
            {
                // Not valid JSON, continue
            }
        }

        var arrayMatch = ValidationRegex.JsonArrayRegex().Match(response);
        if (arrayMatch.Success)
        {
            try
            {
                JsonDocument.Parse(arrayMatch.Value);
                return arrayMatch.Value.Trim();
            }
            catch
            {
                // Not valid JSON, continue
            }
        }

        // Return as-is if no patterns matched
        return trimmed;
    }

    /// <summary>
    /// Extracts JSON using balanced bracket matching.
    /// This is more accurate than regex for finding complete JSON structures.
    /// </summary>
    private static string? ExtractBalancedJson(string text)
    {
        // Find the first { or [ that starts a JSON structure
        var objectStart = text.IndexOf('{');
        var arrayStart = text.IndexOf('[');

        int start;
        char openBracket, closeBracket;

        if (objectStart == -1 && arrayStart == -1)
            return null;
        else if (objectStart == -1)
        {
            start = arrayStart;
            openBracket = '[';
            closeBracket = ']';
        }
        else if (arrayStart == -1)
        {
            start = objectStart;
            openBracket = '{';
            closeBracket = '}';
        }
        else
        {
            // Use whichever comes first
            if (objectStart < arrayStart)
            {
                start = objectStart;
                openBracket = '{';
                closeBracket = '}';
            }
            else
            {
                start = arrayStart;
                openBracket = '[';
                closeBracket = ']';
            }
        }

        // Now do balanced bracket matching
        var depth = 0;
        var inString = false;
        var escape = false;

        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];

            if (escape)
            {
                escape = false;
                continue;
            }

            if (c == '\\' && inString)
            {
                escape = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
                continue;

            if (c == openBracket)
            {
                depth++;
            }
            else if (c == closeBracket)
            {
                depth--;
                if (depth == 0)
                {
                    var json = text.Substring(start, i - start + 1);
                    // Validate it's actually valid JSON
                    try
                    {
                        JsonDocument.Parse(json);
                        return json;
                    }
                    catch
                    {
                        // Not valid JSON, maybe mixed brackets, continue searching
                        return null;
                    }
                }
            }
        }

        return null; // Unbalanced brackets
    }

    /// <summary>
    /// Removes common LLM artifacts that break JSON parsing:
    /// - Ellipsis (...) used to indicate truncation
    /// - C-style comments (// ...)
    /// - Trailing commas before closing brackets
    /// Uses cached regex patterns for performance.
    /// </summary>
    private static string CleanupLlmArtifacts(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return json;

        // Remove ellipsis patterns that indicate truncation (using cached regex)
        // Match: "...", ..., "field": "...", etc.
        json = ValidationRegex.LlmEllipsisQuotedRegex().Replace(json, @"""""");  // "..." -> ""
        json = ValidationRegex.LlmEllipsisAfterColonRegex().Replace(json, ": null");  // : ... -> : null
        json = ValidationRegex.LlmEllipsisBeforeCloseRegex().Replace(json, "");  // , ... before closing -> remove
        json = ValidationRegex.LlmEllipsisLineRegex().Replace(json, "");  // Entire lines with just ...

        // Remove C-style comments (// ...) using cached regex
        json = ValidationRegex.LlmCStyleCommentRegex().Replace(json, "");  // Remove // comments

        // Remove trailing commas before closing brackets (common LLM mistake)
        json = ValidationRegex.LlmTrailingCommaRegex().Replace(json, "$1");  // , } -> } and , ] -> ]

        // Remove empty array/object elements created by cleanup
        json = ValidationRegex.LlmLeadingCommaRegex().Replace(json, "[");  // [, -> [
        json = ValidationRegex.LlmConsecutiveCommaRegex().Replace(json, ",");   // ,, -> ,

        return json;
    }

    /// <summary>
    /// Wraps comma-separated JSON objects in an array
    /// Handles cases where LLM returns: {...}, {...}, {...} instead of [{...}, {...}, {...}]
    /// </summary>
    private static string WrapCommaSeparatedObjectsInArray(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return json;

        // Check if this looks like comma-separated objects
        // Pattern: starts with {, contains }, { pattern
        if (json.StartsWith("{") && json.Contains("}, {"))
        {
            // Try to parse as-is first
            try
            {
                JsonDocument.Parse(json);
                return json; // Already valid
            }
            catch (JsonException)
            {
                // Not valid, try wrapping in array
                var wrapped = $"[{json}]";
                try
                {
                    JsonDocument.Parse(wrapped);
                    return wrapped; // Wrapping fixed it
                }
                catch
                {
                    // Still invalid, return original
                    return json;
                }
            }
        }

        return json;
    }
}
