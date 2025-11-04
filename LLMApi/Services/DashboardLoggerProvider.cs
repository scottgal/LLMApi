using Microsoft.Extensions.Logging;

namespace LLMApi.Services;

/// <summary>
/// Custom logger provider that captures logs for the dashboard
/// </summary>
public class DashboardLoggerProvider : ILoggerProvider
{
    private readonly DashboardMetrics _metrics;

    public DashboardLoggerProvider(DashboardMetrics metrics)
    {
        _metrics = metrics;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new DashboardLogger(categoryName, _metrics);
    }

    public void Dispose()
    {
        // No resources to dispose
    }

    private class DashboardLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly DashboardMetrics _metrics;

        public DashboardLogger(string categoryName, DashboardMetrics metrics)
        {
            _categoryName = categoryName;
            _metrics = metrics;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var message = formatter(state, exception);
            if (exception != null)
            {
                message += $" | {exception.GetType().Name}: {exception.Message}";
            }

            _metrics.AddLog(new DashboardMetrics.LogEntry
            {
                Timestamp = DateTime.UtcNow,
                Level = logLevel.ToString(),
                Category = _categoryName,
                Message = message
            });
        }
    }
}
