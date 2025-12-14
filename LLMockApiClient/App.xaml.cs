using System.Diagnostics;
using System.Windows;
using LLMockApiClient.Services;
using ModernWpf;

namespace LLMockApiClient;

/// <summary>
///     Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public TrafficMonitor TrafficMonitor { get; } = new();
    public LogCaptureService LogCapture { get; } = new();
    public ActivityIndicatorService ActivityIndicator { get; } = new();
    public WebServerHostService WebServer { get; private set; } = null!;

    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        // Set dark mode as default
        ThemeManager.Current.ApplicationTheme = ApplicationTheme.Dark;

        // Create and start the embedded web server with log capture
        WebServer = new WebServerHostService(LogCapture);

        try
        {
            await WebServer.StartAsync();
            Debug.WriteLine($"Web server started at {WebServer.BaseUrl}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to start web server: {ex.Message}", "Server Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void Application_Exit(object sender, ExitEventArgs e)
    {
        // Stop the web server on app exit
        if (WebServer != null) await WebServer.StopAsync();
    }

    public void ToggleTheme()
    {
        var currentTheme = ThemeManager.Current.ActualApplicationTheme;
        ThemeManager.Current.ApplicationTheme =
            currentTheme == ApplicationTheme.Dark ? ApplicationTheme.Light : ApplicationTheme.Dark;
    }
}