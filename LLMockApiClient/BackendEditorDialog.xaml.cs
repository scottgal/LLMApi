using System.Collections.ObjectModel;
using System.Windows;
using LLMockApiClient.Models;
using LLMockApiClient.Services;

namespace LLMockApiClient;

public class ModelViewModel
{
    public string Name { get; set; } = "";
    public int? ContextLength { get; set; }
    public long? Size { get; set; }
    public double? SizeGB => Size.HasValue ? Math.Round(Size.Value / (1024.0 * 1024.0 * 1024.0), 2) : null;
}

public partial class BackendEditorDialog : Window
{
    private readonly BackendConfiguration _backend;
    private readonly ModelDiscoveryService _modelDiscovery;
    private readonly ObservableCollection<ModelViewModel> _availableModels = new();

    public BackendEditorDialog(BackendConfiguration? backend = null)
    {
        InitializeComponent();
        _modelDiscovery = new ModelDiscoveryService();
        _backend = backend ?? new BackendConfiguration();

        ModelComboBox.ItemsSource = _availableModels;

        LoadBackendData();
    }

    public BackendConfiguration Backend => _backend;

    private async void LoadBackendData()
    {
        NameTextBox.Text = _backend.Name;
        BaseUrlTextBox.Text = _backend.BaseUrl;
        ApiKeyPasswordBox.Password = _backend.ApiKey ?? "";
        EnabledCheckBox.IsChecked = _backend.IsEnabled;

        // Set provider
        var providerIndex = _backend.Provider?.ToLowerInvariant() switch
        {
            "ollama" => 0,
            "lmstudio" => 1,
            "openai" => 2,
            _ => 3
        };
        ProviderComboBox.SelectedIndex = providerIndex;

        // Set context length override
        if (_backend.ContextLength.HasValue)
            ContextLengthTextBox.Text = _backend.ContextLength.Value.ToString();

        // Auto-discover models if we have a URL and supported provider
        if (!string.IsNullOrEmpty(_backend.BaseUrl) &&
            (_backend.Provider == "ollama" || _backend.Provider == "lmstudio"))
        {
            await AutoDiscoverModelsAsync();
        }
        // If we have a selected model, add it to the list
        else if (!string.IsNullOrEmpty(_backend.SelectedModel))
        {
            _availableModels.Add(new ModelViewModel
            {
                Name = _backend.SelectedModel,
                ContextLength = _backend.ContextLength
            });
            ModelComboBox.SelectedIndex = 0;
        }
    }

    private async void Provider_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ProviderComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem item)
        {
            var provider = item.Tag?.ToString() ?? "custom";

            // Set default URLs based on provider
            if (string.IsNullOrEmpty(BaseUrlTextBox.Text) ||
                BaseUrlTextBox.Text.Contains("localhost"))
            {
                BaseUrlTextBox.Text = provider switch
                {
                    "ollama" => "http://localhost:11434",
                    "lmstudio" => "http://localhost:1234",
                    "openai" => "https://api.openai.com/v1",
                    _ => BaseUrlTextBox.Text
                };
            }

            // Auto-discover models for Ollama/LM Studio
            if (provider == "ollama" || provider == "lmstudio")
            {
                await AutoDiscoverModelsAsync();
            }
            else
            {
                // Update model status for non-discoverable providers
                ModelStatusText.Text = provider switch
                {
                    "openai" => "OpenAI models: gpt-4, gpt-3.5-turbo, etc. (manual entry required)",
                    _ => "Manual model entry required for custom endpoints"
                };
            }
        }
    }

    private async Task AutoDiscoverModelsAsync()
    {
        try
        {
            var baseUrl = BaseUrlTextBox.Text.Trim();
            if (string.IsNullOrEmpty(baseUrl))
            {
                ModelStatusText.Text = "Enter a Base URL to discover models";
                return;
            }

            var provider = ((System.Windows.Controls.ComboBoxItem)ProviderComboBox.SelectedItem).Tag?.ToString() ?? "custom";

            if (provider != "ollama" && provider != "lmstudio")
                return;

            DiscoverModelsButton.IsEnabled = false;
            DiscoverModelsButton.Content = "🔍 Discovering...";
            ModelStatusText.Text = $"Connecting to {baseUrl}...";

            var models = await _modelDiscovery.DiscoverModelsAsync(baseUrl, provider);

            _availableModels.Clear();

            if (models.Any())
            {
                foreach (var model in models)
                {
                    _availableModels.Add(new ModelViewModel
                    {
                        Name = model.Name,
                        ContextLength = model.ContextLength,
                        Size = model.Size
                    });
                }

                ModelComboBox.SelectedIndex = 0;
                ModelStatusText.Text = $"✅ Found {models.Count} model(s)";
            }
            else
            {
                ModelStatusText.Text = "❌ No models found. Check the URL and ensure the service is running.";
            }

            DiscoverModelsButton.IsEnabled = true;
            DiscoverModelsButton.Content = "🔍 Refresh Models";
        }
        catch (Exception ex)
        {
            ModelStatusText.Text = $"⚠️ Could not connect: {ex.Message}";
            DiscoverModelsButton.IsEnabled = true;
            DiscoverModelsButton.Content = "🔍 Refresh Models";
        }
    }

    private async void DiscoverModels_Click(object sender, RoutedEventArgs e)
    {
        await AutoDiscoverModelsAsync();
    }

    private void Model_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ModelComboBox.SelectedItem is ModelViewModel model)
        {
            // Auto-populate context length if available
            if (model.ContextLength.HasValue && string.IsNullOrEmpty(ContextLengthTextBox.Text))
            {
                ContextLengthTextBox.Text = model.ContextLength.Value.ToString();
            }
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Validate
        if (string.IsNullOrWhiteSpace(NameTextBox.Text))
        {
            MessageBox.Show("Please enter a backend name", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(BaseUrlTextBox.Text))
        {
            MessageBox.Show("Please enter a base URL", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Save values
        _backend.Name = NameTextBox.Text.Trim();
        _backend.BaseUrl = BaseUrlTextBox.Text.Trim();
        _backend.IsEnabled = EnabledCheckBox.IsChecked ?? true;

        var provider = ((System.Windows.Controls.ComboBoxItem)ProviderComboBox.SelectedItem).Tag?.ToString() ?? "custom";
        _backend.Provider = provider;

        // API Key
        if (!string.IsNullOrWhiteSpace(ApiKeyPasswordBox.Password))
            _backend.ApiKey = ApiKeyPasswordBox.Password;
        else
            _backend.ApiKey = null;

        // Selected Model
        if (ModelComboBox.SelectedItem is ModelViewModel selectedModel)
        {
            _backend.SelectedModel = selectedModel.Name;
        }

        // Context Length
        if (int.TryParse(ContextLengthTextBox.Text, out var contextLength))
        {
            _backend.ContextLength = contextLength;
        }
        else if (ModelComboBox.SelectedItem is ModelViewModel model && model.ContextLength.HasValue)
        {
            _backend.ContextLength = model.ContextLength.Value;
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
