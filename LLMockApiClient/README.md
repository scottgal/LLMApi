# LLMock API Client - WPF Desktop Application

A comprehensive, user-friendly Windows desktop client for the LLMock API demo application. Built with WPF and Modern WPF UI.

## Features

### 🌙 Dark Mode by Default
- Beautiful dark theme enabled by default
- **Theme Toggle**: Click the "🌙 Dark Mode" button in the sidebar to switch between dark and light modes
- Smooth theme transitions
- All pages and dialogs support both themes

### 📊 Dashboard
- Real-time connection status monitoring
- Feature status cards for all capabilities
- Quick action buttons
- System information display

### 🔄 SignalR Real-Time Streaming
- Create and manage SignalR contexts
- Subscribe to live data streams
- Real-time message display

### 📡 SSE Streaming
- Three streaming modes:
  - **LlmTokens**: Token-by-token streaming (like AI chat)
  - **CompleteObjects**: Complete JSON objects per event
  - **ArrayItems**: Array items with metadata
- Continuous streaming support

### 📋 OpenAPI Manager
- Load OpenAPI specifications from URL or raw JSON
- View all loaded specs and endpoints
- Test endpoints directly from the UI

### ⚡ gRPC Services
- Upload and manage .proto files
- View services and methods
- Test gRPC methods with JSON input

### 🎮 Play with Mock APIs
- Interactive API testing playground
- Support for all HTTP methods (GET, POST, PUT, DELETE, PATCH)
- Custom JSON shapes
- Request body editor
- Response visualization with syntax highlighting

### ⚙️ Settings & Configuration
- **Multi-Backend Support**: Configure multiple API backends
  - Local dev server
  - Ollama
  - LM Studio
  - Custom backends
- **Backend Management**:
  - Add/edit/remove backends
  - Enable/disable backends
  - Configure API keys and custom headers
- **Traffic Monitor**: Live HTTP request/response logging
  - View all API calls in real-time
  - See request/response bodies
  - Monitor response times and status codes
- **Configuration Persistence**: Settings saved to `appsettings.json`

## Getting Started

### Prerequisites
- .NET 8.0 SDK or later
- Windows 10/11
- LLMock API server running (default: http://localhost:5116)

### Running the Application

```bash
# From the LLMockApiClient directory
dotnet run
```

### Configuration

The application creates `appsettings.json` on first run with default settings:

```json
{
  "Backends": [
    {
      "Name": "Local",
      "BaseUrl": "http://localhost:5116",
      "IsEnabled": true
    }
  ],
  "EnableTrafficLogging": true,
  "AutoReconnectSignalR": true
}
```

### Configuring Backends

1. Click the **⚙️ Settings** button in the sidebar
2. Go to the **🌐 Backends** tab
3. Click **➕ Add Backend** to add a new backend
4. Configure:
   - **Name**: Friendly name for the backend
   - **BaseUrl**: Full API base URL (e.g., `http://localhost:11434`)
   - **API Key**: Optional API key for authenticated backends
   - **Enabled**: Toggle to enable/disable the backend
5. Click **💾 Save** to persist settings

### Traffic Monitoring

The built-in traffic monitor logs all HTTP requests and responses:
1. Open **⚙️ Settings**
2. Go to **📊 Traffic Monitor** tab
3. See real-time request/response data
4. View timestamps, methods, URLs, status codes, and durations
5. Click **🗑️ Clear Traffic Log** to reset

## Architecture

### Project Structure

```
LLMockApiClient/
├── Pages/               # All page components
│   ├── DashboardPage.xaml/.cs
│   ├── SignalRPage.xaml/.cs
│   ├── SSEStreamingPage.xaml/.cs
│   ├── OpenApiPage.xaml/.cs
│   ├── GrpcPage.xaml/.cs
│   └── PlayWithApisPage.xaml/.cs
├── Services/            # Business logic
│   ├── ApiService.cs    # HTTP client wrapper
│   └── TrafficMonitor.cs # Traffic logging service
├── Models/              # Data models
│   └── AppConfiguration.cs
├── MainWindow.xaml/.cs  # Main window with navigation
├── SettingsDialog.xaml/.cs # Settings dialog
└── App.xaml/.cs         # Application entry point
```

### Key Components

- **ApiService**: Centralized HTTP client for all API calls
- **TrafficMonitor**: Observable collection-based traffic logging
- **AppConfiguration**: Configuration model with backend support
- **Modern WPF UI**: Modern Windows UI framework for styling

## Recently Added Features ✨

- [x] **Model Discovery from Ollama/LM Studio** ✅
  - Automatic model detection from Ollama and LM Studio endpoints
  - Context length extraction from Ollama models
  - One-click "Refresh Models" button in Settings
  - Auto-selection of first available model per backend

- [x] **Export Traffic Logs** ✅
  - Export all HTTP traffic to CSV format
  - Includes timestamps, methods, URLs, status codes, durations
  - Full request/response body capture
  - Timestamped filenames for easy organization

- [x] **Request History & Templates** ✅
  - Last 20 requests automatically saved
  - Click any history item to restore the request
  - Quick replay functionality
  - Sidebar history view in Play with APIs page

- [x] **Dark/Light Theme Toggle** ✅
  - Dark mode enabled by default
  - One-click theme switcher in sidebar
  - Smooth transitions across all UI elements

## Future Enhancements

- [ ] GraphQL playground
- [ ] Request collections and folders
- [ ] Environment variables support
- [ ] Batch request runner
- [ ] Response diff viewer

## Technologies Used

- **.NET 8.0** - Framework
- **WPF** - UI Framework
- **Modern WPF UI** - Modern Windows styling
- **SignalR Client** - Real-time communication
- **System.Text.Json** - JSON serialization
- **CommunityToolkit.Mvvm** - MVVM patterns

## License

This project is released into the public domain under the Unlicense.

## Related Projects

- [LLMock API](../) - The main API server
- [mostlylucid.mockllmapi](../mostlylucid.mockllmapi/) - NuGet package

## Support

For issues or questions, please open an issue in the main repository.
