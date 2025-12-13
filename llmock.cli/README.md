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

## Available Endpoints

Once running, the following endpoints are available:

- `GET/POST /api/mock/**` - Shape-based mock endpoints
- `GET/POST /api/mock/stream/**` - Streaming mock endpoints
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

| Condition | Console Level | File Level | Example |
|-----------|---------------|------------|---------|
| Normal request | Info | Not logged | `GET /api/mock/users` (200, 500ms) |
| Slow request (>10s) | Warning | Warning | `GET /api/mock/huge` (200, 12000ms) |
| Client error (4xx) | Warning | Warning | `GET /api/mock/missing` (404) |
| Server error (5xx) | Error | Error | `GET /api/mock/broken` (500) |
| Exception | Error | Error | Any unhandled exception |

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

Native AOT compilation is **not currently supported** due to JSON serialization reflection requirements in the core library. The library would need to be refactored to use JSON source generators throughout.

However, the trimmed single-file executable is already very small (~14 MB) and starts quickly, so AOT is not essential for this use case.

## License

This project is released into the public domain under the Unlicense.
