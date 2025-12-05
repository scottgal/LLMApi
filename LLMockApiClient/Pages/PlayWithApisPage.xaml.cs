using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Text.Json;
using LLMockApiClient.Services;

namespace LLMockApiClient.Pages;

public class RequestHistoryItem
{
    public string Method { get; set; } = "";
    public string Path { get; set; } = "";
    public string Shape { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

public partial class PlayWithApisPage : Page
{
    private readonly ApiService _apiService;
    private readonly ObservableCollection<RequestHistoryItem> _history = new();

    public PlayWithApisPage(ApiService apiService)
    {
        InitializeComponent();
        _apiService = apiService;
        HistoryListBox.ItemsSource = _history;
    }

    private async void SendRequest_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var method = ((ComboBoxItem)MethodComboBox.SelectedItem).Content.ToString()!;
            var path = PathTextBox.Text;
            var shape = ShapeTextBox.Text;
            var body = BodyTextBox.Text;

            ResponseTextBox.Text = "Sending request...";

            var response = await _apiService.CallMockApiAsync(method, path, shape, body);

            // Add to history
            _history.Insert(0, new RequestHistoryItem
            {
                Method = method,
                Path = path,
                Shape = shape,
                Body = body,
                Timestamp = DateTime.Now
            });

            // Keep only last 20
            while (_history.Count > 20)
                _history.RemoveAt(_history.Count - 1);

            // Pretty print JSON
            try
            {
                var jsonDoc = JsonDocument.Parse(response);
                ResponseTextBox.Text = JsonSerializer.Serialize(jsonDoc, new JsonSerializerOptions { WriteIndented = true });
            }
            catch
            {
                ResponseTextBox.Text = response;
            }
        }
        catch (Exception ex)
        {
            ResponseTextBox.Text = $"Error: {ex.Message}";
        }
    }

    private void History_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HistoryListBox.SelectedItem is RequestHistoryItem item)
        {
            // Restore request from history
            PathTextBox.Text = item.Path;
            ShapeTextBox.Text = item.Shape;
            BodyTextBox.Text = item.Body;

            // Set method
            for (int i = 0; i < MethodComboBox.Items.Count; i++)
            {
                if (((ComboBoxItem)MethodComboBox.Items[i]).Content.ToString() == item.Method)
                {
                    MethodComboBox.SelectedIndex = i;
                    break;
                }
            }
        }
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        _history.Clear();
    }
}
