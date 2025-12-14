using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LLMockApiClient.Services;
using Microsoft.Win32;

namespace LLMockApiClient.Pages;

public partial class OpenApiGeneratorPage : Page
{
    private readonly ApiService _apiService;
    private string? _generatedContextName;
    private string? _generatedSpec;

    public OpenApiGeneratorPage(ApiService apiService)
    {
        InitializeComponent();
        _apiService = apiService;
    }

    private void ExampleButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string description) DescriptionTextBox.Text = description;
    }

    private async void GenerateButton_Click(object sender, RoutedEventArgs e)
    {
        var description = DescriptionTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(description))
        {
            ShowStatus("Please enter a system description", StatusType.Error);
            return;
        }

        try
        {
            // Show loading
            LoadingOverlay.Visibility = Visibility.Visible;
            GenerateButton.IsEnabled = false;

            var contextName = ContextNameTextBox.Text?.Trim();
            var basePath = BasePathTextBox.Text?.Trim();
            var autoSetup = AutoSetupCheckBox.IsChecked ?? false;
            var generateUI = GenerateUICheckBox.IsChecked ?? false;

            var requestBody = new
            {
                description,
                contextName,
                basePath,
                autoSetup,
                generateUI
            };

            using var client = new HttpClient();
            var response = await client.PostAsJsonAsync(
                $"{_apiService.BaseUrl}/api/openapi/generate",
                requestBody);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                JsonDocument? errorDoc = null;
                try
                {
                    errorDoc = JsonDocument.Parse(errorContent);
                    var errorMessage = errorDoc.RootElement.GetProperty("error").GetString();
                    ShowStatus($"Error: {errorMessage}", StatusType.Error);
                }
                catch
                {
                    ShowStatus($"Error: {response.StatusCode}", StatusType.Error);
                }
                finally
                {
                    errorDoc?.Dispose();
                }

                return;
            }

            var result = await response.Content.ReadFromJsonAsync<JsonDocument>();
            if (result == null)
            {
                ShowStatus("Failed to parse response", StatusType.Error);
                return;
            }

            // Extract the specification
            _generatedSpec = result.RootElement.GetProperty("specification").GetRawText();
            _generatedContextName = result.RootElement.GetProperty("contextName").GetString();

            var endpointsCreated = result.RootElement.GetProperty("endpointsCreated").GetBoolean();
            var uiGenerated = result.RootElement.GetProperty("uiGenerated").GetBoolean();

            // Display the spec
            PlaceholderPanel.Visibility = Visibility.Collapsed;
            OutputScrollViewer.Visibility = Visibility.Visible;
            ActionsPanel.Visibility = Visibility.Visible;

            var specDoc = JsonDocument.Parse(_generatedSpec);
            SpecViewer.SetJson(_generatedSpec);

            // Show success message
            var message = new StringBuilder("OpenAPI specification generated successfully!");
            if (endpointsCreated)
            {
                message.Append($"\n✅ API endpoints are now live at {basePath}");
                ViewInSwaggerButton.Visibility = Visibility.Visible;
            }

            if (uiGenerated) message.Append("\n✅ Demo UI generated");

            ShowStatus(message.ToString(), StatusType.Success);

            // Signal activity
            if (Application.Current is App app) app.ActivityIndicator.TriggerActivity(ActivityType.OpenAPI);

            // Show toast notification
            if (Application.Current.MainWindow is MainWindow mainWindow)
                mainWindow.ShowToast($"Generated OpenAPI spec: {_generatedContextName}");

            result.Dispose();
        }
        catch (Exception ex)
        {
            ShowStatus($"Error: {ex.Message}", StatusType.Error);
        }
        finally
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            GenerateButton.IsEnabled = true;
        }
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_generatedSpec))
            return;

        try
        {
            // Format JSON nicely
            var doc = JsonDocument.Parse(_generatedSpec);
            var formatted = JsonSerializer.Serialize(doc, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            Clipboard.SetText(formatted);
            ShowStatus("✓ Specification copied to clipboard!", StatusType.Success);

            if (Application.Current.MainWindow is MainWindow mainWindow)
                mainWindow.ShowToast("Specification copied to clipboard");

            doc.Dispose();
        }
        catch (Exception ex)
        {
            ShowStatus($"Failed to copy: {ex.Message}", StatusType.Error);
        }
    }

    private void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_generatedSpec))
            return;

        try
        {
            var dialog = new SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                FileName = $"{_generatedContextName ?? "openapi"}.json",
                DefaultExt = ".json"
            };

            if (dialog.ShowDialog() == true)
            {
                // Format JSON nicely
                var doc = JsonDocument.Parse(_generatedSpec);
                var formatted = JsonSerializer.Serialize(doc, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(dialog.FileName, formatted);
                ShowStatus($"✓ Saved to {Path.GetFileName(dialog.FileName)}", StatusType.Success);

                if (Application.Current.MainWindow is MainWindow mainWindow)
                    mainWindow.ShowToast($"Specification saved to {Path.GetFileName(dialog.FileName)}");

                doc.Dispose();
            }
        }
        catch (Exception ex)
        {
            ShowStatus($"Failed to save: {ex.Message}", StatusType.Error);
        }
    }

    private void ViewInBrowserButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var url = $"{_apiService.BaseUrl}/swagger";
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowStatus($"Failed to open browser: {ex.Message}", StatusType.Error);
        }
    }

    private void ShowStatus(string message, StatusType type)
    {
        StatusBorder.Visibility = Visibility.Visible;
        StatusText.Text = message;

        var brush = type switch
        {
            StatusType.Success => new SolidColorBrush(Color.FromRgb(0xD4, 0xED, 0xDA)),
            StatusType.Error => new SolidColorBrush(Color.FromRgb(0xF8, 0xD7, 0xDA)),
            StatusType.Info => new SolidColorBrush(Color.FromRgb(0xD1, 0xEC, 0xF1)),
            _ => new SolidColorBrush(Colors.LightGray)
        };

        StatusBorder.Background = brush;
    }

    private enum StatusType
    {
        Success,
        Error,
        Info
    }
}