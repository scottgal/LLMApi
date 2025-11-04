using LLMApi.Services;

namespace LLMApi.Middleware;

/// <summary>
/// Middleware to track API connections and requests for the dashboard
/// </summary>
public class DashboardMiddleware
{
    private readonly RequestDelegate _next;
    private readonly DashboardMetrics _metrics;

    public DashboardMiddleware(RequestDelegate next, DashboardMetrics metrics)
    {
        _next = next;
        _metrics = metrics;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var requestId = Guid.NewGuid().ToString();
        var path = context.Request.Path.ToString();
        var method = context.Request.Method;

        // Track API connection
        _metrics.AddApiConnection(requestId, method, path);

        try
        {
            await _next(context);
        }
        finally
        {
            // Remove connection when complete
            _metrics.RemoveApiConnection(requestId);
        }
    }
}
