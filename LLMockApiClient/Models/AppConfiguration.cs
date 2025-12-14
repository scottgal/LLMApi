using System.Collections.ObjectModel;

namespace LLMockApiClient.Models;

public class AppConfiguration
{
    public ObservableCollection<BackendConfiguration> Backends { get; set; } = new();
    public string? SelectedBackendName { get; set; }
    public bool EnableTrafficLogging { get; set; } = true;
    public bool AutoReconnectSignalR { get; set; } = true;
}

public class BackendConfiguration
{
    public string Name { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string Provider { get; set; } = "custom"; // ollama, lmstudio, openai, custom
    public bool IsEnabled { get; set; } = true;
    public string? ApiKey { get; set; }
    public string? SelectedModel { get; set; }
    public int? ContextLength { get; set; }
    public Dictionary<string, string> CustomHeaders { get; set; } = new();
}

public class TrafficLogEntry
{
    public DateTime Timestamp { get; set; }
    public string Method { get; set; } = "";
    public string Url { get; set; } = "";
    public string? RequestBody { get; set; }
    public string? ResponseBody { get; set; }
    public int StatusCode { get; set; }
    public TimeSpan Duration { get; set; }
}