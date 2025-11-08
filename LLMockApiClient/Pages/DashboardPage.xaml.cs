using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.AspNetCore.SignalR.Client;
using LLMockApiClient.Services;
using LLMockApiClient.Models;
using System.Collections.ObjectModel;

namespace LLMockApiClient.Pages;

public partial class DashboardPage : Page
{
    private readonly ApiService _apiService;
    private HubConnection? _hubConnection;
    private int _messageCount = 0;
    private DateTime _sessionStart = DateTime.Now;
    private readonly ObservableCollection<OpenApiEndpointItem> _openApiEndpoints = new();
    private OpenApiEndpointItem? _selectedEndpoint;

    public DashboardPage(ApiService apiService)
    {
        InitializeComponent();
        _apiService = apiService;
        OpenApiEndpointsList.ItemsSource = _openApiEndpoints;
        Loaded += DashboardPage_Loaded;
        Unloaded += DashboardPage_Unloaded;
    }

    private async void DashboardPage_Loaded(object sender, RoutedEventArgs e)
    {
        ApiBaseUrlText.Text = _apiService.BaseUrl;
        _sessionStart = DateTime.Now;

        await TestConnectionAsync();
        await InitializeSignalRAsync();

        UpdateStats();
    }

    private async void DashboardPage_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_hubConnection != null)
        {
            await _hubConnection.StopAsync();
            await _hubConnection.DisposeAsync();
        }
    }

    private async Task InitializeSignalRAsync()
    {
        try
        {
            _hubConnection = new HubConnectionBuilder()
                .WithUrl($"{_apiService.BaseUrl}/hub/mock")
                .WithAutomaticReconnect()
                .Build();

            _hubConnection.On<object>("DataUpdate", (message) =>
            {
                Dispatcher.Invoke(() =>
                {
                    // Trigger activity indicator
                    var app = (App)Application.Current;
                    app.ActivityIndicator.TriggerActivity(ActivityType.SignalR);

                    _messageCount++;
                    var timestamp = DateTime.Now.ToString("HH:mm:ss");
                    var msg = message.ToString();

                    // Truncate long messages
                    if (msg != null && msg.Length > 200)
                        msg = msg.Substring(0, 200) + "...";

                    LiveDataTextBox.Text += $"[{timestamp}] {msg}\n";

                    // Auto-scroll to bottom
                    if (LiveDataTextBox.Text.Length > 5000)
                    {
                        LiveDataTextBox.Text = LiveDataTextBox.Text.Substring(LiveDataTextBox.Text.Length - 5000);
                    }

                    UpdateStats();
                });
            });

            _hubConnection.Reconnecting += error =>
            {
                Dispatcher.Invoke(() =>
                {
                    SignalRStatusText.Text = "⚠️ Reconnecting...";
                });
                return Task.CompletedTask;
            };

            _hubConnection.Reconnected += connectionId =>
            {
                Dispatcher.Invoke(() =>
                {
                    SignalRStatusText.Text = "✅ Reconnected";
                    AddLiveMessage("🔄 SignalR reconnected");
                });
                return Task.CompletedTask;
            };

            await _hubConnection.StartAsync();
            SignalRStatusText.Text = "✅ Connected to SignalR hub";
            AddLiveMessage("🚀 Dashboard loaded - SignalR connected");
        }
        catch (Exception ex)
        {
            SignalRStatusText.Text = $"❌ SignalR connection failed: {ex.Message}";
            AddLiveMessage($"⚠️ SignalR unavailable: {ex.Message}");
        }
    }

    private void UpdateStats()
    {
        var uptime = DateTime.Now - _sessionStart;
        LiveStatsText.Text = $"Messages: {_messageCount}\nUptime: {uptime:hh\\:mm\\:ss}";
    }

    private void AddLiveMessage(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        LiveDataTextBox.Text += $"[{timestamp}] {message}\n";
        UpdateStats();
    }

    private async Task TestConnectionAsync()
    {
        try
        {
            var result = await _apiService.CallMockApiAsync("GET", "/health", null, null);
            ConnectionStatusText.Text = "✅ Connected";
            ConnectionStatusText.Foreground = System.Windows.Media.Brushes.Green;
        }
        catch
        {
            ConnectionStatusText.Text = "❌ Disconnected";
            ConnectionStatusText.Foreground = System.Windows.Media.Brushes.Red;
        }
    }


    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        await TestConnectionAsync();
        MessageBox.Show("Connection test completed!", "Test Connection", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void QuickSignalR_Click(object sender, RoutedEventArgs e)
    {
        var app = (App)Application.Current;
        app.ActivityIndicator.TriggerActivity(ActivityType.SignalR);

        var contextName = SignalRContextInput.Text?.Trim();
        if (string.IsNullOrEmpty(contextName))
        {
            contextName = "demo-stream";
            SignalRContextInput.Text = contextName;
        }

        try
        {
            // Create context first
            var createPayload = JsonSerializer.Serialize(new { name = contextName });
            await _apiService.SendRequestAsync("POST", "/api/signalr/contexts", createPayload, null);

            AddLiveMessage($"📡 Created SignalR context: {contextName}");
            SignalRQuickStatusText.Text = $"✅ Subscribed to context: {contextName}";

            // Subscribe to the context via SignalR
            if (_hubConnection?.State == Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Connected)
            {
                await _hubConnection.InvokeAsync("SubscribeToContext", contextName);
                AddLiveMessage($"🔔 Listening for updates on: {contextName}");
            }
        }
        catch (Exception ex)
        {
            SignalRQuickStatusText.Text = $"❌ Error: {ex.Message}";
            AddLiveMessage($"⚠️ SignalR error: {ex.Message}");
        }
    }

    private async void QuickSSE_Click(object sender, RoutedEventArgs e)
    {
        var app = (App)Application.Current;
        app.ActivityIndicator.TriggerActivity(ActivityType.SSE);

        var path = SSEPathInput.Text?.Trim();
        if (string.IsNullOrEmpty(path))
        {
            path = "products";
            SSEPathInput.Text = path;
        }

        var modeItem = SSEModeCombo.SelectedItem as ComboBoxItem;
        var mode = modeItem?.Content?.ToString() ?? "LlmTokens";

        try
        {
            SSEStatusText.Text = $"🔄 Starting SSE stream: {path} ({mode})";
            AddLiveMessage($"📡 SSE stream started: /{path}?mode={mode}");

            // For demo purposes, just show that it would work
            // Full SSE implementation would require background task with HttpClient streaming
            var sampleUrl = $"{_apiService.BaseUrl}/api/mock/stream/{path}?mode={mode}";
            SSEStatusText.Text = $"✅ Would stream from: {sampleUrl}";
            AddLiveMessage($"💡 Full SSE streaming available on SSE page");
        }
        catch (Exception ex)
        {
            SSEStatusText.Text = $"❌ Error: {ex.Message}";
        }
    }

    private async void QuickOpenAPI_Click(object sender, RoutedEventArgs e)
    {
        var app = (App)Application.Current;
        app.ActivityIndicator.TriggerActivity(ActivityType.OpenAPI);

        var selectedItem = OpenApiSampleCombo.SelectedItem as ComboBoxItem;
        var specUrl = selectedItem?.Tag?.ToString();

        if (string.IsNullOrEmpty(specUrl))
        {
            OpenApiStatusText.Text = "❌ No spec selected";
            return;
        }

        try
        {
            OpenApiStatusText.Text = $"📥 Loading spec from {specUrl}...";

            var payload = JsonSerializer.Serialize(new { url = specUrl });
            var response = await _apiService.SendRequestAsync("POST", "/api/openapi/load", payload, null);

            var doc = JsonDocument.Parse(response);

            // Clear previous endpoints
            _openApiEndpoints.Clear();

            // Parse and add endpoints
            if (doc.RootElement.TryGetProperty("endpoints", out var endpoints))
            {
                foreach (var endpoint in endpoints.EnumerateArray())
                {
                    var method = endpoint.GetProperty("method").GetString() ?? "GET";
                    var path = endpoint.GetProperty("path").GetString() ?? "";
                    var summary = endpoint.TryGetProperty("summary", out var summaryProp)
                        ? summaryProp.GetString()
                        : null;

                    _openApiEndpoints.Add(new OpenApiEndpointItem
                    {
                        Method = method,
                        Path = path,
                        Summary = summary
                    });
                }
            }

            OpenApiStatusText.Text = $"✅ Loaded! Found {_openApiEndpoints.Count} endpoint(s) - Select one to test";
            OpenApiEndpointsPanel.Visibility = Visibility.Visible;
            AddLiveMessage($"📋 OpenAPI spec loaded: {_openApiEndpoints.Count} endpoints available for testing");
        }
        catch (Exception ex)
        {
            OpenApiStatusText.Text = $"❌ Error: {ex.Message}";
            OpenApiEndpointsPanel.Visibility = Visibility.Collapsed;
            AddLiveMessage($"⚠️ OpenAPI load failed: {ex.Message}");
        }
    }

    private void OpenApiEndpoint_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedEndpoint = OpenApiEndpointsList.SelectedItem as OpenApiEndpointItem;

        if (_selectedEndpoint != null)
        {
            SelectedEndpointText.Text = $"Selected: {_selectedEndpoint.Method} {_selectedEndpoint.Path}";
            TestEndpointButton.IsEnabled = true;
        }
        else
        {
            SelectedEndpointText.Text = "";
            TestEndpointButton.IsEnabled = false;
        }
    }

    private async void TestOpenApiEndpoint_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedEndpoint == null) return;

        var app = (App)Application.Current;
        app.ActivityIndicator.TriggerActivity(ActivityType.OpenAPI);

        try
        {
            OpenApiResultViewer.Text = $"Testing {_selectedEndpoint.Method} {_selectedEndpoint.Path}...";
            OpenApiResultViewer.Visibility = Visibility.Visible;

            var fullPath = $"/api/openapi{_selectedEndpoint.Path}";
            var response = await _apiService.SendRequestAsync(_selectedEndpoint.Method, fullPath, null, null);

            OpenApiResultViewer.SetJson(response);
            AddLiveMessage($"📋 OpenAPI test: {_selectedEndpoint.Method} {_selectedEndpoint.Path} → Success");
        }
        catch (Exception ex)
        {
            OpenApiResultViewer.Text = $"Error:\n{ex.Message}";
            AddLiveMessage($"⚠️ OpenAPI test failed: {ex.Message}");
        }
    }

    private async void QuickApiTest_Click(object sender, RoutedEventArgs e)
    {
        var app = (App)Application.Current;
        app.ActivityIndicator.TriggerActivity(ActivityType.MockAPI);

        var methodItem = QuickMethodCombo.SelectedItem as ComboBoxItem;
        var method = methodItem?.Content?.ToString() ?? "GET";
        var path = QuickPathInput.Text?.Trim();

        if (string.IsNullOrEmpty(path))
        {
            path = "/users";
            QuickPathInput.Text = path;
        }

        try
        {
            QuickApiResultViewer.Text = $"Sending {method} {path}...";

            var response = await _apiService.CallMockApiAsync(method, $"/api/mock{path}", null, null);

            // Use JsonViewer to display with syntax highlighting
            QuickApiResultViewer.SetJson(response);
            AddLiveMessage($"🚀 API test: {method} {path} → Success");
        }
        catch (Exception ex)
        {
            QuickApiResultViewer.Text = $"Error:\n{ex.Message}";
            AddLiveMessage($"⚠️ API test failed: {ex.Message}");
        }
    }

    private void ClearLiveData_Click(object sender, RoutedEventArgs e)
    {
        LiveDataTextBox.Text = "";
        _messageCount = 0;
        _sessionStart = DateTime.Now;
        UpdateStats();
        AddLiveMessage("🗑️ Log cleared");
    }

    private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
            e.Handled = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open link: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void NavigateToSSEPage(object sender, RoutedEventArgs e)
    {
        var mainWindow = Application.Current.MainWindow as MainWindow;
        mainWindow?.NavigateToPage("SSE Streaming");
    }

    private void NavigateToOpenAPIPage(object sender, RoutedEventArgs e)
    {
        var mainWindow = Application.Current.MainWindow as MainWindow;
        mainWindow?.NavigateToPage("OpenAPI Manager");
    }
}
