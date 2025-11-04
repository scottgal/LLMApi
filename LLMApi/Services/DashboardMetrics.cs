using System.Collections.Concurrent;

namespace LLMApi.Services;

/// <summary>
/// Thread-safe metrics storage for the dashboard
/// </summary>
public class DashboardMetrics
{
    private readonly ConcurrentQueue<LogEntry> _logs = new();
    private readonly ConcurrentDictionary<string, ApiConnection> _apiConnections = new();
    private readonly ConcurrentDictionary<string, SignalRContextInfo> _signalRContexts = new();
    private readonly ConcurrentQueue<LlmRequest> _llmRequests = new();

    private const int MaxLogEntries = 50;
    private const int MaxLlmRequests = 20;

    public void AddLog(LogEntry entry)
    {
        _logs.Enqueue(entry);
        while (_logs.Count > MaxLogEntries)
        {
            _logs.TryDequeue(out _);
        }
    }

    public void AddApiConnection(string id, string method, string path)
    {
        _apiConnections[id] = new ApiConnection
        {
            Id = id,
            Method = method,
            Path = path,
            StartTime = DateTime.UtcNow
        };
    }

    public void RemoveApiConnection(string id)
    {
        _apiConnections.TryRemove(id, out _);
    }

    public void UpdateSignalRContext(string name, int connections, bool isActive)
    {
        _signalRContexts[name] = new SignalRContextInfo
        {
            Name = name,
            Connections = connections,
            IsActive = isActive,
            LastUpdate = DateTime.UtcNow
        };
    }

    public void AddLlmRequest(string prompt, string? response = null, bool isError = false)
    {
        _llmRequests.Enqueue(new LlmRequest
        {
            Timestamp = DateTime.UtcNow,
            Prompt = prompt.Length > 100 ? prompt.Substring(0, 100) + "..." : prompt,
            Response = response?.Length > 100 ? response.Substring(0, 100) + "..." : response,
            IsError = isError
        });

        while (_llmRequests.Count > MaxLlmRequests)
        {
            _llmRequests.TryDequeue(out _);
        }
    }

    public IEnumerable<LogEntry> GetLogs() => _logs.Reverse().Take(MaxLogEntries);
    public IEnumerable<ApiConnection> GetApiConnections() => _apiConnections.Values;
    public IEnumerable<SignalRContextInfo> GetSignalRContexts() => _signalRContexts.Values;
    public IEnumerable<LlmRequest> GetLlmRequests() => _llmRequests.Reverse().Take(MaxLlmRequests);

    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Level { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    public class ApiConnection
    {
        public string Id { get; set; } = string.Empty;
        public string Method { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
    }

    public class SignalRContextInfo
    {
        public string Name { get; set; } = string.Empty;
        public int Connections { get; set; }
        public bool IsActive { get; set; }
        public DateTime LastUpdate { get; set; }
    }

    public class LlmRequest
    {
        public DateTime Timestamp { get; set; }
        public string Prompt { get; set; } = string.Empty;
        public string? Response { get; set; }
        public bool IsError { get; set; }
    }
}
