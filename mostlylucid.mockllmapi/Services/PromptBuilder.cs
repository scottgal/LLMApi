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
            return $@"
SHAPE: You MUST strictly match this JSON array shape.
{shape}

CRITICAL SHAPE CONFORMITY RULES:
1. EXACT FIELD NAMES: Use the EXACT field names from the shape. Do NOT rename, rephrase, or use snake_case/camelCase variations.
   - If shape has ""customerId"", output MUST have ""customerId"" (NOT ""customer_id"" or ""CustomerID"")
   - If shape has ""productId"", output MUST have ""productId"" (NOT ""product_id"")
2. EXACT DATA TYPES: Match the exact data types shown in the shape.
   - If shape shows ""customerId"": 0, output MUST be a number
   - If shape shows ""name"": ""string"", output MUST be a string
3. EXACT STRUCTURE: Include ALL fields from the shape, in the same nested structure.
4. ARRAY FORMATTING:
   - Your FIRST character MUST be: [
   - Your LAST character MUST be: ]
   - Separate objects with commas INSIDE the array: [{{...}},{{...}},{{...}}]
   - NEVER output: {{...}},{{...}} (this is WRONG)
   - ALWAYS output: [{{...}},{{...}}] (this is CORRECT)
5. NO EXTRA FIELDS: Only include fields that exist in the shape.
";
        }

        return $@"
SHAPE: You MUST strictly match this JSON shape.
{shape}

CRITICAL SHAPE CONFORMITY RULES:
1. EXACT FIELD NAMES: Use the EXACT field names from the shape. Do NOT rename, rephrase, or use snake_case/camelCase variations.
   - If shape has ""customerId"", output MUST have ""customerId"" (NOT ""customer_id"" or ""CustomerID"")
   - If shape has ""productId"", output MUST have ""productId"" (NOT ""product_id"")
   - If shape has ""orderId"", output MUST have ""orderId"" (NOT ""order_id"")
2. EXACT DATA TYPES: Match the exact data types shown in the shape.
   - If shape shows ""customerId"": 0, output MUST be a number (e.g., 12345)
   - If shape shows ""name"": ""string"", output MUST be a string (e.g., ""John Doe"")
   - If shape shows ""price"": 0.0, output MUST be a decimal number (e.g., 29.99)
   - If shape shows ""quantity"": 0, output MUST be an integer (e.g., 5)
3. EXACT STRUCTURE: Include ALL fields from the shape, in the same nested structure.
4. NO EXTRA FIELDS: Only include fields that exist in the shape. Do not add fields like ""id"", ""timestamp"", ""status"", ""message"" unless they are in the shape.
";
    }

    private string BuildJsonSchemaConstraint(string jsonSchema) =>
        $"\nSCHEMA: Output must validate against this JSON Schema.\n{jsonSchema}\n";
}