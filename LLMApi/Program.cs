using mostlylucid.mockllmapi;

var builder = WebApplication.CreateBuilder(args);

// Add LLMock API services with configuration from appsettings.json
builder.Services.AddLLMockApi(builder.Configuration);

// Add LLMock SignalR services
builder.Services.AddLLMockSignalR(builder.Configuration);

// Add LLMock OpenAPI services
builder.Services.AddLLMockOpenApi(builder.Configuration);

// Add Razor Pages
builder.Services.AddRazorPages();

// Add Swagger/OpenAPI documentation for the demo API itself
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "LLMock API Demo",
        Version = "v2.1.0",
        Description = "Interactive demo of the LLMock API library showcasing mock endpoints, SignalR streaming, OpenAPI integration, gRPC support, comprehensive validation suite, and enhanced chunking reliability.",
        Contact = new Microsoft.OpenApi.Models.OpenApiContact
        {
            Name = "GitHub Repository",
            Url = new Uri("https://github.com/scottgallant/mostlylucid.mockllmapi")
        }
    });
});

var app = builder.Build();

// Enable Swagger middleware
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "LLMock API Demo v2.1.0");
    options.RoutePrefix = "swagger"; // Access at /swagger
    options.DocumentTitle = "LLMock API Documentation";
    options.EnableDeepLinking();
    options.DisplayRequestDuration();
});

// Use static files
app.UseStaticFiles();

// Use routing
app.UseRouting();

// Map Razor Pages
app.MapRazorPages();

// Redirect root to Dashboard
app.MapGet("/", () => Results.Redirect("/Dashboard"));

// Map LLMock API endpoints at /api/mock
app.MapLLMockApi("/api/mock", includeStreaming: true);

// Map LLMock SignalR hub and management endpoints
app.MapLLMockSignalR("/hub/mock", "/api/mock");

// Map LLMock OpenAPI endpoints
app.MapLLMockOpenApi();

// Map LLMock OpenAPI management endpoints (for dynamic spec loading)
app.MapLLMockOpenApiManagement();

// Map API Context management endpoints (for viewing/modifying context history)
app.MapLLMockApiContextManagement("/api/contexts");

// Map gRPC proto management endpoints (for uploading/managing .proto files)
app.MapLLMockGrpcManagement("/api/grpc-protos");

// Map gRPC service call endpoints (for invoking mock gRPC methods)
app.MapLLMockGrpc("/api/grpc");

// Dashboard statistics endpoint
app.MapGet("/api/dashboard/stats", (
    mostlylucid.mockllmapi.Services.DynamicHubContextManager hubManager,
    mostlylucid.mockllmapi.Services.OpenApiContextManager contextManager) =>
{
    var apiContexts = contextManager.GetAllContexts();
    var hubContexts = hubManager.GetAllContexts();

    var totalRequests = apiContexts.Sum(c => c.TotalCalls);
    var activeApiContexts = apiContexts.Count();
    var activeHubContexts = hubContexts.Count(c => c.IsActive);
    var hubConnectionsEstimate = hubContexts.Count * 2; // Rough estimate

    return Results.Json(new
    {
        timestamp = DateTime.UtcNow,
        connections = hubConnectionsEstimate,
        activeContexts = activeApiContexts,
        totalRequests = totalRequests,
        hubContexts = activeHubContexts,
        apiContexts = apiContexts.Select(c => new
        {
            name = c.Name,
            calls = c.TotalCalls,
            lastUsed = c.LastUsedAt
        }).ToList()
    });
});

// OpenAPI specification generator endpoint
app.MapPost("/api/openapi/generate", async (
    HttpRequest request,
    mostlylucid.mockllmapi.Services.LlmClient llmClient,
    mostlylucid.mockllmapi.Services.DynamicOpenApiManager openApiManager,
    ILogger<Program> logger) =>
{
    try
    {
        // Parse request body
        var requestData = await request.ReadFromJsonAsync<OpenApiGenerateRequest>();
        if (requestData == null || string.IsNullOrWhiteSpace(requestData.Description))
        {
            return Results.BadRequest(new { error = "Description is required" });
        }

        // Generate context name if not provided
        var specName = requestData.ContextName;
        if (string.IsNullOrWhiteSpace(specName))
        {
            specName = GenerateContextName(requestData.Description);
        }

        // Build prompt for LLM to generate OpenAPI spec
        var prompt = BuildOpenApiGenerationPrompt(requestData.Description, requestData.BasePath ?? "/api/v1");

        logger.LogInformation("Generating OpenAPI spec for: {Description}", requestData.Description);

        // Call LLM to generate the spec - using named parameters for clarity
        var llmResponse = await llmClient.GetCompletionAsync(
            prompt: prompt,
            maxTokens: 4000,
            request: request);

        // Parse the response as JSON
        var specJson = ExtractJsonFromResponse(llmResponse);
        if (string.IsNullOrWhiteSpace(specJson))
        {
            return Results.Json(new { error = "Failed to generate valid OpenAPI specification" }, statusCode: 500);
        }

        // Validate JSON
        try
        {
            System.Text.Json.JsonDocument.Parse(specJson);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Generated invalid JSON");
            return Results.Json(new { error = "Generated invalid JSON specification" }, statusCode: 500);
        }

        var result = new
        {
            specification = System.Text.Json.JsonDocument.Parse(specJson).RootElement,
            contextName = specName,
            endpointsCreated = false,
            uiGenerated = false,
            uiPath = (string?)null
        };

        // Optionally auto-setup the API
        if (requestData.AutoSetup)
        {
            try
            {
                // Create data URI for the spec
                var dataUri = $"data:application/json;base64,{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(specJson))}";

                // Load the spec using DynamicOpenApiManager
                var loadResult = await openApiManager.LoadSpecAsync(
                    name: specName,
                    source: dataUri,
                    basePath: requestData.BasePath,
                    contextName: specName,
                    cancellationToken: request.HttpContext.RequestAborted);

                if (loadResult.Success)
                {
                    result = result with { endpointsCreated = true };
                    logger.LogInformation("Auto-setup complete for spec: {SpecName} with {EndpointCount} endpoints",
                        specName, loadResult.EndpointCount);
                }
                else
                {
                    logger.LogWarning("Failed to auto-setup spec: {Error}", loadResult.Error);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to auto-setup endpoints for spec: {SpecName}", specName);
            }
        }

        // TODO: UI generation (experimental feature)
        if (requestData.GenerateUI)
        {
            // This would generate a simple UI for the API
            // For now, we'll leave this as a future enhancement
            logger.LogInformation("UI generation requested but not yet implemented");
        }

        return Results.Json(result);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error generating OpenAPI specification");
        return Results.Json(new { error = ex.Message }, statusCode: 500);
    }
});

// Helper methods for OpenAPI generation
static string GenerateContextName(string description)
{
    // Generate a simple context name from the description
    var words = description.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Take(3)
        .Select(w => w.ToLowerInvariant().Trim('.', ',', '!', '?'));
    return string.Join("-", words);
}

static string BuildOpenApiGenerationPrompt(string description, string basePath)
{
    return $@"You are an expert API designer. Generate a complete OpenAPI 3.0.0 specification in JSON format for the following system:

{description}

Requirements:
- Use OpenAPI 3.0.0 specification format
- Base path should be: {basePath}
- Include realistic endpoint paths, HTTP methods, request/response schemas
- Add detailed descriptions for all endpoints and schemas
- Include appropriate response codes (200, 201, 400, 404, 500, etc.)
- Define reusable schemas in components/schemas section
- Use proper data types (string, integer, boolean, array, object)
- Add examples where appropriate
- Include pagination for list endpoints (using query parameters: page, limit)
- Add filtering and sorting capabilities where relevant
- Use RESTful conventions
- Make the API comprehensive and production-ready

IMPORTANT: Return ONLY the JSON specification, no additional text or explanation. The response must be valid JSON.";
}

static string ExtractJsonFromResponse(string response)
{
    // Try to extract JSON from the response
    // LLMs sometimes wrap JSON in code blocks
    var trimmed = response.Trim();

    // Remove markdown code blocks if present
    if (trimmed.StartsWith("```"))
    {
        var lines = trimmed.Split('\n');
        var jsonLines = lines.Skip(1).TakeWhile(line => !line.Trim().Equals("```")).ToList();
        trimmed = string.Join('\n', jsonLines);
    }

    // Find the first { and last }
    var firstBrace = trimmed.IndexOf('{');
    var lastBrace = trimmed.LastIndexOf('}');

    if (firstBrace >= 0 && lastBrace > firstBrace)
    {
        return trimmed.Substring(firstBrace, lastBrace - firstBrace + 1);
    }

    return trimmed;
}

app.Run();

// Type declarations for OpenAPI generation
record OpenApiGenerateRequest(
    string Description,
    string? ContextName,
    string? BasePath,
    bool AutoSetup,
    bool GenerateUI
);

// Expose Program as partial to support WebApplicationFactory in integration tests
public partial class Program { }
