using mostlylucid.mockllmapi;

var builder = WebApplication.CreateBuilder(args);
// Add LLMock API services with configuration from appsettings.json
builder.Services.AddLLMockApi(builder.Configuration);
var app = builder.Build();
// Map LLMock API endpoints at /api/mock
app.MapLLMockApi("/api/mock", includeStreaming: true);
app.Run();

// Expose Program as partial to support WebApplicationFactory in integration tests
public partial class Program { }
