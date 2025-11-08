using System.IO;
using System.Text.Json;
using System.Windows;
using LLMockApiClient.Models;
using LLMockApiClient.Services;

namespace LLMockApiClient;

public partial class SettingsDialog : Window
{
    private readonly ApiService _apiService;
    private readonly AppConfiguration _config;
    private readonly ModelDiscoveryService _modelDiscovery;

    public SettingsDialog(ApiService apiService)
    {
        InitializeComponent();
        _apiService = apiService;
        _modelDiscovery = new ModelDiscoveryService();
        _config = LoadConfiguration();

        BackendsListBox.ItemsSource = _config.Backends;
        ActiveBackendComboBox.ItemsSource = _config.Backends;

        // Select the active backend
        if (!string.IsNullOrEmpty(_config.SelectedBackendName))
        {
            var selectedBackend = _config.Backends.FirstOrDefault(b => b.Name == _config.SelectedBackendName);
            if (selectedBackend != null)
                ActiveBackendComboBox.SelectedItem = selectedBackend;
        }
        else if (_config.Backends.Any(b => b.IsEnabled))
        {
            ActiveBackendComboBox.SelectedItem = _config.Backends.First(b => b.IsEnabled);
        }

        // Get traffic monitor from App
        if (Application.Current is App app)
            TrafficDataGrid.ItemsSource = app.TrafficMonitor.Entries;

        EnableTrafficLoggingCheckBox.IsChecked = _config.EnableTrafficLogging;
        AutoReconnectCheckBox.IsChecked = _config.AutoReconnectSignalR;
    }

    private AppConfiguration LoadConfiguration()
    {
        try
        {
            var path = "appsettings.json";
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<AppConfiguration>(json) ?? CreateDefaultConfig();
            }
        }
        catch { }

        return CreateDefaultConfig();
    }

    private AppConfiguration CreateDefaultConfig()
    {
        var config = new AppConfiguration();
        config.Backends.Add(new BackendConfiguration
        {
            Name = "Local",
            BaseUrl = "http://localhost:5116",
            Provider = "custom",
            IsEnabled = true
        });
        config.Backends.Add(new BackendConfiguration
        {
            Name = "Ollama",
            BaseUrl = "http://localhost:11434",
            Provider = "ollama",
            IsEnabled = false
        });
        config.Backends.Add(new BackendConfiguration
        {
            Name = "LM Studio",
            BaseUrl = "http://localhost:1234",
            Provider = "lmstudio",
            IsEnabled = false
        });
        return config;
    }

    private void AddBackend_Click(object sender, RoutedEventArgs e)
    {
        var editor = new BackendEditorDialog();
        editor.Owner = this;

        if (editor.ShowDialog() == true)
        {
            _config.Backends.Add(editor.Backend);
            RefreshBackendLists();
        }
    }

    private void EditBackend_Click(object sender, RoutedEventArgs e)
    {
        if (BackendsListBox.SelectedItem is BackendConfiguration backend)
        {
            var editor = new BackendEditorDialog(backend);
            editor.Owner = this;

            if (editor.ShowDialog() == true)
            {
                RefreshBackendLists();
            }
        }
        else
        {
            MessageBox.Show("Please select a backend to edit", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void RefreshBackendLists()
    {
        BackendsListBox.Items.Refresh();
        ActiveBackendComboBox.Items.Refresh();
    }

    private void ActiveBackend_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ActiveBackendComboBox.SelectedItem is BackendConfiguration backend)
        {
            _config.SelectedBackendName = backend.Name;
        }
    }

    private void RemoveBackend_Click(object sender, RoutedEventArgs e)
    {
        if (BackendsListBox.SelectedItem is BackendConfiguration backend)
        {
            var result = MessageBox.Show(
                $"Remove backend '{backend.Name}'?",
                "Confirm Remove",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
                _config.Backends.Remove(backend);
        }
    }

    private async void RefreshModels_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var button = sender as System.Windows.Controls.Button;
            if (button != null)
            {
                button.IsEnabled = false;
                button.Content = "🔄 Refreshing...";
            }

            var results = new System.Text.StringBuilder();
            results.AppendLine("Model Discovery Results:\n");

            foreach (var backend in _config.Backends.Where(b => b.IsEnabled))
            {
                if (backend.Provider == "ollama" || backend.Provider == "lmstudio")
                {
                    results.AppendLine($"🔍 Scanning {backend.Name} ({backend.BaseUrl})...");
                    var models = await _modelDiscovery.DiscoverModelsAsync(backend.BaseUrl, backend.Provider);

                    if (models.Any())
                    {
                        results.AppendLine($"✅ Found {models.Count} model(s):\n");
                        foreach (var model in models)
                        {
                            var ctx = model.ContextLength.HasValue ? $" (ctx: {model.ContextLength})" : "";
                            var size = model.Size.HasValue ? $" ({model.Size / (1024 * 1024 * 1024)}GB)" : "";
                            results.AppendLine($"  • {model.Name}{ctx}{size}");

                            // Auto-select first model if none selected
                            if (string.IsNullOrEmpty(backend.SelectedModel))
                            {
                                backend.SelectedModel = model.Name;
                                backend.ContextLength = model.ContextLength;
                            }
                        }
                    }
                    else
                    {
                        results.AppendLine($"❌ No models found or connection failed\n");
                    }

                    results.AppendLine();
                }
            }

            BackendsListBox.Items.Refresh();

            MessageBox.Show(
                results.ToString(),
                "Model Discovery Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            if (button != null)
            {
                button.IsEnabled = true;
                button.Content = "🔄 Refresh Models";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error refreshing models: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportTraffic_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Application.Current is not App app || !app.TrafficMonitor.Entries.Any())
            {
                MessageBox.Show("No traffic data to export!", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                FileName = $"traffic-log-{DateTime.Now:yyyy-MM-dd-HHmmss}.csv",
                DefaultExt = ".csv"
            };

            if (saveDialog.ShowDialog() == true)
            {
                using var writer = new StreamWriter(saveDialog.FileName);

                // Write header
                writer.WriteLine("Timestamp,Method,URL,StatusCode,Duration(ms),RequestBody,ResponseBody");

                // Write data
                foreach (var entry in app.TrafficMonitor.Entries)
                {
                    var timestamp = entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
                    var requestBody = EscapeCsv(entry.RequestBody ?? "");
                    var responseBody = EscapeCsv(entry.ResponseBody ?? "");
                    var duration = entry.Duration.TotalMilliseconds;

                    writer.WriteLine($"\"{timestamp}\",\"{entry.Method}\",\"{entry.Url}\",{entry.StatusCode},{duration:F2},\"{requestBody}\",\"{responseBody}\"");
                }

                MessageBox.Show($"Traffic log exported to:\n{saveDialog.FileName}", "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error exporting traffic log: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        // Escape quotes and limit length
        value = value.Replace("\"", "\"\"");
        if (value.Length > 500)
            value = value.Substring(0, 500) + "...";

        return value;
    }

    private void ClearTraffic_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App app)
        {
            app.TrafficMonitor.Clear();
            MessageBox.Show("Traffic log cleared!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _config.EnableTrafficLogging = EnableTrafficLoggingCheckBox.IsChecked ?? true;
            _config.AutoReconnectSignalR = AutoReconnectCheckBox.IsChecked ?? true;

            var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText("appsettings.json", json);

            MessageBox.Show("Settings saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error saving settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
