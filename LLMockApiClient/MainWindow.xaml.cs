using System.Windows;
using System.Windows.Controls;
using LLMockApiClient.Pages;
using LLMockApiClient.Services;

namespace LLMockApiClient;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly ApiService _apiService;

    public MainWindow()
    {
        InitializeComponent();

        // Get the base URL from the embedded web server
        var app = (App)Application.Current;
        _apiService = new ApiService(app.WebServer.BaseUrl);

        // Subscribe to activity indicator events
        app.ActivityIndicator.ActivityOccurred += OnActivityOccurred;

        Loaded += MainWindow_Loaded;
    }

    public void ShowToast(string message, Controls.ToastNotification.ToastType type = Controls.ToastNotification.ToastType.Success)
    {
        Controls.ToastNotification.Show(message, type);
    }

    private void OnActivityOccurred(object? sender, ActivityType type)
    {
        var color = type switch
        {
            ActivityType.SignalR => System.Windows.Media.Color.FromRgb(0xE7, 0x4C, 0x3C),
            ActivityType.SSE => System.Windows.Media.Color.FromRgb(0x34, 0x98, 0xDB),
            ActivityType.OpenAPI => System.Windows.Media.Color.FromRgb(0xF3, 0x9C, 0x12),
            ActivityType.Grpc => System.Windows.Media.Color.FromRgb(0x9B, 0x59, 0xB6),
            ActivityType.MockAPI => System.Windows.Media.Color.FromRgb(0x27, 0xAE, 0x60),
            ActivityType.Server => System.Windows.Media.Color.FromRgb(0x00, 0x78, 0xD4),
            _ => System.Windows.Media.Colors.Gray
        };

        var indicator = type switch
        {
            ActivityType.SignalR => SignalRIndicator,
            ActivityType.SSE => SSEIndicator,
            ActivityType.OpenAPI => OpenApiIndicator,
            ActivityType.Grpc => GrpcIndicator,
            ActivityType.MockAPI => MockApiIndicator,
            ActivityType.Server => ServerIndicator,
            _ => null
        };

        if (indicator != null)
        {
            ActivityIndicatorService.BlinkIndicator(indicator, color);
        }
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Initialize toast notification system
        Controls.ToastNotification.Initialize(ToastContainer);

        // Navigate to Dashboard by default
        ContentFrame.Navigate(new DashboardPage(_apiService));

        // Show welcome message
        ShowToast("Welcome to LLMock API Client!", Controls.ToastNotification.ToastType.Info);
    }

    private void NavigationList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Don't navigate if frame isn't ready yet (happens during initialization)
        if (ContentFrame == null)
            return;

        if (NavigationList.SelectedItem is not ListBoxItem selectedItem)
            return;

        var tag = selectedItem.Tag?.ToString();

        switch (tag)
        {
            case "Dashboard":
                ContentFrame.Navigate(new DashboardPage(_apiService));
                break;
            case "SignalR":
                ContentFrame.Navigate(new SignalRPage(_apiService));
                break;
            case "SSE":
                ContentFrame.Navigate(new SSEStreamingPage(_apiService));
                break;
            case "OpenAPI":
                ContentFrame.Navigate(new OpenApiPage(_apiService));
                break;
            case "gRPC":
                ContentFrame.Navigate(new GrpcPage(_apiService));
                break;
            case "Play":
                ContentFrame.Navigate(new PlayWithApisPage(_apiService));
                break;
            case "ServerLogs":
                ContentFrame.Navigate(new ServerLogsPage());
                break;
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog(_apiService);
        dialog.Owner = this;
        dialog.ShowDialog();
    }

    private void ThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App app)
        {
            app.ToggleTheme();

            // Update button text based on current theme
            var isDark = ModernWpf.ThemeManager.Current.ActualApplicationTheme == ModernWpf.ApplicationTheme.Dark;
            ThemeToggleButton.Content = isDark ? "🌙 Dark Mode" : "☀️ Light Mode";
        }
    }

    public void NavigateToPage(string pageName)
    {
        var tag = pageName switch
        {
            "SSE Streaming" => "SSE",
            "OpenAPI Manager" => "OpenAPI",
            "SignalR" => "SignalR",
            "Dashboard" => "Dashboard",
            "gRPC" => "gRPC",
            "Play with APIs" => "Play",
            "Server Logs" => "ServerLogs",
            _ => null
        };

        if (tag == null)
            return;

        // Find and select the corresponding navigation item
        foreach (var item in NavigationList.Items)
        {
            if (item is ListBoxItem listBoxItem && listBoxItem.Tag?.ToString() == tag)
            {
                NavigationList.SelectedItem = listBoxItem;
                break;
            }
        }
    }
}