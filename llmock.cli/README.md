# LLMock CLI

A minimal, cross-platform CLI tool for running LLM-powered mock API servers with OpenAPI support.

## Features

- **OpenAPI Import**: Load any OpenAPI 3.x or Swagger 2.0 spec from URL or file
- **Multiple LLM Backends**: Support for Ollama, OpenAI, and LM Studio
- **Configuration Flexibility**: CLI args, appsettings.json, or environment variables
- **Cross-Platform**: Works on Windows, Linux, and macOS
- **Small Binary Size**: Optimized with trimming and compression

## Installation

### From Source

```bash
dotnet publish -c Release -r <runtime-id>
```

Runtime IDs:

- `win-x64` - Windows 64-bit
- `linux-x64` - Linux 64-bit
- `osx-x64` - macOS Intel
- `osx-arm64` - macOS Apple Silicon

### Example

```bash
# Windows
dotnet publish -c Release -r win-x64

# Linux
dotnet publish -c Release -r linux-x64

# macOS Apple Silicon
dotnet publish -c Release -r osx-arm64
```

The binary will be in `bin/Release/net10.0/<runtime-id>/publish/`

## Usage

### Basic Usage

Start a server with default Ollama backend:

```bash
llmock serve
```

### Import OpenAPI Spec

```bash
llmock serve --spec https://petstore3.swagger.io/api/v3/openapi.json
```

Multiple specs:

```bash
llmock serve \
  --spec https://petstore3.swagger.io/api/v3/openapi.json \
  --spec ./my-api.yaml \
  --spec https://api.github.com/openapi.json
```

### Configure LLM Backend

```bash
# Use OpenAI
llmock serve \
  --backend openai \
  --model gpt-4o-mini \
  --api-key sk-your-key-here

# Use local Ollama with custom model
llmock serve \
  --backend ollama \
  --model codellama:7b \
  --base-url http://localhost:11434/v1/

# Use LM Studio
llmock serve \
  --backend lmstudio \
  --model local-model \
  --base-url http://localhost:1234/v1/
```

### Custom Port

```bash
llmock serve --port 8080
```

### Use Configuration File

Create `appsettings.json`:

```json
{
  "LLMockApi": {
    "Backends": [
      {
        "Name": "ollama",
        "Provider": "ollama",
        "BaseUrl": "http://localhost:11434/v1/",
        "ModelName": "llama3",
        "Enabled": true
      }
    ],
    "OpenApiSpecs": [
      {
        "Name": "petstore",
        "Source": "https://petstore3.swagger.io/api/v3/openapi.json",
        "BasePath": "/petstore"
      }
    ]
  }
}
```

Then run:

```bash
llmock serve
# or specify custom config file
llmock serve --config my-config.json
```

### Environment Variables

All settings can be configured via environment variables with `LLMOCK_` prefix:

```bash
# Linux/macOS
export LLMOCK_LLMockApi__Backends__0__ApiKey=sk-your-key
llmock serve

# Windows PowerShell
$env:LLMOCK_LLMockApi__Backends__0__ApiKey="sk-your-key"
llmock serve
```

## Catch-All Mock Configuration

By default, the CLI is configured to respond to **ANY endpoint** (except management endpoints) with mock data. This is
configurable in `appsettings.json`:

```json
{
  "LLMockCli": {
    "CatchAllMockPath": "/"
  }
}
```

**Options:**

| Value          | Behavior                                | Example Requests                                 |
|----------------|-----------------------------------------|--------------------------------------------------|
| `"/"`          | Mock ALL paths (default)                | `/`, `/api/test`, `/serviceworker`, `/users/123` |
| `"/api"`       | Mock only paths starting with `/api`    | `/api/test`, `/api/users` (but NOT `/test`)      |
| `null` or `""` | Disable catch-all, only explicit routes | Only `/api/mock/**` works                        |

**Management Endpoints (Always Excluded):**

These endpoints are always excluded from catch-all mocking:

- `/api/openapi/**` - OpenAPI spec management
- `/api/contexts` - API context management
- `/api/grpc-protos` - gRPC proto management
- `/api/signalr/**` - SignalR context management
- `/api/graphql` - GraphQL endpoint
- `/hubs/**` - SignalR hubs

## Available Endpoints

Once running, the following endpoints are available:

**Standard Mock Endpoints:**

- `GET/POST /api/mock/**` - Shape-based mock endpoints
- `GET/POST /api/mock/stream/**` - Streaming mock endpoints

**Catch-All (Configurable):**

- `ANY /**` - Dynamic mock for any path (when CatchAllMockPath="/")
- `ANY /api/**` - Dynamic mock for API paths (when CatchAllMockPath="/api")

**Management Endpoints:**

- `POST /api/openapi/specs` - Load OpenAPI specs at runtime
- `GET /api/openapi/specs` - List loaded specs
- `DELETE /api/openapi/specs/{name}` - Remove a spec
- `GET /api/contexts` - View API contexts

## Examples

### Example 1: Quick Start with Petstore

```bash
llmock serve --spec https://petstore3.swagger.io/api/v3/openapi.json --port 5000
```

Then test:

```bash
curl http://localhost:5000/petstore/pet/123
```

### Example 2: Multiple Backends

Create `appsettings.json`:

```json
{
  "LLMockApi": {
    "Backends": [
      {
        "Name": "ollama-fast",
        "Provider": "ollama",
        "ModelName": "llama3:8b",
        "BaseUrl": "http://localhost:11434/v1/",
        "Enabled": true
      },
      {
        "Name": "ollama-smart",
        "Provider": "ollama",
        "ModelName": "llama3:70b",
        "BaseUrl": "http://localhost:11434/v1/",
        "Enabled": true
      },
      {
        "Name": "openai",
        "Provider": "openai",
        "ModelName": "gpt-4o-mini",
        "ApiKey": "${OPENAI_API_KEY}",
        "Enabled": true
      }
    ]
  }
}
```

Run and switch backends per request:

```bash
llmock serve

# Use default backend (ollama-fast)
curl http://localhost:5000/api/mock/users

# Switch to ollama-smart for complex request
curl http://localhost:5000/api/mock/complex-data?backend=ollama-smart

# Switch to OpenAI
curl http://localhost:5000/api/mock/premium-data?backend=openai
```

### Example 3: Development Workflow

```bash
# Start server with your API spec
llmock serve --spec ./openapi.yaml --port 3000

# In another terminal, test your frontend against the mock
npm run dev  # Your frontend on port 8080, calling localhost:3000
```

## Command Reference

### `llmock serve`

Start the mock API server.

**Options:**

- `--spec, -s <path-or-url>` - OpenAPI spec file or URL (repeatable)
- `--port, -p <port>` - Server port (default: 5000)
- `--backend, -b <provider>` - LLM backend provider (ollama, openai, lmstudio)
- `--model, -m <model>` - Model name
- `--base-url <url>` - LLM backend base URL
- `--api-key, -k <key>` - API key for LLM backend
- `--base-path <path>` - Base path for OpenAPI endpoints (default: /api)
- `--config, -c <file>` - Path to appsettings.json file

## Logging

The CLI includes comprehensive Serilog logging with two outputs:

### Console Logging (Info and above)

The console shows informative messages about what's happening without being overwhelming:

```
[14:23:15 INF] LLMock CLI Server starting on http://localhost:5000
[14:23:15 INF] Using default LLM backend: ollama/llama3 at http://localhost:11434/v1/
[14:23:16 INF] Loading 1 OpenAPI specifications
[14:23:16 INF] Loaded OpenAPI spec 'petstore' from https://petstore3.swagger.io/api/v3/openapi.json - 19 endpoints at /api/spec0
[14:23:16 INF] Server ready - listening for connections
[14:23:16 INF] Application started successfully
[14:23:25 INF] HTTP GET /api/mock/users responded 200 in 842.3456ms
[14:23:30 INF] HTTP POST /api/spec0/pet responded 200 in 1234.5678ms
```

**What gets logged to console:**

- Server startup and configuration
- OpenAPI spec loading (success and failures)
- HTTP requests with method, path, status code, and response time
- Backend selection and configuration
- Application lifecycle events

**What doesn't flood the console:**

- Individual LLM tokens during streaming
- Request/response bodies
- Detailed debug information
- Framework internals

### File Logging (Warning and above)

Detailed logs are written to `logs/llmock-{date}.log`:

```
[2025-12-13 14:23:15.123 +00:00 WRN] LLM request took longer than expected: 15234ms
[2025-12-13 14:23:20.456 +00:00 ERR] Failed to connect to LLM backend at http://localhost:11434/v1/
System.Net.Http.HttpRequestException: Connection refused
   at System.Net.Http.HttpClient.SendAsync(...)
```

**File logs include:**

- Warnings (slow requests, deprecation notices)
- Errors (LLM connection failures, spec loading errors)
- Critical issues
- Full exception stack traces
- Daily rotation (7 days retained)

### Log Levels

The CLI uses intelligent log level selection:

| Condition           | Console Level | File Level | Example                             |
|---------------------|---------------|------------|-------------------------------------|
| Normal request      | Info          | Not logged | `GET /api/mock/users` (200, 500ms)  |
| Slow request (>10s) | Warning       | Warning    | `GET /api/mock/huge` (200, 12000ms) |
| Client error (4xx)  | Warning       | Warning    | `GET /api/mock/missing` (404)       |
| Server error (5xx)  | Error         | Error      | `GET /api/mock/broken` (500)        |
| Exception           | Error         | Error      | Any unhandled exception             |

### Connection Logging

When connections are made, you'll see:

```
[14:23:25 INF] HTTP GET /api/mock/users?shape={"id":0,"name":"string"} responded 200 in 842.3456ms
```

Additional diagnostic context (visible in file logs):

- Remote IP address
- User Agent
- Selected LLM backend (if specified via `?backend=` parameter)

### Controlling Verbosity

To see more detailed logs in the console during development, modify the CLI source:

```csharp
.WriteTo.Console(
    restrictedToMinimumLevel: LogEventLevel.Debug,  // Change from Information
    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
```

## Configuration Reference

The CLI supports all configuration options from the [mostlylucid.mockllmapi](https://github.com/scottgal/LLMApi) library.

### Core LLM Backend Configuration

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `Backends` | `Array` | `[]` | Multiple LLM backends for load balancing/failover. See [Multiple LLM Backends](https://github.com/scottgal/LLMApi/blob/main/docs/MULTIPLE_LLM_BACKENDS.md) |
| `BaseUrl` | `string` | `http://localhost:11434/v1/` | **DEPRECATED**: Use `Backends` instead |
| `ModelName` | `string` | `llama3` | **DEPRECATED**: Use `Backends` instead |
| `Temperature` | `double` | `1.2` | LLM generation temperature (0.0-2.0). Higher = more creative |
| `TimeoutSeconds` | `int` | `30` | Request timeout in seconds |

**Backend Array Configuration:**

```json
{
  "LLMockApi": {
    "Backends": [
      {
        "Name": "ollama",
        "Provider": "ollama",
        "BaseUrl": "http://localhost:11434/v1/",
        "ModelName": "llama3",
        "Enabled": true,
        "ApiKey": null,
        "MaxTokens": null
      }
    ]
  }
}
```

### Context Window & Response Limits

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `MaxContextWindow` | `int` | `4096` | Total context window. See [model docs](https://github.com/scottgal/LLMApi/blob/main/docs/MULTIPLE_LLM_BACKENDS.md#%EF%B8%8F-ollama-context-window-configuration) |
| `EnableAutoChunking` | `bool` | `true` | Auto-split large responses |
| `MaxItems` | `int` | `1000` | Max items per response AND max cache size |

### Caching Configuration

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `MaxCachePerKey` | `int` | `5` | Max cached variants per key |
| `CacheSlidingExpirationMinutes` | `int` | `15` | Sliding expiration (refreshed on access) |
| `CacheAbsoluteExpirationMinutes` | `int?` | `60` | Absolute expiration time |
| `EnableCacheStatistics` | `bool` | `false` | Track cache metrics |
| `EnableCacheCompression` | `bool` | `false` | Compress cached responses |

### Streaming Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `SseMode` | `string` | `LlmTokens` | Mode: `LlmTokens`, `CompleteObjects`, `ArrayItems` |
| `StreamingChunkDelayMinMs` | `int` | `0` | Min delay between chunks (ms) |
| `StreamingChunkDelayMaxMs` | `int` | `0` | Max delay (random if both set) |
| `EnableContinuousStreaming` | `bool` | `false` | Keep SSE open for continuous data |
| `ContinuousStreamingIntervalMs` | `int` | `2000` | Interval between events (2 sec) |
| `ContinuousStreamingMaxDurationSeconds` | `int` | `300` | Max duration (5 min) |

See [Streaming Modes](https://github.com/scottgal/LLMApi#streaming-modes)

### Resilience & Reliability

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `EnableRetryPolicy` | `bool` | `true` | Exponential backoff retry |
| `MaxRetryAttempts` | `int` | `3` | Max retry attempts |
| `RetryBaseDelaySeconds` | `double` | `1.0` | Base delay (2^attempt) |
| `EnableCircuitBreaker` | `bool` | `true` | Circuit breaker pattern |
| `CircuitBreakerFailureThreshold` | `int` | `5` | Failures before opening |
| `CircuitBreakerDurationSeconds` | `int` | `30` | Duration circuit stays open |

### Rate Limiting Simulation

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `EnableRateLimiting` | `bool` | `false` | Simulate rate-limited APIs |
| `RateLimitDelayRange` | `string?` | `null` | Range: `"500-4000"` or `"max"` |
| `RateLimitStrategy` | `string` | `Auto` | `Auto`, `Sequential`, `Parallel`, `Streaming` |

See [Rate Limiting Docs](https://github.com/scottgal/LLMApi/blob/main/docs/RATE_LIMITING.md)

### OpenAPI Configuration

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `OpenApiSpecs` | `Array` | `[]` | Pre-load OpenAPI specs. See [OpenAPI Features](https://github.com/scottgal/LLMApi/blob/main/docs/OPENAPI-FEATURES.md) |

```json
{
  "OpenApiSpecs": [
    {
      "Name": "petstore",
      "Source": "https://petstore3.swagger.io/api/v3/openapi.json",
      "BasePath": "/petstore",
      "EnableStreaming": false
    }
  ]
}
```

### Simulator Types

Control which endpoint types are enabled:

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `SimulatorTypes.EnableRest` | `bool` | `true` | REST mock endpoints |
| `SimulatorTypes.EnableGraphQL` | `bool` | `true` | GraphQL endpoint |
| `SimulatorTypes.EnableGrpc` | `bool` | `true` | gRPC services |
| `SimulatorTypes.EnableSignalR` | `bool` | `true` | SignalR hubs |
| `SimulatorTypes.EnableOpenApi` | `bool` | `true` | OpenAPI dynamic endpoints |
| `SimulatorTypes.EnableConfiguredApis` | `bool` | `true` | Pre-configured REST APIs |
| `SimulatorTypes.EnableManagementEndpoints` | `bool` | `true` | Management endpoints |

### Tools & Actions

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `ToolExecutionMode` | `string` | `Disabled` | `Disabled`, `Explicit`, `LlmDriven` |
| `Tools` | `Array` | `[]` | Available tool definitions |
| `MaxConcurrentTools` | `int` | `5` | Max parallel executions |
| `MaxToolChainDepth` | `int` | `3` | Max recursion depth |

See [Tools Documentation](https://github.com/scottgal/LLMApi/blob/main/docs/TOOLS.md)

### Request Delays

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `RandomRequestDelayMinMs` | `int` | `0` | Min delay before ANY request |
| `RandomRequestDelayMaxMs` | `int` | `0` | Max delay (random if both set) |

### Context Management

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `ContextExpirationMinutes` | `int` | `15` | Auto-expire inactive contexts |

See [API Contexts](https://github.com/scottgal/LLMApi#api-contexts)

### Other Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `EnableVerboseLogging` | `bool` | `false` | Detailed debug logging |
| `IncludeShapeInResponse` | `bool` | `false` | Include JSON shape in response |
| `GraphQLMaxTokens` | `int?` | `500` | Max tokens for GraphQL (200-300 recommended for small models) |
| `CustomPromptTemplate` | `string?` | `null` | Custom prompt template |

### CLI-Specific Configuration

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `LLMockCli.CatchAllMockPath` | `string?` | `"/"` | Catch-all path: `"/"` (all), `"/api"` (API only), `null` (disabled) |

### Additional Documentation

- **[Main Repository](https://github.com/scottgal/LLMApi)** - Complete documentation
- **[Multiple LLM Backends](https://github.com/scottgal/LLMApi/blob/main/docs/MULTIPLE_LLM_BACKENDS.md)** - Backend config & load balancing
- **[OpenAPI Features](https://github.com/scottgal/LLMApi/blob/main/docs/OPENAPI-FEATURES.md)** - OpenAPI spec import
- **[Rate Limiting](https://github.com/scottgal/LLMApi/blob/main/docs/RATE_LIMITING.md)** - Rate limiting simulation
- **[Tools & Actions](https://github.com/scottgal/LLMApi/blob/main/docs/TOOLS.md)** - Tool execution & MCP
- **[Streaming Modes](https://github.com/scottgal/LLMApi#streaming-modes)** - SSE streaming config

## Configuration Priority

Settings are applied in this order (later overrides earlier):

1. Default values
2. `appsettings.json`
3. Environment variables (LLMOCK_ prefix)
4. Command-line arguments

## Building from Source

### Standard Build

```bash
dotnet build
```

### Optimized Release Build

```bash
# Single-file, trimmed executable
dotnet publish -c Release -r win-x64 \
  /p:PublishSingleFile=true \
  /p:PublishTrimmed=true \
  /p:EnableCompressionInSingleFile=true
```

### All Platforms

```bash
# Windows x64
dotnet publish -c Release -r win-x64

# Windows ARM64
dotnet publish -c Release -r win-arm64

# Linux x64
dotnet publish -c Release -r linux-x64

# Linux ARM64
dotnet publish -c Release -r linux-arm64

# macOS Intel
dotnet publish -c Release -r osx-x64

# macOS Apple Silicon
dotnet publish -c Release -r osx-arm64
```

## Size Optimization

The CLI is optimized for small binary size:

- **PublishTrimmed**: Removes unused code
- **PublishSingleFile**: Single executable
- **EnableCompressionInSingleFile**: Compresses the binary
- **InvariantGlobalization**: Removes localization data

Expected sizes (Release build with trimming):

- Windows x64: ~14 MB
- Linux x64: ~14 MB
- macOS ARM64: ~14 MB

## AOT Compilation

Native AOT compilation is **not currently supported** due to JSON serialization reflection requirements in the core
library. The library would need to be refactored to use JSON source generators throughout.

However, the trimmed single-file executable is already very small (~14 MB) and starts quickly, so AOT is not essential
for this use case.

## License

This project is released into the public domain under the Unlicense.
