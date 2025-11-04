using Microsoft.Extensions.Hosting;
using mostlylucid.mockllmapi.Services;

namespace LLMApi.Services;

/// <summary>
/// Background service that updates SignalR context information in dashboard metrics
/// </summary>
public class DashboardContextTracker : BackgroundService
{
    private readonly DashboardMetrics _metrics;
    private readonly DynamicHubContextManager _contextManager;

    public DashboardContextTracker(
        DashboardMetrics metrics,
        DynamicHubContextManager contextManager)
    {
        _metrics = metrics;
        _contextManager = contextManager;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var contexts = _contextManager.GetAllContexts();
                foreach (var ctx in contexts)
                {
                    _metrics.UpdateSignalRContext(
                        ctx.Name,
                        ctx.ConnectionCount,
                        ctx.IsActive);
                }

                await Task.Delay(2000, stoppingToken);
            }
            catch (Exception)
            {
                // Ignore errors during shutdown
                if (!stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
            }
        }
    }
}
