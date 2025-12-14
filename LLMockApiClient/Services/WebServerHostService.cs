using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using mostlylucid.mockllmapi;

namespace LLMockApiClient.Services;

public class WebServerHostService
{
    private readonly LogCaptureService? _logCapture;
    private WebApplication? _app;
    private Task? _runTask;

    public WebServerHostService(LogCaptureService? logCapture = null, string baseUrl = "http://localhost:5116")
    {
        BaseUrl = baseUrl;
        _logCapture = logCapture;
    }

    public string BaseUrl { get; }

    public bool IsRunning => _app != null && _runTask != null && !_runTask.IsCompleted;

    public async Task StartAsync()
    {
        if (IsRunning)
            return;

        // Build the web application using LLMApi's configuration
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = Array.Empty<string>(),
            ContentRootPath = AppDomain.CurrentDomain.BaseDirectory
        });

        // Configure to listen on the specified URL
        builder.WebHost.UseUrls(BaseUrl);

        // Configure logging with capture
        if (_logCapture != null)
        {
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(new LogCaptureProvider(_logCapture));
            builder.Logging.SetMinimumLevel(LogLevel.Information);
        }

        // Add LLMock API services
        builder.Services.AddLLMockApi(builder.Configuration);
        builder.Services.AddLLMockSignalR(builder.Configuration);
        builder.Services.AddLLMockOpenApi(builder.Configuration);

        // Add Swagger/OpenAPI documentation
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        _app = builder.Build();

        // Enable Swagger middleware
        _app.UseSwagger();
        _app.UseSwaggerUI();

        // Use static files and routing
        _app.UseStaticFiles();
        _app.UseRouting();

        // Map LLMock API endpoints
        _app.MapLLMockApi();
        _app.MapLLMockSignalR();
        _app.MapLLMockOpenApi();
        _app.MapLLMockOpenApiManagement();
        _app.MapLLMockApiContextManagement();
        _app.MapLLMockGrpcManagement();
        _app.MapLLMockGrpc();

        // Start the web server on a background thread
        _runTask = Task.Run(async () =>
        {
            try
            {
                await _app.RunAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Web server error: {ex.Message}");
            }
        });

        // Give it a moment to start
        await Task.Delay(1000);
    }

    public async Task StopAsync()
    {
        if (_app != null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
            _app = null;
        }

        if (_runTask != null)
        {
            try
            {
                await _runTask;
            }
            catch
            {
                // Ignore errors during shutdown
            }

            _runTask = null;
        }
    }
}