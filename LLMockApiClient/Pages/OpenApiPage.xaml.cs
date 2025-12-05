using System.Windows;
using System.Windows.Controls;
using System.Text.Json;
using LLMockApiClient.Services;

namespace LLMockApiClient.Pages;

public partial class OpenApiPage : Page
{
    private readonly ApiService _apiService;

    public OpenApiPage(ApiService apiService)
    {
        InitializeComponent();
        _apiService = apiService;
        Loaded += OpenApiPage_Loaded;
    }

    private async void OpenApiPage_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshSpecsAsync();
    }

    private async Task RefreshSpecsAsync()
    {
        try
        {
            var json = await _apiService.GetOpenApiSpecsAsync();
            var doc = JsonDocument.Parse(json);
            SpecsListBox.Items.Clear();

            if (doc.RootElement.TryGetProperty("specs", out var specs))
            {
                foreach (var spec in specs.EnumerateArray())
                {
                    var name = spec.GetProperty("name").GetString();
                    var endpointCount = spec.TryGetProperty("endpointCount", out var ec) ? ec.GetInt32() : 0;
                    SpecsListBox.Items.Add($"{name} ({endpointCount} endpoints)");
                }
            }
        }
        catch { }
    }

    private async void LoadSpec_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var name = SpecNameTextBox.Text;
            var url = SpecUrlTextBox.Text;

            await _apiService.LoadOpenApiSpecAsync(name, url);
            MessageBox.Show($"Spec '{name}' loaded successfully!", "Success");
            await RefreshSpecsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
