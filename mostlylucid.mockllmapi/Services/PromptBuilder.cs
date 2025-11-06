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
        string? description = null,
        string? contextHistory = null)
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
                    .Replace("{description}", description ?? "")
                    .Replace("{context}", contextHistory ?? "");

        // Use custom templates if provided
        if (!string.IsNullOrWhiteSpace(_options.CustomPromptTemplate) && !streaming)
            return ApplyTemplate(_options.CustomPromptTemplate);

        if (!string.IsNullOrWhiteSpace(_options.CustomStreamingPromptTemplate) && streaming)
            return ApplyTemplate(_options.CustomStreamingPromptTemplate);

        // Build default compact prompt
        var prompt = BuildDefaultPrompt(method, fullPathWithQuery, body, randomSeed, timestamp, streaming, description, contextHistory);

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
        string? description,
        string? contextHistory)
    {
        var desc = string.IsNullOrWhiteSpace(description) ? "" : $"Desc: {description}\n";
        var context = string.IsNullOrWhiteSpace(contextHistory) ? "" : $"\n{contextHistory}\n";

        // Compact instructions for TinyLLaMA
        var baseInstr = streaming
            ? "TASK: Generate varied mock API data.\n"
            : "TASK: Generate a varied mock API response.\n";

        return $@"{baseInstr}
RULES: Output ONLY valid JSON (object or array). Arrays MUST start with [ and end with ]. No markdown, no comments, no extra text.{context}
RandomSeed: {randomSeed}, Time: {timestamp}
{desc}Method: {method}
Path: {fullPathWithQuery}
Body: {body ?? "none"}";
    }

    private string BuildShapeConstraint(string shape)
    {
        // Check if shape is an array to provide explicit array instructions
        var trimmedShape = shape.TrimStart();
        var isArrayShape = trimmedShape.StartsWith("[");

        if (isArrayShape)
        {
            return $"\nSHAPE: Strictly match this JSON array shape.\n{shape}\n\nCRITICAL FORMATTING RULES FOR ARRAYS:\n- Your FIRST character MUST be: [\n- Your LAST character MUST be: ]\n- Separate objects with commas INSIDE the array: [{{...}},{{...}},{{...}}]\n- NEVER output: {{...}},{{...}} (this is WRONG)\n- ALWAYS output: [{{...}},{{...}}] (this is CORRECT)\n";
        }

        return $"\nSHAPE: Strictly match this JSON shape. Keep property names/types consistent.\n{shape}\n";
    }

    private string BuildJsonSchemaConstraint(string jsonSchema) =>
        $"\nSCHEMA: Output must validate against this JSON Schema.\n{jsonSchema}\n";
}