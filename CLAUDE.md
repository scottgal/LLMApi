# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**mostlylucid.mockllmapi** is a reusable NuGet package that adds LLM-powered mock API endpoints to any ASP.NET Core application. It generates realistic, varied JSON responses using a local LLM (via Ollama) without requiring databases, hardcoded fixtures, or mock data files.

The repository contains three projects:
- **mostlylucid.mockllmapi**: The NuGet package library (main deliverable)
- **LLMApi**: Demo application showing usage
- **LLMApi.Tests**: Comprehensive test suite (191 tests passing, 5 gRPC tests skipped)

## Core Architecture

### NuGet Package Structure (mostlylucid.mockllmapi/)

**mostlylucid.mockllmapiOptions.cs**
- Configuration POCO with appsettings.json binding
- Properties: BaseUrl, ModelName, Temperature, TimeoutSeconds, CustomPromptTemplate, etc.
- Section name: "mostlylucid.mockllmapi"

**mostlylucid.mockllmapiService.cs**
- Main service class injected via DI
- Methods:
  - `ReadBodyAsync()`: Extracts request body
  - `ExtractShape()`: Gets shape from query/header/body (precedence order)
  - `BuildPrompt()`: Creates LLM prompts with randomness (GUID seed + timestamp)
  - `BuildChatRequest()`: Formats OpenAI-compatible request
  - `CreateHttpClient()`: Factory method for configured HttpClient

**mostlylucid.mockllmapiExtensions.cs**
- Extension methods for easy integration:
  - `Addmostlylucid.mockllmapi(IConfiguration)`: Registers services with appsettings config
  - `Addmostlylucid.mockllmapi(Action<mostlylucid.mockllmapiOptions>)`: Registers with inline config
  - `Mapmostlylucid.mockllmapi(string pattern, bool includeStreaming)`: Maps endpoints
- Endpoint implementations:
  - Non-streaming: `/{pattern}/**` - Returns complete JSON
  - Streaming: `/{pattern}/stream/**` - Returns SSE with chunks

### Request Flow

1. HTTP request arrives at configured pattern (e.g., `/api/mock/users`)
2. mostlylucid.mockllmapiService extracts: method, path, query, body, shape
3. Random seed (GUID) + timestamp injected for uniqueness
4. Prompt built (uses custom template if configured)
5. HttpClient posts to Ollama with temperature setting
6. Response parsed and returned (complete JSON or SSE stream)

### Key Design Decisions

- **Temperature 1.2**: High randomness ensures varied data across requests
- **Shape precedence**: Query param > Header > Body field
- **Streaming format**: `{"chunk":"...","done":false}` per token, final `{"content":"...","done":true}`
- **HttpClientFactory**: Proper disposal and timeout management
- **Error handling**: All endpoints catch exceptions and return JSON errors

### LLM Provider Architecture (v1.8.0+)

**Services/Providers/ILlmProvider.cs**
- Provider abstraction interface
- Methods: `GetCompletionAsync()`, `GetStreamingCompletionAsync()`, `GetNCompletionsAsync()`, `ConfigureClient()`
- Property: `Name` (provider identifier)

**Services/Providers/OllamaProvider.cs**
- Default provider for local Ollama instances
- Optional API key support
- OpenAI-compatible API format

**Services/Providers/OpenAIProvider.cs**
- Official OpenAI API provider
- Required API key (throws if missing)
- Full streaming and n-completions support

**Services/Providers/LMStudioProvider.cs**
- Local LM Studio provider
- OpenAI-compatible format
- Optional API key

**Services/Providers/LlmProviderFactory.cs**
- Factory pattern for provider management
- Registry: `ollama`, `openai`, `lmstudio`
- Fallback to Ollama for unknown providers

**Services/LlmBackendSelector.cs**
- Selects backend from configuration
- Per-request selection via `X-LLM-Backend` header or `?backend=` query param
- Falls back to first enabled backend

**Services/LlmClient.cs** (Updated)
- Now uses `LlmProviderFactory` for API calls
- Methods accept optional `HttpRequest?` parameter for per-request backend selection
- Resilience pipeline works across all providers

**Configuration (LLMockApiOptions.cs)**
- `Backends` array: List of `LlmBackendConfig`
- Legacy `BaseUrl`/`ModelName` still supported (creates default Ollama backend)
- Backward compatible - no breaking changes

**LlmBackendConfig Properties:**
- `Name`: Unique identifier for backend selection
- `Provider`: "ollama", "openai", or "lmstudio"
- `BaseUrl`: Full endpoint URL
- `ModelName`: Model identifier
- `ApiKey`: Optional API key (required for OpenAI)
- `Enabled`: Active/inactive flag
- `Weight`: Reserved for future load balancing
- `MaxTokens`: Optional max output tokens (overrides global `MaxOutputTokens`)

**Provider Selection Flow:**
1. Check for `X-LLM-Backend` header
2. Check for `?backend=` query parameter
3. Fall back to first enabled backend in `Backends` array
4. If no backends configured, use legacy `BaseUrl`/`ModelName`

## AutoShape Feature (Shape Memory)

The autoshape feature automatically remembers the JSON structure of the first response to an endpoint and reuses it for subsequent requests, ensuring consistent response structures across multiple calls to the same logical endpoint.

### Key Components

**Services/AutoShapeManager.cs**
- Main coordinator for autoshape operations
- Checks if autoshape is enabled (config + per-request override)
- Retrieves shapes from memory when no explicit shape is provided
- Stores shapes after successful responses

**Services/IShapeStore.cs + MemoryCacheShapeStore.cs**
- Interface and in-memory implementation for shape storage
- Uses IMemoryCache with sliding expiration (default: 15 minutes)
- Thread-safe with concurrent dictionary tracking
- Automatic cleanup on expiration

**Services/ShapeExtractorFromResponse.cs**
- Extracts JSON structure from response data
- Creates simplified templates (e.g., `{"name": "", "id": 0, "active": true}`)
- Validates responses (skips error responses)
- Handles objects, arrays, and nested structures

**Services/PathNormalizer.cs**
- Normalizes endpoint paths for grouping
- Converts `/api/mock/users/123` → `/api/mock/users/{id}`
- Recognizes UUIDs, numeric IDs, and alphanumeric patterns
- Preserves known keywords (api, users, products, etc.)

### Configuration

Add to `appsettings.json` (mostlylucid.mockllmapi section):

```json
{
  "mostlylucid.mockllmapi": {
    "EnableAutoShape": true,               // Global enable/disable (default: true)
    "ShapeExpirationMinutes": 15          // Sliding expiration for shapes (default: 15)
  }
}
```

### Per-Request Override

Enable or disable autoshape for specific requests:

```http
# Enable/disable autoshape
GET /api/mock/users?autoshape=true
# or
GET /api/mock/users
X-Auto-Shape: true

# Renew/replace existing shape (if stored shape is bad)
GET /api/mock/users?renewshape=true
# or
GET /api/mock/users
X-Renew-Shape: true
```

**Using renewshape:**
- Forces generation of a new shape, replacing the stored one
- Useful when the first response had a bad/unexpected structure
- The new response will define the shape going forward
- Example: If `/api/mock/users/1` had incomplete data, call `/api/mock/users/1?renewshape=true` to reset it

### Behavior

**When autoshape is enabled:**

1. **First request to `/api/mock/users/123`:**
   - No explicit shape provided
   - No shape in memory
   - Generates response freely
   - Extracts structure: `{"id": 0, "name": "", "email": ""}`
   - Stores under normalized path: `/api/mock/users/{id}`

2. **Second request to `/api/mock/users/456`:**
   - No explicit shape provided
   - Finds stored shape for `/api/mock/users/{id}`
   - Uses stored shape to guide LLM generation
   - Response follows same structure as first request

3. **Request with explicit shape:**
   - Autoshape is **skipped** (explicit shape takes precedence)
   - Explicit shape used directly

### Important Notes

- **Enabled by default**: Automatically active to match user expectations (can be disabled via config)
- **Explicit shapes take precedence**: If you provide a shape via query param, header, or body, autoshape is not applied
- **Path normalization**: Different IDs to same endpoint share the same shape (e.g., `/users/1`, `/users/2`, `/users/abc` all use same shape)
- **Sliding expiration**: Shapes automatically expire after inactivity (configurable)
- **Works across all endpoint types**: Regular, streaming, and GraphQL
- **Error responses ignored**: Only successful JSON responses are used for shape extraction

### Integration Points

Autoshape is integrated into:
- `RegularRequestHandler` (Regular requests in RegularRequestHandler.cs:94-108 and :376-377)
- `StreamingRequestHandler` (Streaming requests in StreamingRequestHandler.cs:158-172 and :281-282)
- `GraphQLRequestHandler` (GraphQL requests in GraphQLRequestHandler.cs:132-133)

## Development Commands

### Build Everything

```bash
# Build solution
dotnet build LLMApi.sln

# Build NuGet package
dotnet pack mostlylucid.mockllmapi/mostlylucid.mockllmapi.csproj -c Release
# Output: mostlylucid.mockllmapi/bin/Release/mostlylucid.mockllmapi.1.0.0.nupkg

# Install locally for testing
dotnet add package mostlylucid.mockllmapi --source ./mostlylucid.mockllmapi/bin/Release
```

### Run Demo Application

```bash
# From solution root
dotnet run --project LLMApi/LLMApi.csproj

# Endpoints will be at:
# - http://localhost:5116/api/mock/**
# - http://localhost:5116/api/mock/stream/**
```

### Testing

```bash
# Run all tests
dotnet test

# Run with verbose output
dotnet test --verbosity detailed

# Test specific project
dotnet test LLMApi.Tests/LLMApi.Tests.csproj
```

Test project includes:
- Body reading tests
- Shape extraction tests (all 3 methods + precedence)
- Prompt building tests (randomness, templates)
- Options configuration tests
- HttpClient creation tests

### Testing with HTTP File

Use `LLMApi/LLMApi.http` with Visual Studio, Rider, or REST Client extension.
All examples use `/api/mock` pattern to match current configuration.

## Configuration

### Demo App (LLMApi/appsettings.json)

```json
{
  "mostlylucid.mockllmapi": {
    "BaseUrl": "http://localhost:11434/v1/",
    "ModelName": "llama3",
    "Temperature": 1.2,
    "TimeoutSeconds": 30,
    "EnableVerboseLogging": false,
    "ContextExpirationMinutes": 15
  }
}
```

**Key Configuration Options:**
- `BaseUrl`: LLM API endpoint (default: `http://localhost:11434/v1/`)
- `ModelName`: Model to use (default: `llama3`)
- `Temperature`: Randomness level (default: `1.2` for high variety)
- `TimeoutSeconds`: Request timeout (default: `30`)
- `ContextExpirationMinutes`: Auto-expire inactive API contexts (default: `15`)
  - Contexts maintain consistency across related requests
  - Automatically cleaned up after inactivity to prevent memory leaks
  - Set higher (60+) for long test sessions, lower (5) to save memory

### Demo App (LLMApi/Program.cs)

Minimal example showing package usage:

```csharp
using mostlylucid.mockllmapi;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Addmostlylucid.mockllmapi(builder.Configuration);

var app = builder.Build();
app.Mapmostlylucid.mockllmapi("/api/mock", includeStreaming: true);

app.Run();
```

## Common Modifications

### Change NuGet Package Version

Edit mostlylucid.mockllmapi/mostlylucid.mockllmapi.csproj:
```xml
<Version>1.1.0</Version>
```

### Add New Configuration Option

1. Add property to `mostlylucid.mockllmapiOptions.cs`
2. Use in `mostlylucid.mockllmapiService.cs`
3. Document in README.md
4. Add test in `mostlylucid.mockllmapiServiceTests.cs`

### Customize Default Prompts

Edit `mostlylucid.mockllmapiService.BuildPrompt()` method (lines ~90-130).
Or users can override via `CustomPromptTemplate` in config.

### Support Different LLM Providers

Currently hardcoded for OpenAI-compatible APIs (Ollama).
To support others:
1. Create provider interfaces in library
2. Add provider selection to options
3. Implement provider-specific request/response handling

## Important Implementation Notes

- **Target frameworks**: .NET 8.0 and .NET 9.0 (multi-targeted for maximum compatibility)
- **Demo project**: Runs on .NET 8.0
- **No version overloading**: Currently only supports single configuration per app
- **HttpClientFactory**: Required dependency - automatically registered by Addmostlylucid.mockllmapi
- **Endpoint routing**: Requires `UseRouting()` called before `Mapmostlylucid.mockllmapi()`
- **Shape validation**: No JSON validation - invalid shapes pass through to LLM
- **Streaming**: Uses SSE (text/event-stream), not gRPC or WebSockets
- **Thread safety**: Service is registered as Scoped, safe for concurrent requests
- **Prompt injection**: User input goes directly to LLM - not for production use with untrusted input

## Publishing NuGet Package

### Automated Publishing via GitHub Actions (Recommended)

This project uses **NuGet Trusted Publishers** for secure OIDC-based publishing.

**One-time setup** (see `.github/NUGET_SETUP.md` for details):
1. Add `NUGET_USERNAME` secret to GitHub (your NuGet.org profile name)
2. Upload v1.0.0 manually to NuGet.org (first version only)
3. Configure Trusted Publishing Policy on NuGet.org:
   - Owner: `mostlylucid`
   - Repository: `mostlylucid.mockllmapi`
   - Workflow: `publish-nuget.yml`

**Publishing a new version**:
```bash
# Update version in mostlylucid.mockllmapi/mostlylucid.mockllmapi.csproj
# Then create and push a version tag:
git tag v1.0.1
git push origin v1.0.1
```

The GitHub Action will automatically:
1. Build the solution
2. Run all tests
3. Pack the NuGet package
4. Request temporary OIDC token from GitHub
5. Exchange for short-lived NuGet API key (1-hour validity)
6. Publish to NuGet.org

### Manual Publishing

```bash
# Build release package
dotnet pack mostlylucid.mockllmapi/mostlylucid.mockllmapi.csproj -c Release

# Publish to local feed for testing
dotnet nuget push mostlylucid.mockllmapi/bin/Release/mostlylucid.mockllmapi.1.0.0.nupkg \
  --source ~/local-nuget-feed
```

## Error Simulation

The package includes comprehensive error simulation capabilities for testing client error handling.

### Error Configuration Methods

Error responses can be configured using four methods (in precedence order):

1. **Query Parameters** (highest precedence)

**IMPORTANT**: Query parameter values must be URL-encoded. Common encodings:
- Space → `%20`
- `&` → `%26`
- `:` → `%3A`
- `'` → `%27`
- `,` → `%2C`

```http
# Properly encoded
GET /api/mock/users?error=404&errorMessage=Not%20found&errorDetails=User%20ID%20invalid

# Complex example with special characters
# Decoded: "Invalid input: email & phone"
GET /api/mock/users?error=400&errorMessage=Invalid%20input%3A%20email%20%26%20phone
```

2. **HTTP Headers**
```http
GET /api/mock/users
X-Error-Code: 401
X-Error-Message: Unauthorized
X-Error-Details: Token expired
```

3. **Shape JSON** (`$error` property)
```http
GET /api/mock/users?shape={"$error":404}
# Or complex:
GET /api/mock/users?shape={"$error":{"code":422,"message":"Validation failed","details":"Email invalid"}}
```

4. **Request Body** (`error` property)
```json
{
  "error": {
    "code": 409,
    "message": "Conflict",
    "details": "Resource already exists"
  }
}
```

### Error Response Formats

**Regular/Streaming endpoints**:
```json
{
  "error": {
    "code": 404,
    "message": "Not Found",
    "details": "Optional additional context"
  }
}
```

**GraphQL endpoint**:
```json
{
  "data": null,
  "errors": [
    {
      "message": "Not Found",
      "extensions": {
        "code": 404,
        "details": "Optional additional context"
      }
    }
  ]
}
```

**SignalR contexts**:
Configure via `ErrorConfig` property in `HubContextConfig`:
```csharp
new HubContextConfig
{
    Name = "errors",
    ErrorConfig = new ErrorConfig(500, "Server error", "Database unavailable")
}
```

### Error Handling in Code

The `ErrorConfig` class (Models/ErrorConfig.cs) provides:
- Default messages for common HTTP status codes (400-504)
- JSON and GraphQL response formatting
- Automatic JSON escaping for safe output

The `ShapeExtractor` service automatically extracts and sanitizes error hints from shapes,
removing `$error` properties before passing shapes to the LLM.

### Testing Error Responses

See `LLMApi/LLMApi.http` for comprehensive error simulation examples including:
- 4xx client errors (400, 401, 403, 404, 409, 422, 429)
- 5xx server errors (500, 503)
- GraphQL errors
- Streaming endpoint errors
- Error precedence demonstrations

## Troubleshooting

**"Mapmostlylucid.mockllmapi requires endpoint routing"**
- Call `app.UseRouting()` before `Mapmostlylucid.mockllmapi()`

**LLM returns empty responses**
- Check Ollama is running: `curl http://localhost:11434`
- Verify model is installed: `ollama list`
- Check BaseUrl in configuration

**Streaming not working**
- Ensure `Accept: text/event-stream` header
- Check `includeStreaming: true` in Mapmostlylucid.mockllmapi call
- Verify LLM supports streaming (most do)

**Data not random enough**
- Increase Temperature (try 1.5-2.0)
- Check prompts include random seed and timestamp
- Try different model (some models are more creative)

**Timeouts**
- Increase TimeoutSeconds in configuration
- Check LLM server performance
- Consider using faster/smaller model

## License

This project is released into the public domain under the [Unlicense](https://unlicense.org). See the LICENSE file for details. You are free to use, modify, and distribute this code without restriction.
