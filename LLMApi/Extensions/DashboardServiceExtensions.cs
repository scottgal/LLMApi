using LLMApi.Services;
using mostlylucid.mockllmapi.Services;

namespace LLMApi.Extensions;

public static class DashboardServiceExtensions
{
    public static IServiceCollection AddDashboard(this IServiceCollection services)
    {
        // Add Dashboard services
        services.AddSingleton<DashboardMetrics>();

        // Replace LlmClient with decorated version for dashboard tracking
        services.Remove(services.First(d => d.ServiceType == typeof(LlmClient)));
        services.AddScoped<LlmClient, DashboardLlmClientDecorator>();

        // Add dashboard hosted services
        services.AddHostedService<DashboardContextTracker>();
        services.AddHostedService<DashboardService>();

        return services;
    }

    public static ILoggingBuilder AddDashboardLogging(this ILoggingBuilder logging)
    {
        logging.ClearProviders();
        logging.AddConsole(); // Keep console logging

        logging.Services.AddSingleton<DashboardLoggerProvider>(sp =>
            new DashboardLoggerProvider(sp.GetRequiredService<DashboardMetrics>()));

        logging.Services.AddSingleton<Microsoft.Extensions.Logging.ILoggerProvider>(sp =>
            sp.GetRequiredService<DashboardLoggerProvider>());

        return logging;
    }
}
