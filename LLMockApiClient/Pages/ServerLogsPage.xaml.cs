using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LLMockApiClient.Services;

namespace LLMockApiClient.Pages;

public partial class ServerLogsPage : Page
{
    private readonly LogCaptureService _logCapture;
    private readonly WebServerHostService _webServer;

    public ServerLogsPage()
    {
        InitializeComponent();

        var app = (App)Application.Current;
        _logCapture = app.LogCapture;
        _webServer = app.WebServer;

        Loaded += ServerLogsPage_Loaded;
        Unloaded += ServerLogsPage_Unloaded;
    }

    private void ServerLogsPage_Loaded(object sender, RoutedEventArgs e)
    {
        // Bind to log collection
        LogsItemsControl.ItemsSource = _logCapture.Logs;

        // Subscribe to collection changes for auto-scroll
        _logCapture.Logs.CollectionChanged += Logs_CollectionChanged;

        UpdateServerStatus();
    }

    private void ServerLogsPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _logCapture.Logs.CollectionChanged -= Logs_CollectionChanged;
    }

    private void Logs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Update log count
        LogCountText.Text = _logCapture.Logs.Count.ToString();

        // Auto-scroll to top (logs are inserted at index 0)
        if (AutoScrollCheckBox.IsChecked == true && _logCapture.Logs.Count > 0) LogScrollViewer.ScrollToTop();
    }

    private void UpdateServerStatus()
    {
        if (_webServer.IsRunning)
        {
            ServerStatusText.Text = $"✅ Running at {_webServer.BaseUrl}";
            ServerStatusText.Foreground = Brushes.Green;
        }
        else
        {
            ServerStatusText.Text = "❌ Stopped";
            ServerStatusText.Foreground = Brushes.Red;
        }

        LogCountText.Text = _logCapture.Logs.Count.ToString();
    }

    private void ClearLogs_Click(object sender, RoutedEventArgs e)
    {
        _logCapture.Clear();
        LogCountText.Text = "0";
    }
}