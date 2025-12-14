using System.Collections.ObjectModel;
using System.Windows;
using LLMockApiClient.Models;

namespace LLMockApiClient.Services;

public class TrafficMonitor
{
    public ObservableCollection<TrafficLogEntry> Entries { get; } = new();
    public event EventHandler<TrafficLogEntry>? NewEntry;

    public void LogRequest(string method, string url, string? requestBody, string? responseBody, int statusCode,
        TimeSpan duration)
    {
        var entry = new TrafficLogEntry
        {
            Timestamp = DateTime.Now,
            Method = method,
            Url = url,
            RequestBody = requestBody,
            ResponseBody = responseBody,
            StatusCode = statusCode,
            Duration = duration
        };

        Application.Current.Dispatcher.Invoke(() =>
        {
            Entries.Insert(0, entry);

            // Keep only last 100 entries
            while (Entries.Count > 100)
                Entries.RemoveAt(Entries.Count - 1);
        });

        NewEntry?.Invoke(this, entry);
    }

    public void Clear()
    {
        Application.Current.Dispatcher.Invoke(() => Entries.Clear());
    }
}