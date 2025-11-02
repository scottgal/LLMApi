using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Catch-all route under /api/auto/*
app.MapMethods("/api/auto/{**path}", new[] { "GET", "POST", "PUT", "DELETE" },
    async (HttpContext ctx, string path) =>
    {
        // Capture method, path, query, body
        var method = ctx.Request.Method;
        var query = ctx.Request.QueryString.Value;
        string body = "";
        if (ctx.Request.ContentLength > 0)
        {
            using var reader = new StreamReader(ctx.Request.Body);
            body = await reader.ReadToEndAsync();
        }

        // Optionally capture a JSON "shape" from query, header, or body
        string? shape = null;

        // 1) Query parameter: ?shape={...}
        if (ctx.Request.Query.TryGetValue("shape", out var shapeQuery) && shapeQuery.Count > 0)
        {
            shape = shapeQuery[0];
        }
        // 2) Header: X-Response-Shape: {...}
        else if (ctx.Request.Headers.TryGetValue("X-Response-Shape", out var shapeHeader) && shapeHeader.Count > 0)
        {
            shape = shapeHeader[0];
        }
        // 3) JSON body field: { "shape": { ... } } (without removing it from original body)
        else if (!string.IsNullOrWhiteSpace(body) &&
                 ctx.Request.ContentType != null &&
                 ctx.Request.ContentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("shape", out var shapeNode))
                {
                    shape = shapeNode.GetRawText();
                }
            }
            catch
            {
                // ignore JSON parse errors and proceed without shape
            }
        }

        // Build a prompt for the LLM
        var prompt = $@"
Simulate an API response. Only output raw JSON — no code fences, no comments, no extra text.
 Use a random seed for the data so no two instances are the same, be creative.
Method: {method}
Path: /api/auto/{path}{query}
Body: {body}
";
        if (!string.IsNullOrWhiteSpace(shape))
        {
            prompt += $@"\nA response JSON 'Shape' was provided. Your output MUST strictly conform to this shape (properties, casing, and structure). Fill values with realistic sample data that match the implied types.\nShape: {shape}\n";
        }

        // Call your LLM (Ollama, OpenAI, etc.)
        var client = new HttpClient { BaseAddress = new Uri("http://localhost:11434/v1/") };
        var request = new
        {
            model = "llama3",
            messages = new[] { new { role = "user", content = prompt } }
        };
        var response = await client.PostAsJsonAsync("chat/completions", request);
        var result = await response.Content.ReadFromJsonAsync<ChatCompletionLite>();

        // Return the simulated response
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync(result.FirstContent ?? "");
    });

app.Run();

public struct ChatCompletionLite
{
    [JsonPropertyName("choices")] public ChoiceLite[] Choices { get; set; }

    [JsonIgnore]
    public string? FirstContent => Choices != null && Choices.Length > 0 
        ? Choices[0].Message.Content 
        : null;
}

public struct ChoiceLite
{
    [JsonPropertyName("message")] public MessageLite Message { get; set; }
}

public struct MessageLite
{
    [JsonPropertyName("content")] public string Content { get; set; }
}