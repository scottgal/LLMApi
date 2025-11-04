using Microsoft.Extensions.Options;
using mostlylucid.mockllmapi.Models;

namespace mostlylucid.mockllmapi.Services;

/// <summary>
/// Builds compact prompts for LLM requests (optimized for small-context models like TinyLLaMA).
/// </summary>
public class PromptBuilder(IOptions<LLMockApiOptions> options)
{
    private readonly LLMockApiOptions _options = options.Value;

    public string BuildPrompt(
        string method,
        string fullPathWithQuery,
        string? body,
        ShapeInfo shapeInfo,
        bool streaming,
        string? description = null)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var randomSeed = Guid.NewGuid().ToString("N")[..8];

        string ApplyTemplate(string template) =>
            template.Replace("{method}", method)
                    .Replace("{path}", fullPathWithQuery)
                    .Replace("{body}", body ?? "none")
                    .Replace("{randomSeed}", randomSeed)
                    .Replace("{timestamp}", timestamp.ToString())
                    .Replace("{shape}", shapeInfo.Shape ?? "")
                    .Replace("{description}", description ?? "");

        // Use custom templates if provided
        if (!string.IsNullOrWhiteSpace(_options.CustomPromptTemplate) && !streaming)
            return ApplyTemplate(_options.CustomPromptTemplate);

        if (!string.IsNullOrWhiteSpace(_options.CustomStreamingPromptTemplate) && streaming)
            return ApplyTemplate(_options.CustomStreamingPromptTemplate);

        // Build default compact prompt
        var prompt = BuildDefaultPrompt(method, fullPathWithQuery, body, randomSeed, timestamp, streaming, description);

        // Add constraints only if needed
        if (!string.IsNullOrWhiteSpace(shapeInfo.Shape))
        {
            prompt += shapeInfo.IsJsonSchema
                ? BuildJsonSchemaConstraint(shapeInfo.Shape)
                : BuildShapeConstraint(shapeInfo.Shape);
        }

        return prompt;
    }

    private string BuildDefaultPrompt(
        string method,
        string fullPathWithQuery,
        string? body,
        string randomSeed,
        long timestamp,
        bool streaming,
        string? description)
    {
        var desc = string.IsNullOrWhiteSpace(description) ? "" : $"Desc: {description}\n";

        // Compact instructions for TinyLLaMA
        var baseInstr = streaming
            ? "TASK: Generate varied mock API data.\n"
            : "TASK: Generate a varied mock API response.\n";

        return $@"{baseInstr}
RULES: Output ONLY one valid JSON object/array. No markdown, no comments, no extra text.
RandomSeed: {randomSeed}, Time: {timestamp}
{desc}Method: {method}
Path: {fullPathWithQuery}
Body: {body ?? "none"}";
    }

    private string BuildShapeConstraint(string shape) =>
        $"\nSHAPE: Strictly match this JSON shape. Keep property names/types consistent.\n{shape}\n";

    private string BuildJsonSchemaConstraint(string jsonSchema) =>
        $"\nSCHEMA: Output must validate against this JSON Schema.\n{jsonSchema}\n";
}