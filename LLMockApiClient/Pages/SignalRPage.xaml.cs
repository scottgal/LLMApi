using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using LLMockApiClient.Services;
using Microsoft.AspNetCore.SignalR.Client;

namespace LLMockApiClient.Pages;

public partial class SignalRPage : Page
{
    private readonly ApiService _apiService;
    private readonly ObservableCollection<SignalRContextViewModel> _contexts = new();
    private HubConnection? _connection;

    public SignalRPage(ApiService apiService)
    {
        InitializeComponent();
        _apiService = apiService;
        ContextsList.ItemsSource = _contexts;
        Loaded += SignalRPage_Loaded;
        Unloaded += SignalRPage_Unloaded;
    }

    private async void SignalRPage_Loaded(object sender, RoutedEventArgs e)
    {
        await InitializeSignalRAsync();
        await LoadExistingContextsAsync();
    }

    private async void SignalRPage_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_connection != null)
        {
            await _connection.StopAsync();
            await _connection.DisposeAsync();
        }
    }

    private async Task InitializeSignalRAsync()
    {
        try
        {
            _connection = new HubConnectionBuilder()
                .WithUrl($"{_apiService.BaseUrl}/hub/mock")
                .WithAutomaticReconnect()
                .Build();

            _connection.On<object>("DataUpdate", message =>
            {
                Dispatcher.Invoke(() =>
                {
                    var app = (App)Application.Current;
                    app.ActivityIndicator.TriggerActivity(ActivityType.SignalR);

                    var timestamp = DateTime.Now.ToString("HH:mm:ss");
                    var messageStr = message.ToString();

                    // Try to format as JSON
                    try
                    {
                        var jsonDoc = JsonDocument.Parse(messageStr!);
                        var formatted = JsonSerializer.Serialize(jsonDoc,
                            new JsonSerializerOptions { WriteIndented = true });
                        LiveDataViewer.Text += $"[{timestamp}] {formatted}\n\n";
                    }
                    catch
                    {
                        LiveDataViewer.Text += $"[{timestamp}] {messageStr}\n\n";
                    }

                    // Auto-scroll to bottom
                    LiveDataScrollViewer.ScrollToBottom();

                    // Update message count for the context
                    // Extract context name from message if possible
                    UpdateContextMessageCount(messageStr);
                });
            });

            _connection.Reconnecting += error =>
            {
                Dispatcher.Invoke(() => { LiveDataViewer.Text += "⚠️ Reconnecting...\n"; });
                return Task.CompletedTask;
            };

            _connection.Reconnected += connectionId =>
            {
                Dispatcher.Invoke(() => { LiveDataViewer.Text += "✅ Reconnected\n"; });
                return Task.CompletedTask;
            };

            await _connection.StartAsync();
            LiveDataViewer.Text = $"✅ Connected to SignalR hub at {DateTime.Now:HH:mm:ss}\n\n";
        }
        catch (Exception ex)
        {
            LiveDataViewer.Text = $"❌ Connection error: {ex.Message}\n";
        }
    }

    private async Task LoadExistingContextsAsync()
    {
        try
        {
            var response = await _apiService.SendRequestAsync("GET", "/api/signalr/contexts");
            var doc = JsonDocument.Parse(response);

            if (doc.RootElement.TryGetProperty("contexts", out var contextsArray))
                foreach (var context in contextsArray.EnumerateArray())
                {
                    var name = context.GetProperty("name").GetString() ?? "";
                    var description = context.TryGetProperty("description", out var descProp)
                        ? descProp.GetString() ?? ""
                        : "";
                    var isActive = context.TryGetProperty("isActive", out var activeProp) && activeProp.GetBoolean();

                    var vm = new SignalRContextViewModel
                    {
                        Name = name,
                        Description = description,
                        Status = isActive ? "Active" : "Stopped",
                        StatusColor = isActive ? "#27AE60" : "#95A5A6",
                        CanStart = !isActive,
                        CanStop = isActive,
                        CanSubscribe = true
                    };

                    _contexts.Add(vm);
                }
        }
        catch (Exception ex)
        {
            LiveDataViewer.Text += $"⚠️ Could not load existing contexts: {ex.Message}\n";
        }
    }

    private void UpdateContextMessageCount(string? message)
    {
        // Try to find context reference in message
        // This is a simple implementation - could be enhanced
        foreach (var context in _contexts)
            if (message?.Contains(context.Name, StringComparison.OrdinalIgnoreCase) == true)
            {
                context.MessageCount++;
                break;
            }
    }

    private async void CreateContext_Click(object sender, RoutedEventArgs e)
    {
        var app = (App)Application.Current;
        app.ActivityIndicator.TriggerActivity(ActivityType.SignalR);

        try
        {
            var name = ContextNameTextBox.Text?.Trim();
            var description = DescriptionTextBox.Text?.Trim();

            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("Please enter a context name", "Validation", MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // Check if context already exists
            if (_contexts.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show($"Context '{name}' already exists", "Duplicate", MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // Create context via API
            await _apiService.CreateContextAsync(name, description ?? "");

            // Add to list
            var vm = new SignalRContextViewModel
            {
                Name = name,
                Description = description ?? "",
                Status = "Active",
                StatusColor = "#27AE60",
                CanStart = false,
                CanStop = true,
                CanSubscribe = true
            };

            _contexts.Add(vm);

            // Auto-subscribe
            if (_connection?.State == HubConnectionState.Connected)
            {
                await _connection.InvokeAsync("SubscribeToContext", name);
                LiveDataViewer.Text += $"✅ Created and subscribed to context: {name}\n";
            }

            // Clear inputs
            ContextNameTextBox.Text = "";
            DescriptionTextBox.Text = "";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error creating context: {ex.Message}", "Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
            LiveDataViewer.Text += $"❌ Error creating context: {ex.Message}\n";
        }
    }

    private async void StartContext_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string contextName)
            return;

        var app = (App)Application.Current;
        app.ActivityIndicator.TriggerActivity(ActivityType.SignalR);

        try
        {
            await _apiService.SendRequestAsync("POST", $"/api/signalr/contexts/{contextName}/start");

            var context = _contexts.FirstOrDefault(c => c.Name == contextName);
            if (context != null)
            {
                context.Status = "Active";
                context.StatusColor = "#27AE60";
                context.CanStart = false;
                context.CanStop = true;
            }

            LiveDataViewer.Text += $"▶️ Started context: {contextName}\n";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error starting context: {ex.Message}", "Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void StopContext_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string contextName)
            return;

        var app = (App)Application.Current;
        app.ActivityIndicator.TriggerActivity(ActivityType.SignalR);

        try
        {
            await _apiService.SendRequestAsync("POST", $"/api/signalr/contexts/{contextName}/stop");

            var context = _contexts.FirstOrDefault(c => c.Name == contextName);
            if (context != null)
            {
                context.Status = "Stopped";
                context.StatusColor = "#95A5A6";
                context.CanStart = true;
                context.CanStop = false;
            }

            LiveDataViewer.Text += $"⏹️ Stopped context: {contextName}\n";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error stopping context: {ex.Message}", "Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void SubscribeContext_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string contextName)
            return;

        var app = (App)Application.Current;
        app.ActivityIndicator.TriggerActivity(ActivityType.SignalR);

        try
        {
            if (_connection?.State == HubConnectionState.Connected)
            {
                await _connection.InvokeAsync("SubscribeToContext", contextName);
                LiveDataViewer.Text += $"🔌 Subscribed to context: {contextName}\n";
            }
            else
            {
                MessageBox.Show("SignalR connection not active", "Not Connected", MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error subscribing: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ClearLiveData_Click(object sender, RoutedEventArgs e)
    {
        LiveDataViewer.Text = "";
    }
}

public class SignalRContextViewModel : INotifyPropertyChanged
{
    private bool _canStart = true;
    private bool _canStop;
    private bool _canSubscribe = true;
    private string _description = "";
    private int _messageCount;
    private string _name = "";
    private string _status = "Stopped";
    private string _statusColor = "#95A5A6";

    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            OnPropertyChanged();
        }
    }

    public string Description
    {
        get => _description;
        set
        {
            _description = value;
            OnPropertyChanged();
        }
    }

    public string Status
    {
        get => _status;
        set
        {
            _status = value;
            OnPropertyChanged();
        }
    }

    public string StatusColor
    {
        get => _statusColor;
        set
        {
            _statusColor = value;
            OnPropertyChanged();
        }
    }

    public bool CanStart
    {
        get => _canStart;
        set
        {
            _canStart = value;
            OnPropertyChanged();
        }
    }

    public bool CanStop
    {
        get => _canStop;
        set
        {
            _canStop = value;
            OnPropertyChanged();
        }
    }

    public bool CanSubscribe
    {
        get => _canSubscribe;
        set
        {
            _canSubscribe = value;
            OnPropertyChanged();
        }
    }

    public int MessageCount
    {
        get => _messageCount;
        set
        {
            _messageCount = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}