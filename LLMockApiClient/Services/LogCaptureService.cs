using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Windows;

namespace LLMockApiClient.Services;

public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public LogLevel Level { get; set; }
    public string Category { get; set; } = "";
    public string Message { get; set; } = "";
    public string? Exception { get; set; }

    public string LevelColor => Level switch
    {
        LogLevel.Critical => "#FF0000",
        LogLevel.Error => "#FF4444",
        LogLevel.Warning => "#FFA500",
        LogLevel.Information => "#00AA00",
        LogLevel.Debug => "#888888",
        LogLevel.Trace => "#666666",
        _ => "#CCCCCC"
    };

    public string FormattedMessage =>
        $"[{Timestamp:HH:mm:ss}] [{Level}] {Category}: {Message}{(Exception != null ? $"\n{Exception}" : "")}";
}

public class LogCaptureService
{
    private readonly ObservableCollection<LogEntry> _logs = new();
    private const int MaxLogEntries = 1000;

    public ObservableCollection<LogEntry> Logs => _logs;

    public void AddLog(LogLevel level, string category, string message, Exception? exception = null)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
            Category = category,
            Message = message,
            Exception = exception?.ToString()
        };

        // Must run on UI thread for ObservableCollection
        Application.Current.Dispatcher.Invoke(() =>
        {
            _logs.Insert(0, entry);

            // Keep only the most recent entries
            while (_logs.Count > MaxLogEntries)
            {
                _logs.RemoveAt(_logs.Count - 1);
            }
        });
    }

    public void Clear()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _logs.Clear();
        });
    }
}

// Custom logger provider for capturing logs
public class LogCaptureProvider : ILoggerProvider
{
    private readonly LogCaptureService _captureService;

    public LogCaptureProvider(LogCaptureService captureService)
    {
        _captureService = captureService;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new LogCaptureLogger(_captureService, categoryName);
    }

    public void Dispose() { }
}

public class LogCaptureLogger : ILogger
{
    private readonly LogCaptureService _captureService;
    private readonly string _categoryName;

    public LogCaptureLogger(LogCaptureService captureService, string categoryName)
    {
        _captureService = captureService;
        _categoryName = categoryName;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var message = formatter(state, exception);
        _captureService.AddLog(logLevel, _categoryName, message, exception);
    }
}
