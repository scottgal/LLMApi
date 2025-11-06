using System.Text.Json;
using System.Text.RegularExpressions;

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

        // Remove markdown code blocks
        var jsonPattern = @"```(?:json|graphql)?\s*(\{[\s\S]*?\}|\[[\s\S]*?\])\s*```";
        var match = Regex.Match(response, jsonPattern);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        // Try to find JSON object or array in the text
        var jsonObjectPattern = @"\{[\s\S]*\}";
        var objectMatch = Regex.Match(response, jsonObjectPattern);
        if (objectMatch.Success)
        {
            return objectMatch.Value.Trim();
        }

        var jsonArrayPattern = @"\[[\s\S]*\]";
        var arrayMatch = Regex.Match(response, jsonArrayPattern);
        if (arrayMatch.Success)
        {
            return arrayMatch.Value.Trim();
        }

        // Return as-is if no patterns matched
        return trimmed;
    }

    /// <summary>
    /// Removes common LLM artifacts that break JSON parsing:
    /// - Ellipsis (...) used to indicate truncation
    /// - C-style comments (// ...)
    /// - Trailing commas before closing brackets
    /// </summary>
    private static string CleanupLlmArtifacts(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return json;

        // Remove ellipsis patterns that indicate truncation
        // Match: "...", ..., "field": "...", etc.
        json = Regex.Replace(json, @"""\.\.\.""", @"""""");  // "..." -> ""
        json = Regex.Replace(json, @":\s*\.\.\.(?=[,\}\]])", ": null");  // : ... -> : null
        json = Regex.Replace(json, @",\s*\.\.\.(?=[\}\]])", "");  // , ... before closing -> remove
        json = Regex.Replace(json, @"^\s*\.\.\..*$", "", RegexOptions.Multiline);  // Entire lines with just ...

        // Remove C-style comments (// ...)
        json = Regex.Replace(json, @"//[^\n]*", "");  // Remove // comments

        // Remove trailing commas before closing brackets (common LLM mistake)
        json = Regex.Replace(json, @",(\s*[\}\]])", "$1");  // , } -> } and , ] -> ]

        // Remove empty array/object elements created by cleanup
        json = Regex.Replace(json, @"\[\s*,\s*", "[");  // [, -> [
        json = Regex.Replace(json, @",\s*,\s*", ",");   // ,, -> ,

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
