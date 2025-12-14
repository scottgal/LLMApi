using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using LLMockApiClient.Services;

namespace LLMockApiClient.Pages;

public partial class GrpcPage : Page
{
    private readonly ApiService _apiService;

    public GrpcPage(ApiService apiService)
    {
        InitializeComponent();
        _apiService = apiService;
        Loaded += GrpcPage_Loaded;
    }

    private async void GrpcPage_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshProtosAsync();
    }

    private async Task RefreshProtosAsync()
    {
        try
        {
            var json = await _apiService.GetGrpcProtosAsync();
            var doc = JsonDocument.Parse(json);
            ProtosListBox.Items.Clear();

            if (doc.RootElement.TryGetProperty("protos", out var protos))
                foreach (var proto in protos.EnumerateArray())
                {
                    var name = proto.GetProperty("name").GetString();
                    ProtosListBox.Items.Add(name);
                }
        }
        catch
        {
        }
    }

    private async void UploadProto_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var name = ProtoNameTextBox.Text;
            var content = ProtoContentTextBox.Text;

            await _apiService.UploadProtoAsync(name, content);
            MessageBox.Show($"Proto '{name}' uploaded successfully!", "Success");
            await RefreshProtosAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}