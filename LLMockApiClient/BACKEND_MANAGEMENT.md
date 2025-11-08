# Backend Management System

## Overview

The LLMock API Client now features a comprehensive backend management system that allows you to:
- Configure multiple API backends (Ollama, LM Studio, OpenAI, custom)
- Automatically discover available models
- Select and switch between backends
- Configure models with context lengths

## Features

### 1. 🎯 Active Backend Selection

**Location**: Settings → Backends tab → Top section

The **Active Backend** dropdown shows all configured backends with:
- Backend name
- Provider type
- Selected model (if any)

**Example**:
```
Local - custom (no model)
Ollama - ollama llama3.2:latest
LM Studio - lmstudio phi-2
```

Simply select a backend from the dropdown to make it active. This backend will be used for all API calls.

### 2. 🔧 Backend Editor Dialog

**Access**: Click "➕ Add Backend" or "✏️ Edit" button

The Backend Editor provides a comprehensive configuration interface:

#### Fields:

**Backend Name** (Required)
- Friendly name for the backend
- Examples: "My Ollama Server", "Production API", "Dev LM Studio"

**Provider Type** (Required)
- **Ollama (Local LLM)**: Local Ollama instance
- **LM Studio (Local LLM)**: Local LM Studio server
- **OpenAI (Cloud API)**: OpenAI cloud service
- **Custom HTTP Endpoint**: Any other HTTP API

**Base URL** (Required)
- Full API endpoint URL
- Auto-populated based on provider:
  - Ollama: `http://localhost:11434`
  - LM Studio: `http://localhost:1234`
  - OpenAI: `https://api.openai.com/v1`

**API Key** (Optional)
- Required for OpenAI
- Optional for Ollama/LM Studio
- Hidden password field for security

**Model Selection** (Automatic)
- Dropdown populated automatically for Ollama/LM Studio
- Shows:
  - Model name
  - Context length
  - Model size in GB

**Context Length Override** (Optional)
- Manually override the detected context length
- Useful for custom configurations

**Enabled Checkbox**
- Toggle to enable/disable the backend
- Disabled backends are not shown in active backend selector

### 3. 🔍 Automatic Model Discovery

**How it works**:

1. **Select Provider**: Choose "Ollama" or "LM Studio"
2. **Auto-Discovery Triggers**: Models are automatically fetched when:
   - Provider is selected
   - Base URL is entered
   - Dialog is opened for editing an existing backend

3. **No Button Click Required**: The system automatically connects and fetches models

4. **Manual Refresh**: Click "🔍 Refresh Models" to re-fetch if needed

**Discovery Process**:
```
Provider: Ollama selected
Base URL: http://localhost:11434
→ Automatically fetches models from Ollama API
→ Populates dropdown with:
   - llama3.2:latest (ctx: 8192, 4.7 GB)
   - mistral:latest (ctx: 4096, 3.8 GB)
   - phi-2 (ctx: 2048, 1.3 GB)
```

**Status Messages**:
- "Connecting to http://localhost:11434..."
- "✅ Found 3 model(s)"
- "❌ No models found. Check the URL..."
- "⚠️ Could not connect: Connection refused"

### 4. 📋 Backend List Display

**Location**: Settings → Backends tab → Middle section

Shows all configured backends with:
- Name and Base URL
- Provider type
- **Selected Model** ✨
- **Context Length** ✨
- Enabled/Disabled toggle

**Example Display**:
```
┌─────────────────────────────────────┐
│ My Ollama - http://localhost:11434  │
│ Provider: ollama                    │
│ Model: llama3.2:latest              │
│ Context Length: 8192                │
│ [✓] Enabled                         │
└─────────────────────────────────────┘
```

### 5. 💾 Configuration Persistence

**File**: `appsettings.json` in application directory

**Example Configuration**:
```json
{
  "Backends": [
    {
      "Name": "My Ollama",
      "BaseUrl": "http://localhost:11434",
      "Provider": "ollama",
      "IsEnabled": true,
      "SelectedModel": "llama3.2:latest",
      "ContextLength": 8192
    },
    {
      "Name": "LM Studio",
      "BaseUrl": "http://localhost:1234",
      "Provider": "lmstudio",
      "IsEnabled": true,
      "SelectedModel": "phi-2",
      "ContextLength": 4096
    },
    {
      "Name": "OpenAI",
      "BaseUrl": "https://api.openai.com/v1",
      "Provider": "openai",
      "IsEnabled": false,
      "ApiKey": "sk-...",
      "SelectedModel": "gpt-4",
      "ContextLength": 8192
    }
  ],
  "SelectedBackendName": "My Ollama",
  "EnableTrafficLogging": true,
  "AutoReconnectSignalR": true
}
```

## Usage Workflows

### Adding a New Ollama Backend

1. Open Settings (⚙️ button in sidebar)
2. Go to **Backends** tab
3. Click **➕ Add Backend**
4. Enter name: "My Ollama Server"
5. Select provider: "Ollama (Local LLM)"
6. Base URL auto-fills: `http://localhost:11434`
7. **Models auto-discover** - dropdown populates automatically
8. Select a model from dropdown
9. Context length auto-fills
10. Click **💾 Save**

**Result**: Backend is added to list and available in Active Backend selector

### Adding Multiple Ollama Instances

You can add multiple Ollama backends with the same model:

**Backend 1**:
- Name: "Ollama GPU Server"
- Provider: ollama
- Base URL: http://192.168.1.100:11434
- Model: llama3.2:latest

**Backend 2**:
- Name: "Ollama CPU Server"
- Provider: ollama
- Base URL: http://192.168.1.101:11434
- Model: llama3.2:latest

Both will auto-discover their models independently!

### Editing an Existing Backend

1. Select backend in list
2. Click **✏️ Edit**
3. Dialog opens with all current values
4. **Models auto-discover** from the current URL
5. Make changes (name, URL, model, etc.)
6. Click **💾 Save**

### Switching Active Backend

1. Go to Settings → Backends tab
2. Use the **🎯 Active Backend** dropdown at the top
3. Select the backend you want to use
4. Click **💾 Save** in Settings dialog

The selected backend is now active for all API calls!

## Model Discovery Details

### Ollama API

**Endpoint**: `GET {baseUrl}/api/tags`

**Response Parsing**:
- Lists all available models
- Extracts model name, size, modified date
- Fetches model details via `POST {baseUrl}/api/show`
- Parses modelfile for `num_ctx` parameter (context length)

**Example**:
```
Model: llama3.2:latest
Size: 4.7 GB
Context: 8192 (extracted from modelfile)
```

### LM Studio API

**Endpoint**: `GET {baseUrl}/v1/models`

**Response Parsing**:
- OpenAI-compatible format
- Lists model IDs
- Assumes default context length of 4096

**Example**:
```
Model: phi-2
Context: 4096 (default)
```

### OpenAI

OpenAI models are not auto-discoverable. Common models:
- gpt-4 (8k or 32k context)
- gpt-3.5-turbo (4k or 16k context)
- gpt-4-turbo (128k context)

Manual entry required.

## Benefits

✅ **Multiple Backend Support**: Configure as many backends as needed
✅ **Automatic Discovery**: No manual model entry for Ollama/LM Studio
✅ **Context Awareness**: Context lengths auto-detected and saved
✅ **Easy Switching**: One-click backend selection
✅ **Persistent Config**: All settings saved between sessions
✅ **User-Friendly**: Intuitive UI with helpful status messages
✅ **Flexible**: Supports local and cloud providers
✅ **Secure**: API keys stored securely

## Technical Details

**Model Discovery Service**: `Services/ModelDiscoveryService.cs`
- Async HTTP calls to provider APIs
- 5-second timeout for quick response
- Error handling with user-friendly messages
- Supports Ollama and LM Studio endpoints

**Backend Editor Dialog**: `BackendEditorDialog.xaml/.cs`
- Full MVVM pattern
- Observable collection for models
- Automatic discovery on provider change
- Validation before save

**Settings Integration**: `SettingsDialog.xaml/.cs`
- Active backend selector
- Backend list view
- Add/Edit/Remove operations
- Configuration persistence

**Configuration Model**: `Models/AppConfiguration.cs`
```csharp
public class BackendConfiguration
{
    public string Name { get; set; }
    public string BaseUrl { get; set; }
    public string Provider { get; set; }
    public bool IsEnabled { get; set; }
    public string? ApiKey { get; set; }
    public string? SelectedModel { get; set; }
    public int? ContextLength { get; set; }
    public Dictionary<string, string> CustomHeaders { get; set; }
}
```

## Future Enhancements

Potential improvements:
- [ ] Multi-backend load balancing
- [ ] Backend health monitoring
- [ ] Model benchmarking
- [ ] Custom model parameters (temperature, top_p, etc.)
- [ ] Backend templates/presets
- [ ] Import/export backend configurations
