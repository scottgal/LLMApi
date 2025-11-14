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

app.Run();

// Expose Program as partial to support WebApplicationFactory in integration tests
public partial class Program { }
