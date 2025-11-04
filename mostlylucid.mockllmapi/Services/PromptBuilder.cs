using Microsoft.Extensions.Options;
using mostlylucid.mockllmapi.Models;

namespace mostlylucid.mockllmapi.Services;

/// <summary>
/// Builds prompts for LLM requests based on request context and configuration
/// </summary>
public class PromptBuilder(IOptions<LLMockApiOptions> options)
{
    private readonly LLMockApiOptions _options = options.Value;

    /// <summary>
    /// Builds a prompt for LLM generation
    /// </summary>
    public string BuildPrompt(string method, string fullPathWithQuery, string? body, ShapeInfo shapeInfo, bool streaming)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var randomSeed = Guid.NewGuid().ToString("N")[..8];

        string ApplyTemplate(string template)
        {
            return template
                .Replace("{method}", method)
                .Replace("{path}", fullPathWithQuery)
                .Replace("{body}", body ?? "none")
                .Replace("{randomSeed}", randomSeed)
                .Replace("{timestamp}", timestamp.ToString())
                .Replace("{shape}", shapeInfo.Shape ?? "");
        }

        // Use custom template if provided
        if (!string.IsNullOrWhiteSpace(_options.CustomPromptTemplate) && !streaming)
        {
            return ApplyTemplate(_options.CustomPromptTemplate);
        }

        if (!string.IsNullOrWhiteSpace(_options.CustomStreamingPromptTemplate) && streaming)
        {
            return ApplyTemplate(_options.CustomStreamingPromptTemplate);
        }

        // Build default prompt
        var prompt = BuildDefaultPrompt(method, fullPathWithQuery, body, randomSeed, timestamp, streaming);

        // Add shape requirement
        if (!string.IsNullOrWhiteSpace(shapeInfo.Shape))
        {
            if (shapeInfo.IsJsonSchema)
            {
                prompt += BuildJsonSchemaConstraint(shapeInfo.Shape);
            }
            else
            {
                prompt += BuildShapeConstraint(shapeInfo.Shape);
            }
        }

        return prompt;
    }

    private string BuildDefaultPrompt(string method, string fullPathWithQuery, string? body, string randomSeed, long timestamp, bool streaming)
    {
        return streaming
            ? $@"Generate realistic mock API data. Output ONLY a single VALID JSON value (object or array) — no markdown, no code fences, no comments, no explanatory text.

IMPORTANT: Be highly creative and varied. Use random values, different names, varied numbers, diverse data.
Each request should produce COMPLETELY DIFFERENT data. Random seed: {randomSeed}, timestamp: {timestamp}

Method: {method}
Path: {fullPathWithQuery}
Body: {body ?? "none"}
"
            : $@"Generate a realistic mock API response. Output ONLY a single VALID JSON value (object or array) — no markdown, no code fences, no comments, no explanatory text.

IMPORTANT: Be highly creative and varied. Use random values, different names, varied numbers, diverse data.
Each request should produce COMPLETELY DIFFERENT data. Random seed: {randomSeed}, timestamp: {timestamp}

Method: {method}
Path: {fullPathWithQuery}
Body: {body ?? "none"}
";
    }

    private string BuildShapeConstraint(string shape)
    {
        return "\nSHAPE REQUIREMENT: Your output MUST strictly conform to this JSON shape (exact properties, casing, structure).\nFill with realistic, varied sample data matching the implied types.\nKeep the structure and property types CONSISTENT across updates for this context.\nShape: " + shape + "\n";
    }

    private string BuildJsonSchemaConstraint(string jsonSchema)
    {
        return "\nJSON SCHEMA REQUIREMENT: Your output MUST be valid JSON conforming to this JSON Schema specification.\nGenerate realistic, varied sample data that validates against this schema.\nKeep the structure and property types CONSISTENT across updates for this context.\nSchema: " + jsonSchema + "\n";
    }
}
