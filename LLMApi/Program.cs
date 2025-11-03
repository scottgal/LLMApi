using mostlylucid.mockllmapi;

var builder = WebApplication.CreateBuilder(args);

// Add LLMock API services with configuration from appsettings.json
builder.Services.AddLLMockApi(builder.Configuration);

// Add LLMock SignalR services
builder.Services.AddLLMockSignalR(builder.Configuration);

// Add Razor Pages
builder.Services.AddRazorPages();

var app = builder.Build();

// Use static files
app.UseStaticFiles();

// Use routing
app.UseRouting();

// Map Razor Pages
app.MapRazorPages();

// Map LLMock API endpoints at /api/mock
app.MapLLMockApi("/api/mock", includeStreaming: true);

// Map LLMock SignalR hub and management endpoints
app.MapLLMockSignalR("/hub/mock", "/api/mock");

app.Run();

// Expose Program as partial to support WebApplicationFactory in integration tests
public partial class Program { }
