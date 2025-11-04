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
}
