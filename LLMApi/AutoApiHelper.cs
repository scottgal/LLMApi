using System.Text.Json;

namespace LLMApi;

public static class AutoApiHelper
{
    public static async Task<string> ReadBodyAsync(HttpRequest req)
    {
        if (req.ContentLength is > 0)
        {
            using var reader = new StreamReader(req.Body);
            return await reader.ReadToEndAsync();
        }
        return string.Empty;
    }

    public static string? ExtractShape(HttpRequest req, string? body)
    {
        // 1) Query parameter
        if (req.Query.TryGetValue("shape", out var shapeQuery) && shapeQuery.Count > 0)
        {
            return shapeQuery[0];
        }
        // 2) Header
        if (req.Headers.TryGetValue("X-Response-Shape", out var shapeHeader) && shapeHeader.Count > 0)
        {
            return shapeHeader[0];
        }
        // 3) Body property
        if (!string.IsNullOrWhiteSpace(body) &&
            req.ContentType != null &&
            req.ContentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("shape", out var shapeNode))
                {
                    return shapeNode.GetRawText();
                }
            }
            catch
            {
                // ignore JSON parse errors
            }
        }
        return null;
    }

    public static string BuildPrompt(string method, string fullPathWithQuery, string? body, string? shape)
    {
        var prompt = $@"
Simulate an API response. Only output raw JSON — no code fences, no comments, no extra text.
 Use a random seed for the data so no two instances are the same, be creative.
Method: {method}
Path: {fullPathWithQuery}
Body: {body}
";
        if (!string.IsNullOrWhiteSpace(shape))
        {
            prompt += "\\nA response JSON 'Shape' was provided. Your output MUST strictly conform to this shape (properties, casing, and structure). Fill values with realistic sample data that match the implied types.\\nShape: " + shape + "\\n";
        }
        return prompt;
    }

    public static object BuildChatRequest(string model, string prompt, bool stream)
    {
        return new
        {
            model,
            stream,
            messages = new[] { new { role = "user", content = prompt } }
        };
    }
}