using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using LLMockApiClient.Services;

namespace LLMockApiClient.Pages;

public partial class SSEStreamingPage : Page
{
    private readonly ApiService _apiService;
    private int _eventCount;
    private SseStreamService? _sseService;

    public SSEStreamingPage(ApiService apiService)
    {
        InitializeComponent();
        _apiService = apiService;
        Unloaded += SSEStreamingPage_Unloaded;
    }

    private void SSEStreamingPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _sseService?.StopStream();
        _sseService?.Dispose();
    }

    private async void StartStreaming_Click(object sender, RoutedEventArgs e)
    {
        var app = (App)Application.Current;
        app.ActivityIndicator.TriggerActivity(ActivityType.SSE);

        var path = PathTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(path)) path = "products";

        var modeItem = ModeComboBox.SelectedItem as ComboBoxItem;
        var mode = modeItem?.Tag?.ToString() ?? "LlmTokens";
        var continuous = ContinuousCheckBox.IsChecked == true;

        var endpoint = $"/api/mock/stream/{path}?mode={mode}";
        if (continuous) endpoint += "&continuous=true";

        // Reset UI
        StreamTextBox.Text = "";
        _eventCount = 0;
        EventCountText.Text = "0";
        StartButton.IsEnabled = false;
        StopButton.IsEnabled = true;

        // Create new SSE service
        _sseService?.Dispose();
        _sseService = new SseStreamService(_apiService.BaseUrl);

        // Wire up events
        _sseService.MessageReceived += OnSseMessageReceived;
        _sseService.ErrorOccurred += OnSseError;
        _sseService.StreamEnded += OnSseStreamEnded;

        // Start streaming
        StreamTextBox.Text += "🔄 Connecting to SSE stream...\n";
        StreamTextBox.Text += $"📡 Endpoint: {endpoint}\n";
        StreamTextBox.Text += $"📦 Mode: {mode}\n";
        StreamTextBox.Text += $"🔁 Continuous: {continuous}\n";
        StreamTextBox.Text += "\n--- Stream Started ---\n\n";

        try
        {
            await _sseService.StartStreamAsync(endpoint);
        }
        catch (Exception ex)
        {
            StreamTextBox.Text += $"\n❌ Error: {ex.Message}\n";
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
        }
    }

    private void OnSseMessageReceived(object? sender, string data)
    {
        Dispatcher.Invoke(() =>
        {
            var app = (App)Application.Current;
            app.ActivityIndicator.TriggerActivity(ActivityType.SSE);

            _eventCount++;
            EventCountText.Text = _eventCount.ToString();

            // Try to parse and format JSON
            try
            {
                var jsonDoc = JsonDocument.Parse(data);
                var formatted = JsonSerializer.Serialize(jsonDoc, new JsonSerializerOptions { WriteIndented = true });
                StreamTextBox.Text += $"[Event #{_eventCount}]\n{formatted}\n\n";
            }
            catch
            {
                // Not JSON, just display as-is
                StreamTextBox.Text += $"[Event #{_eventCount}]\n{data}\n\n";
            }

            // Auto-scroll to bottom
            OutputScrollViewer.ScrollToBottom();

            // Trim if getting too long (keep last 50000 characters)
            if (StreamTextBox.Text.Length > 50000)
                StreamTextBox.Text = StreamTextBox.Text.Substring(StreamTextBox.Text.Length - 50000);
        });
    }

    private void OnSseError(object? sender, string error)
    {
        Dispatcher.Invoke(() =>
        {
            StreamTextBox.Text += $"\n❌ Stream Error: {error}\n";
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
        });
    }

    private void OnSseStreamEnded(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            StreamTextBox.Text += $"\n✅ Stream Ended - Received {_eventCount} events\n";
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
        });
    }

    private void StopStreaming_Click(object sender, RoutedEventArgs e)
    {
        _sseService?.StopStream();
        StreamTextBox.Text += "\n⏹️ Stream Stopped by user\n";
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
    }

    private void ClearOutput_Click(object sender, RoutedEventArgs e)
    {
        StreamTextBox.Text = "";
        _eventCount = 0;
        EventCountText.Text = "0";
    }
}