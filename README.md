# mostlylucid.mockllmapi

**What it does:** A production-ready ASP.NET Core mocking platform for generating realistic mock API responses using LLMs.

**Why you'd use it:** Add intelligent mock endpoints to any project with just 2 lines of code—no databases, no hardcoded fixtures, no maintenance.

[![NuGet](https://img.shields.io/nuget/v/mostlylucid.mockllmapi.svg)](https://www.nuget.org/packages/mostlylucid.mockllmapi)
[![NuGet](https://img.shields.io/nuget/dt/mostlylucid.mockllmapi.svg)](https://www.nuget.org/packages/mostlylucid.mockllmapi)
[![GitHub Release](https://img.shields.io/github/v/release/scottgal/LLMApi)](https://github.com/scottgal/LLMApi/releases)
[![License: Unlicense](https://img.shields.io/badge/license-Unlicense-blue.svg)](http://unlicense.org/)

**Companion Package:** [mostlylucid.mockllmapi.Testing](./mostlylucid.mockllmapi.Testing/README.md) - Testing utilities with fluent HttpClient integration

---

## How to Read This README

This README is comprehensive by design. Choose your path:

- **"I want to try this in 5 minutes"** → Jump to [Quick Start](#quick-start)
- **"I want to know what this can do"** → See [Features Overview](#features) and [Architecture](#architecture)
- **"I want to understand how it works"** → Read [Feature Documentation](#feature-documentation) and [Advanced Features](#advanced-features)

---

## What's New - TL;DR

**v2.3.0:** Full content type support - form bodies, file uploads, comprehensive testing (405 tests)
**v2.2.0:** Pluggable tools for API integration, pre-configured REST APIs, automatic context memory expiration
**v2.1.0:** Rate limiting, batching, n-completions with multiple execution strategies

<details>
<summary><strong>Click for detailed v2.3.0 feature breakdown</strong></summary>

### Version 2.3.0 Details

**Focus**: Complete content type support for all common HTTP request formats. Fully backward compatible with v2.2.0.

**1. Form Body Support** - Full support for `application/x-www-form-urlencoded` content type. Single and multiple values, automatic JSON conversion, manual construction for .NET 10 compatibility. Perfect for testing HTML forms and traditional web apps.

**2. File Upload Support** - Full support for `multipart/form-data` including file uploads. Memory-safe streaming (8KB buffer), metadata extraction (filename, size, content type), mixed form fields and files. Content is dumped to avoid memory bloat.

**3. Arbitrary Path Lengths** - Support for deep path nesting via `{**path}` catch-all routing. Tested with 9-segment deep paths and complex query strings. No practical limit on path depth (up to ASP.NET Core defaults).

**4. Comprehensive Test Suite** - 228 tests (37 new) covering all features. Form body parsing (12 tests), JSON handling (25 tests), integration tests for full HTTP workflows. 100% pass rate, zero regressions.

**5. .NET 10 Compatibility** - Manual JSON construction throughout to avoid reflection-based serialization issues. All features tested and working on .NET 10 preview builds.

[See TEST_SUMMARY.md and IMPLEMENTATION_SUMMARY.md for complete details](./TEST_SUMMARY.md)
</details>

<details>
<summary><strong>Click for detailed v2.2.0 feature breakdown</strong></summary>

### Version 2.2.0 Details

**Focus**: Pluggable tools for API integration, pre-configured REST APIs, and intelligent context memory. Fully backward compatible with v2.1.0.

**1. Pluggable Tools & Actions System** - Call external REST APIs or chain mock endpoints to create realistic workflows and decision trees. MCP-compatible architecture ready for LLM-driven tool selection. [Full docs →](./docs/TOOLS_ACTIONS.md)

**2. Pre-Configured REST APIs** - Define complete API configurations once, call by name. Shape or OpenAPI spec reference, shared context management, tool integration. See `appsettings.Full.json` for 8 complete examples.

**3. Dynamic Context Memory Management** - Contexts now expire after 15 minutes of inactivity (configurable 5-1440 minutes). No memory leaks, automatic cleanup, smart touch on access.

**4. Intelligent Shared Data Extraction** - Automatically extracts ALL fields from responses at any nesting level. Nested objects, array tracking, first item data, custom fields—all tracked automatically.

**5. Enhanced Documentation** - New comprehensive guides for [Tools & Actions](./docs/TOOLS_ACTIONS.md), [Rate Limiting](./docs/RATE_LIMITING_BATCHING.md), and [API Contexts](./docs/API-CONTEXTS.md).

[See RELEASE_NOTES.md for complete details](./RELEASE_NOTES.md)
</details>

---

## Table of Contents

### Core Concepts
- [Features Overview](#features) - What this system can do
- [Quick Start](#quick-start) - Get running in 5 minutes
- [Architecture](#architecture) - How it all fits together
- [Non-Goals & Boundaries](#non-goals--boundaries) - What this is NOT

### Protocols & Features
- [REST API Mocking](#1-rest-api-mocking)
- [GraphQL API Mocking](#graphql-api-mocking)
- [SSE Streaming](#3-server-sent-events-sse-streaming)
- [SignalR Real-Time](#signalr-real-time-data-streaming)
- [OpenAPI / Swagger](#openapi--swagger-mock-generation)
- [gRPC Services](#6-grpc-service-mocking-new-in-v170)

### Advanced & Architecture
- [Configuration Options](#configuration-options)
- [Feature Documentation](#feature-documentation) - In-depth guides
- [Advanced Features](#advanced-features)
- [Demo Applications](#demo-applications)
- [Testing](#testing)

---

## Architecture Overview

**One engine, multiple protocols, shared infrastructure.** All features share the same generation, context, and control systems—giving you consistent behavior across REST, GraphQL, SSE, SignalR, OpenAPI, and gRPC.

```mermaid
graph LR
    Client[Client] -->|HTTP Request| API[LLMApi<br/>Minimal API]
    API -->|Chat Completion| Ollama[Ollama API<br/>localhost:11434]
    Ollama -->|Inference| Model[llm-model Model]
    Model -->|Response| Ollama
    Ollama -->|JSON/Stream| API
    API -->|JSON/SSE| Client

    API -.->|uses| Helper[AutoApiHelper]

    style API fill:#4CAF50
    style Helper fill:#2196F3
    style Model fill:#FF9800
```

**Key Components:**
- **Single LLM Client**: All protocols use the same LLM integration with resilience policies
- **Shared Context Memory**: API contexts work across REST, GraphQL, SignalR, and OpenAPI
- **Common Features**: Rate limiting, caching, error simulation, and token management apply to all
- **Modular Design**: Use any combination—add only what you need

See [detailed architecture diagrams](#architecture) below for request flow and shape control.

---

## Features

This package provides **six independent features** - use any combination you need (see [Modular Examples](./MODULAR_EXAMPLES.md) for protocol-specific setups):

### 1. REST API Mocking
- **Super Simple**: `AddLLMockApi()` + `MapLLMockApi("/api/mock")` = instant mock API
- **Shape Control**: Specify exact JSON structure via header, query param, or request body
- **All HTTP Methods**: Supports GET, POST, PUT, DELETE, PATCH
- **Wildcard Routing**: Any path under your chosen endpoint works
- **Complete Content Type Support** (NEW in v2.3.0):
  - **JSON** (`application/json`) - Standard JSON request bodies
  - **Form Bodies** (`application/x-www-form-urlencoded`) - HTML form submissions
  - **File Uploads** (`multipart/form-data`) - File uploads with metadata extraction
  - **Memory-Safe**: File content streamed and dumped (only metadata retained)
- **Arbitrary Path Lengths**: Deep path nesting fully supported (`/api/mock/v1/api/products/electronics/computers/laptops/gaming/...`)

### 2. GraphQL API Mocking
- **Native GraphQL Support**: POST to `/api/mock/graphql` with standard GraphQL queries
- **Query-Driven Shapes**: The GraphQL query itself defines the response structure
- **Variables & Operations**: Full support for variables, operation names, and fragments
- **Proper Error Handling**: Returns GraphQL-formatted errors with `data` and `errors` fields

### 3. Server-Sent Events (SSE) Streaming
- **Progressive Streaming**: SSE support with progressive JSON generation
- **Real-time Updates**: Stream data token-by-token to clients
- **Works standalone**: No REST API setup required
- **[Complete SSE Guide](./docs/SSE_STREAMING_MODES.md)** | **[Continuous Streaming](./docs/CONTINUOUS_STREAMING.md)**

### 4. SignalR Real-Time Streaming
- **WebSocket Streaming**: Continuous real-time mock data via SignalR
- **Multiple Contexts**: Run multiple independent data streams simultaneously
- **Lifecycle Management**: Start/stop contexts dynamically with management API
- **Works standalone**: No REST API setup required
- **[SignalR Demo Guide](./SIGNALR_DEMO_GUIDE.md)**

### 5. OpenAPI / Swagger Mock Generation
- **Automatic Endpoint Generation**: Point to any OpenAPI/Swagger spec (URL or file)
- **All Operations Mocked**: Every path and method from spec becomes a live endpoint
- **Schema-Driven Data**: LLM generates realistic data matching your schemas
- **Multiple Specs**: Load and mount multiple API specs simultaneously
- **Works standalone**: No REST API setup required
- **[OpenAPI Features Guide](./docs/OPENAPI-FEATURES.md)**

### 6. gRPC Service Mocking (NEW in v1.7.0)
- **Proto File Upload**: Upload .proto files to generate gRPC service mocks
- **Dual Protocol Support**: JSON over HTTP for testing, binary Protobuf for production-grade mocking
- **Dynamic Serialization**: Runtime Protobuf encoding without code generation
- **LLM-Powered Data**: Realistic responses matching your proto message definitions
- **Works standalone**: No REST API setup required
- **[gRPC Support Guide](./docs/GRPC_SUPPORT.md)**

### Common Features
- **Configurable**: appsettings.json or inline configuration
- **Highly Variable Data**: Each request/update generates completely different realistic data
- **NuGet Package**: Easy to add to existing projects
- **API Contexts**: Maintain consistency across related requests - **[Complete Guide](./docs/API-CONTEXTS.md)**
- **Error Simulation**: Comprehensive error testing with 4xx/5xx status codes, custom messages, and multiple configuration methods
- **Testing Utilities**: Companion package `mostlylucid.mockllmapi.Testing` for easy HttpClient integration in tests - **[See Testing Section](#testing)**

---

## Non-Goals & Boundaries

**What this is:**
- A development and prototyping tool for realistic mock data generation
- Perfect for frontend development, API design, demos, and testing
- Zero-maintenance alternative to hardcoded fixtures and database-backed mocks

**What this is NOT:**
- **Not a production data source** - Generated data is realistic but synthetic
- **Not a replacement for contract tests** - Use this for development; validate real APIs separately
- **Not for unvalidated production traffic** - User input goes directly to LLM prompts (prompt injection possible)
- **Not a database or persistent store** - No state maintained between requests (except optional API contexts)
- **Not a substitute for real backend logic** - Business rules, validation, and workflows remain simplified

**When to use it:**
- Developing frontends before backends are ready
- Creating realistic demos without production data access
- Testing UI components with varied data scenarios
- Prototyping API designs and experimenting with response shapes
- Learning LLM integration patterns in .NET

**When NOT to use it:**
- In production environments serving real users
- For testing authentication/authorization logic (security schemes ignored)
- When data must conform to strict business rules or validation
- For load testing actual backend performance
- When deterministic, reproducible data is required across test runs

---

## Feature Documentation

For detailed guides with architecture diagrams, use cases, and implementation details:

- **[Docker Deployment Guide](./docs/DOCKER_GUIDE.md)** - Complete Docker setup and deployment
  - Quick start with Docker Compose + Ollama
  - End-to-end example with llm-model
  - Configuration methods (env vars, volume mapping, .env files)
  - GPU support, multi-backend setup
  - Production considerations and troubleshooting

- **[Backend API Reference](./docs/BACKEND_API_REFERENCE.md)** - Complete management endpoint documentation
  - OpenAPI management endpoints
  - API context management
  - gRPC proto management
  - SignalR hubs
  - Full request/response examples

- **[Multiple LLM Backends Guide](./docs/MULTIPLE_LLM_BACKENDS.md)** - Multiple provider support
  - Connect to Ollama, OpenAI, and LM Studio simultaneously
  - Per-request backend selection via headers/query params
  - Complete backward compatibility with legacy configs
  - Provider-specific examples and best practices

- **[API Contexts Guide](./docs/API-CONTEXTS.md)** - NEW!
  - Shared memory across requests for consistent multi-step workflows
  - E-commerce flows, stock tickers, game state examples
  - Token management and intelligent truncation
  - Mermaid architecture diagrams

- **[Rate Limiting & Batching Guide](./docs/RATE_LIMITING_BATCHING.md)** - NEW in v2.1.0!
  - N-completions support for generating multiple response variants
  - Per-endpoint statistics tracking with moving averages
  - Three batching strategies (Sequential, Parallel, Streaming)
  - Detailed timing headers for performance testing
  - Perfect for testing backoff strategies and timeout scenarios

- **[Pluggable Tools & Actions Guide](./docs/TOOLS_ACTIONS.md)** - NEW in v2.2.0!
  - MCP-compatible architecture for extensible tool integration
  - HTTP tools for calling external APIs with authentication
  - Mock tools for creating decision trees and workflows
  - Template substitution with environment variable support
  - Tool chaining with safety limits and result caching
  - Phase 1 (Explicit), Phase 2 (LLM-driven), Phase 3 (Advanced) roadmap

- **[gRPC Support Guide](./docs/GRPC_SUPPORT.md)** - NEW in v1.7.0!
  - Upload .proto files for automatic gRPC service mocking
  - Dual support: JSON over HTTP and binary Protobuf
  - LLM-powered realistic response generation
  - Complete implementation details and testing strategies

- **[OpenAPI Features Guide](./docs/OPENAPI-FEATURES.md)**
  - Dynamic spec loading from URLs, files, or inline JSON
  - Automatic endpoint generation with path parameters
  - Request/response examples with the Petstore API
  - SignalR integration for real-time updates

- **[Modular Architecture Examples](./MODULAR_EXAMPLES.md)**
  - REST-only, GraphQL-only, SignalR-only setups
  - Memory optimization strategies
  - Mix and match protocols

- **[SignalR Demo Guide](./SIGNALR_DEMO_GUIDE.md)**
  - Real-time data streaming setup
  - Context lifecycle management
  - Client connection examples

- **[Release Notes](./RELEASE_NOTES.md)**
  - Complete version history
  - Breaking changes (none since v1.0!)
  - Migration guides

- **[Test Summary](./TEST_SUMMARY.md)** - NEW in v2.3.0!
  - Complete test coverage documentation
  - 228 tests with detailed breakdown
  - Performance metrics and validation
  - All test categories explained

- **[Implementation Summary](./IMPLEMENTATION_SUMMARY.md)** - NEW in v2.3.0!
  - Form body and file upload implementation details
  - Manual JSON construction for .NET 10
  - Memory-safe file handling architecture
  - Complete code changes and rationale

---

## Quick Start

### Installation

**Main Package:**
```bash
dotnet add package mostlylucid.mockllmapi
```

**Testing Utilities (Optional):**
```bash
dotnet add package mostlylucid.mockllmapi.Testing
```
> Provides fluent API for easy HttpClient configuration in tests. [See Testing Section](#testing) for details.

### Quick Start with Docker (Recommended)

The fastest way to get started - no .NET or Ollama installation required!

```bash
# Clone the repository
git clone https://github.com/scottgal/LLMApi.git
cd LLMApi

# Start everything with Docker Compose (includes Ollama + llm-model)
docker compose up -d

# Wait for model download (first run only, ~4.7GB)
docker compose logs -f ollama

# Test the API
curl "http://localhost:5116/api/mock/users?shape={\"id\":0,\"name\":\"\",\"email\":\"\"}"
```

**That's it!** The API is running at `http://localhost:5116` with Ollama backend.

See the **[Complete Docker Guide](./docs/DOCKER_GUIDE.md)** for:
- Configuration options (env vars, volume mapping)
- Using different models (gemma3:4b, mistral-nemo)
- GPU support
- Production deployment
- Troubleshooting

### Prerequisites (Local Development)

If not using Docker:

1. Install [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later
2. Install [Ollama](https://ollama.ai/) and pull a model:
   ```bash
   ollama pull ministral-3:3b
   ```

### Choosing an LLM Model

This package was **developed and tested with `ministral-3:3b`** (3B parameters), which provides excellent results for all features with very fast performance. However, it works with any Ollama-compatible model:

#### Recommended Models

| Model                | Size | Speed     | Quality | Context | Best For |
|----------------------|------|-----------|---------|---------|----------|
| **ministral-3:3b** (default) | 3B   | V.Fast    | Excellent | 256K | **KILLER for JSON! Fast, accurate, huge context** |
| **gemma3:4b**        | 4B   | Fast      | Good | 4K | Alternative for lower-end machines |
| **llama3**           | 8B   | Medium    | Very Good | 8K | General use, production |
| **mistral-nemo**     | 12B  | Slower    | Excellent | 128K | **High quality, massive datasets** |
| **mistral:7b**       | 7B   | Medium    | Very Good | 8K | Alternative to llm-model |
| **phi3**             | 3.8B | Fast      | Good | 4K | Quick prototyping |
| **tinyllama**        | 1.1B | Very Fast | Basic | 2K | Ultra resource-constrained |

#### RECOMMENDED: Qwen 2.5 Coder (3B) - Perfect for Development!

**Qwen 2.5 Coder is KILLER for JSON generation** - ultra-fast, highly accurate, large context:

```bash
ollama pull ministral-3:3b
```

```json
{
  "MockLlmApi": {
    "ModelName": "ministral-3:3b",
    "Temperature": 1.2,
    "MaxInputTokens": 8192
  }
}
```

**Why it's great:**
- Exceptionally fast on any hardware (3-4GB RAM)
- Best-in-class JSON generation accuracy
- 256K context window (handles complex nested structures)
- Trained specifically for code/structured data
- Perfect for CI/CD pipelines and development
- Minimal hallucinations compared to general models

#### PRODUCTION: Mistral-Nemo - Best Quality & Massive Contexts

**For production-like testing with complex schemas:**

```bash
ollama pull mistral-nemo
```

```json
{
  "MockLlmApi": {
    "ModelName": "mistral-nemo",
    "Temperature": 1.2,
    "MaxInputTokens": 8000
  }
}
```

**Why it's great:**
- Highest quality realistic data generation
- **128K context window** (requires [Ollama configuration](./docs/MULTIPLE_LLM_BACKENDS.md#ollama-context-window-configuration))
- Best for complex nested structures and large datasets
- More creative variation in generated data
- **Note**: Requires more resources (12-16GB RAM)

#### Model-Specific Configuration

**For ministral-3:3b (Recommended - default):**
```json
{
  "ModelName": "ministral-3:3b",
  "Temperature": 1.2,
  "MaxContextWindow": 262144   // 256K context window
}
```

**For gemma3:4b or llama3:**
```json
{
  "ModelName": "llm-model",     // or "mistral:7b"
  "Temperature": 1.2,
  "MaxContextWindow": 8192   // Set to model's context window size
}
```

**For mistral-nemo (High-quality production):**
```json
{
  "ModelName": "mistral-nemo",
  "Temperature": 1.2,
  "MaxContextWindow": 32768, // Or 128000 if configured in Ollama
  "TimeoutSeconds": 120      // Longer timeout for large contexts
}
```
**Note:** Mistral-nemo requires [Ollama context configuration](./docs/MULTIPLE_LLM_BACKENDS.md#ollama-context-window-configuration) for 128K contexts.

**Where to find MaxContextWindow:**
```bash
# Check model info
ollama show {model-name}

# Look for "context_length" or "num_ctx" parameter
# Example output: "context_length": 8192
```

**For smaller models (phi3, tinyllama):**
```json
{
  "ModelName": "tinyllama",
  "Temperature": 0.7         // Lower temperature for stability
}
```

**Why Temperature Matters:**
- **Larger models (7B+)** can handle high temperatures (1.0-1.5) while maintaining valid JSON
- **Smaller models (<4B)** need lower temperatures (0.6-0.8) to avoid:
  - Invalid JSON syntax (missing quotes, brackets)
  - Truncated responses with ellipsis ("...")
  - Hallucinated field names or structures
- Lower temperature = more predictable output, less variety
- Higher temperature = more creative output, more variety (but riskier for small models)

#### Installation

```bash
# RECOMMENDED for development (fastest, most accurate JSON)
ollama pull ministral-3:3b

# Alternative options
ollama pull gemma3:4b       # Good for low-end machines
ollama pull llama3          # General purpose, good balance
ollama pull mistral-nemo    # Highest quality (requires more RAM)

# Alternative options
ollama pull mistral:7b
ollama pull phi3
```

**Important Limitations:**
- **Smaller models** (`tinyllama`, `phi3`) work but may:
  - Generate simpler/less varied data
  - Struggle with complex GraphQL queries
  - Need more retry attempts
  - Work best with simple queries and small response sizes
- **All models** can struggle with:
  - **Very complex/deeply nested GraphQL queries** (>5 levels deep)
  - **Many fields per object** (>10 fields)
  - **Large array requests** - Limit to 2-5 items for reliability
- **If seeing errors** about truncated JSON ("...") or comments ("//"):
  - Lower temperature to 0.8 or below
  - Simplify your GraphQL query (fewer fields, less nesting)
  - Increase `MaxRetryAttempts` to 5 or more

### Basic Usage

**Program.cs:**
```csharp
using mostlylucid.mockllmapi;

var builder = WebApplication.CreateBuilder(args);

// Add LLMock API services (all protocols: REST, GraphQL, SSE)
builder.Services.AddLLMockApi(builder.Configuration);

var app = builder.Build();

// Map mock endpoints at /api/mock (includes REST, GraphQL, SSE)
app.MapLLMockApi("/api/mock");

app.Run();
```

**appsettings.json:**
```json
{
  "mostlylucid.mockllmapi": {
    "BaseUrl": "http://localhost:11434/v1/",
    "ModelName": "ministral-3:3b",
    "Temperature": 1.2
  }
}
```

That's it! Now all requests to `/api/mock/**` return intelligent mock data.

## Configuration Options

> **📘 Complete Configuration Reference:** See [Configuration Reference Guide](./docs/CONFIGURATION_REFERENCE.md) for all options
> **📄 Full Example:** See [appsettings.Full.json](./LLMApi/appsettings.Full.json) - demonstrates **every** configuration option

### Via appsettings.json (Recommended)

```json
{
  "mostlylucid.mockllmapi": {
    "BaseUrl": "http://localhost:11434/v1/",
    "ModelName": "ministral-3:3b",
    "Temperature": 1.2,
    "TimeoutSeconds": 30,
    "EnableVerboseLogging": false,
    "CustomPromptTemplate": null,

    // Token Management (NEW in v1.5.0)
    "MaxInputTokens": 8192,  // Ministral has 256K context

    // Resilience Policies (enabled by default)
    "EnableRetryPolicy": true,
    "MaxRetryAttempts": 3,
    "RetryBaseDelaySeconds": 1.0,
    "EnableCircuitBreaker": true,
    "CircuitBreakerFailureThreshold": 5,
    "CircuitBreakerDurationSeconds": 30
  }
}
```

**Model-Specific Token Limits**: See `LLMApi/appsettings.json` for configuration examples for different models (Llama 3, TinyLlama, Mistral, etc.). Each model has different context window sizes - adjust `MaxInputTokens` accordingly.

**API Contexts**: For detailed information about using contexts to maintain consistency across requests, see the **[API Contexts Guide](./docs/API-CONTEXTS.md)**.

### Resilience Policies

**New in v1.2.0:** Built-in Polly resilience policies protect your application from LLM service failures!

The package includes two resilience patterns enabled by default:

**Exponential Backoff Retry**
- Automatically retries failed LLM requests with exponential delays (1s, 2s, 4s...)
- Includes jitter to prevent thundering herd problems
- Handles connection errors, timeouts, and non-success status codes
- Default: 3 attempts with 1 second base delay

**Circuit Breaker**
- Opens after consecutive failures to prevent cascading failures
- Stays open for a configured duration before allowing test requests
- Three states: Closed (normal), Open (rejecting), Half-Open (testing)
- Default: Opens after 5 consecutive failures, stays open for 30 seconds

**Configuration:**

```json
{
  "mostlylucid.mockllmapi": {
    // Enable/disable retry policy
    "EnableRetryPolicy": true,
    "MaxRetryAttempts": 3,
    "RetryBaseDelaySeconds": 1.0,  // Actual delays: 1s, 2s, 4s (exponential)

    // Enable/disable circuit breaker
    "EnableCircuitBreaker": true,
    "CircuitBreakerFailureThreshold": 5,  // Open after 5 consecutive failures
    "CircuitBreakerDurationSeconds": 30   // Stay open for 30 seconds
  }
}
```

**Logging:**

The resilience policies log all retry attempts and circuit breaker state changes:

```
[Warning] LLM request failed (attempt 2/4). Retrying in 2000ms. Error: Connection refused
[Error] Circuit breaker OPENED after 5 consecutive failures. All LLM requests will be rejected for 30 seconds
[Information] Circuit breaker CLOSED. LLM requests will be attempted normally
```

**When to Adjust:**

- **Slow LLM?** Increase `MaxRetryAttempts` or `RetryBaseDelaySeconds`
- **Aggressive recovery?** Reduce `CircuitBreakerDurationSeconds`
- **Many transient errors?** Increase `CircuitBreakerFailureThreshold`
- **Disable for local testing?** Set both `EnableRetryPolicy` and `EnableCircuitBreaker` to `false`

### Via Code

```csharp
builder.Services.Addmostlylucid.mockllmapi(options =>
{
    options.BaseUrl = "http://localhost:11434/v1/";
    options.ModelName = "mixtral";
    options.Temperature = 1.5;
    options.TimeoutSeconds = 60;
});
```

### Custom Endpoint Patterns

```csharp
// Default: /api/mock/** and /api/mock/stream/**
app.Mapmostlylucid.mockllmapi("/api/mock");

// Custom pattern
app.Mapmostlylucid.mockllmapi("/demo");
// Creates: /demo/** and /demo/stream/**

// Without streaming
app.Mapmostlylucid.mockllmapi("/api/mock", includeStreaming: false);
```

## Usage Examples

### Basic Request

```bash
curl http://localhost:5000/api/mock/users?limit=5
```

Returns realistic user data generated by the LLM.

### Form Body Requests (NEW in v2.3.0)

**HTML Form Submission:**
```bash
curl -X POST http://localhost:5000/api/mock/users/register \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "username=john_doe&email=john@example.com&age=30"
```

The form data is automatically converted to JSON and passed to the LLM for realistic response generation.

**Form with Multiple Values:**
```bash
curl -X POST http://localhost:5000/api/mock/posts \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "title=My Post&tags=tech&tags=programming&tags=llm"
```

Multiple values for the same field name are converted to arrays automatically.

### File Upload Requests (NEW in v2.3.0)

**Single File Upload:**
```bash
curl -X POST http://localhost:5000/api/mock/photos/upload \
  -F "title=My Photo" \
  -F "description=Beautiful sunset" \
  -F "image=@photo.jpg"
```

**Multiple Files with Form Data:**
```bash
curl -X POST http://localhost:5000/api/mock/documents/bulk \
  -F "title=Multiple uploads" \
  -F "file1=@document1.pdf" \
  -F "file2=@document2.pdf"
```

**How File Uploads Work:**
- File content is **streamed through an 8KB buffer** and **dumped** (not stored in memory)
- Only metadata is retained: filename, content type, size, bytes read
- LLM receives file metadata and generates appropriate response
- Memory-safe for large files (tested with 100MB+)

**Example Response:**
```json
{
  "message": "Files uploaded successfully",
  "uploads": [
    {
      "fieldName": "image",
      "fileName": "photo.jpg",
      "contentType": "image/jpeg",
      "size": 524288,
      "processed": true
    }
  ]
}
```

### Deep Path Nesting (NEW in v2.3.0)

**Complex Nested Paths:**
```bash
curl "http://localhost:5000/api/mock/v1/api/products/electronics/computers/laptops/gaming/high-end/2024/details?brand=Dell&model=XPS15"
```

The LLM incorporates all path segments and query parameters into realistic response generation. No practical limit on path depth.

### With API Context (NEW in v1.5.0)

**Use contexts to maintain consistency across multiple related requests:**

```bash
# Step 1: Create a user
curl "http://localhost:5000/api/mock/users?context=checkout-flow"

# Step 2: Create order for that user (LLM references user from context)
curl "http://localhost:5000/api/mock/orders?context=checkout-flow"

# Step 3: Add payment (LLM references both user and order)
curl "http://localhost:5000/api/mock/payments?context=checkout-flow"
```

Each request in the same context sees the previous requests, ensuring consistent IDs, names, and data relationships. Perfect for multi-step workflows! See the **[API Contexts Guide](./docs/API-CONTEXTS.md)** for complete examples.

### With Shape Control

```bash
curl -X POST http://localhost:5000/api/mock/orders \
  -H "X-Response-Shape: {\"orderId\":\"string\",\"total\":0.0,\"items\":[{\"sku\":\"string\",\"qty\":0}]}" \
  -H "Content-Type: application/json" \
  -d '{"customerId":"cus_123"}'
```

LLM generates data matching your exact shape specification.

### Streaming (SSE - Server-Sent Events)

**SSE streaming is part of the REST API - just enable it when mapping endpoints:**

```csharp
// SSE streaming is automatically available at /api/mock/stream/**
app.MapLLMockApi("/api/mock", includeStreaming: true);
```

**Usage:**

```bash
curl -N http://localhost:5000/api/mock/stream/products?category=electronics \
  -H "Accept: text/event-stream"
```

Returns Server-Sent Events as JSON is generated token-by-token:
```
data: {"chunk":"{","done":false}
data: {"chunk":"\"id\"","done":false}
data: {"chunk":":","done":false}
data: {"chunk":"123","done":false}
...
data: {"content":"{\"id\":123,\"name\":\"Product\"}","done":true,"schema":"{...}"}
```

**JavaScript Example:**
```javascript
const eventSource = new EventSource('/api/mock/stream/users?limit=5');

eventSource.onmessage = (event) => {
    const data = JSON.parse(event.data);

    if (data.done) {
        console.log('Complete:', data.content);
        eventSource.close();
    } else {
        console.log('Chunk:', data.chunk);
    }
};
```

**With Shape Control:**
```bash
curl -N "http://localhost:5000/api/mock/stream/orders?shape=%7B%22id%22%3A0%2C%22items%22%3A%5B%5D%7D"
```

The streaming endpoint supports all the same features as regular endpoints:
- Shape control (query param, header, or body)
- JSON Schema support
- Custom prompts
- All HTTP methods (GET, POST, PUT, DELETE, PATCH)

## GraphQL API Mocking

**New in v1.2.0:** Native GraphQL support with query-driven mock data generation!

LLMock API includes built-in GraphQL endpoint support. Unlike REST endpoints where you specify shapes separately, GraphQL queries naturally define the exact structure they expect - the query IS the shape.

### Quick Start with GraphQL

The GraphQL endpoint is automatically available when you map the LLMock API:

```csharp
app.MapLLMockApi("/api/mock", includeGraphQL: true); // GraphQL enabled by default
```

This creates a GraphQL endpoint at `/api/mock/graphql`.

### Basic Usage

**Simple Query:**
```bash
curl -X POST http://localhost:5000/api/mock/graphql \
  -H "Content-Type: application/json" \
  -d '{"query": "{ users { id name email role } }"}'
```

**Response:**
```json
{
  "data": {
    "users": [
      { "id": 1, "name": "Alice Johnson", "email": "alice@example.com", "role": "admin" },
      { "id": 2, "name": "Bob Smith", "email": "bob@example.com", "role": "user" }
    ]
  }
}
```

### With Variables

```bash
curl -X POST http://localhost:5000/api/mock/graphql \
  -H "Content-Type: application/json" \
  -d '{
    "query": "query GetUser($userId: ID!) { user(id: $userId) { id name email } }",
    "variables": { "userId": "12345" },
    "operationName": "GetUser"
  }'
```

### Nested Queries

GraphQL's power shines with nested data:

```graphql
{
  company {
    name
    employees {
      id
      firstName
      lastName
      department {
        name
        location
      }
      projects {
        id
        title
        status
        milestones {
          title
          dueDate
          completed
        }
      }
    }
  }
}
```

The LLM generates realistic data matching your exact query structure - including all nested relationships.

### JavaScript Client Example

```javascript
async function fetchGraphQL(query, variables = {}) {
    const response = await fetch('/api/mock/graphql', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ query, variables })
    });

    const result = await response.json();

    if (result.errors) {
        console.error('GraphQL errors:', result.errors);
    }

    return result.data;
}

// Usage
const data = await fetchGraphQL(`
    query GetProducts($category: String) {
        products(category: $category) {
            id
            name
            price
            inStock
            reviews {
                rating
                comment
            }
        }
    }
`, { category: 'electronics' });
```

### Error Handling

GraphQL errors are returned in standard format:

```json
{
  "data": null,
  "errors": [
    {
      "message": "Invalid GraphQL request format",
      "extensions": {
        "code": "INTERNAL_SERVER_ERROR"
      }
    }
  ]
}
```

### How It Works

1. **Parse Request**: Extract GraphQL query, variables, and operation name
2. **Build Prompt**: Send the query structure to the LLM with instructions to generate matching data
3. **Generate Data**: LLM creates realistic data that exactly matches the query fields
4. **Wrap Response**: Returns data in GraphQL format: `{ "data": {...} }`

### Key Advantages

- **No Shape Specification Needed**: The GraphQL query defines the structure
- **Type Safety**: Queries explicitly request fields by name
- **Nested Relationships**: Natural support for complex, nested data structures
- **Standard Format**: Works with any GraphQL client library
- **Realistic Data**: LLM generates contextually appropriate data for each field

### Testing GraphQL

Use the included `LLMApi.http` file which contains 5 ready-to-use GraphQL examples:
- Simple user query
- Query with variables
- Nested fields with arrays
- E-commerce product catalog
- Complex organizational data

See the [GraphQL examples in LLMApi.http](LLMApi/LLMApi.http#L229-L294) for complete working examples.

### GraphQL Configuration

#### Token Limits and Model Selection

GraphQL responses can become large with deeply nested queries. To prevent JSON truncation errors, configure the `GraphQLMaxTokens` option:

```json
{
  "MockLlmApi": {
    "GraphQLMaxTokens": 300  // Recommended: 200-300 for reliability
  }
}
```

**Token Limit Guidelines:**

| Model | Recommended Max Tokens | Notes |
|-------|----------------------|-------|
| **llm-model** | 300-500 | Best balance of speed and complexity |
| **mistral:7b** | 300-500 | Handles nested structures well |
| **phi3** | 200-300 | Keep queries simple |
| **tinyllama** | 150-200 | Use shallow queries only |

**Why Lower Is Better:**
- The prompt prioritizes **correctness over length**
- Lower limits force the LLM to generate simpler, complete JSON
- Higher limits risk truncated responses (invalid JSON)
- If you see "Invalid JSON from LLM" errors, **reduce GraphQLMaxTokens**

**For Complex Nested Queries:**
1. Use larger models (llm-model, mistral:7b)
2. Increase GraphQLMaxTokens to 500-1000
3. Keep array sizes small (2 items max by default)
4. Monitor logs for truncation warnings

**Example configuration for complex queries:**
```json
{
  "MockLlmApi": {
    "ModelName": "llm-model",           // Larger model
    "GraphQLMaxTokens": 800,          // Higher limit for nested data
    "Temperature": 1.2
  }
}
```

## SignalR Real-Time Data Streaming

LLMock API includes optional SignalR support for continuous, real-time mock data generation. This is perfect for:
- Dashboard prototypes requiring live updates
- Testing real-time UI components
- Demos with constantly changing data
- WebSocket/SignalR integration testing

### Quick Start with SignalR

**SignalR works independently - you don't need the REST API endpoints to use SignalR streaming.**

**1. Minimal SignalR-only setup:**

```csharp
using mostlylucid.mockllmapi;

var builder = WebApplication.CreateBuilder(args);

// Add SignalR services (no REST API needed!)
builder.Services.AddLLMockSignalR(builder.Configuration);

var app = builder.Build();

app.UseRouting();

// Map SignalR hub and management endpoints
app.MapLLMockSignalR("/hub/mock", "/api/mock");

app.Run();
```

**Optional: Add REST API too**

If you also want the REST API endpoints, add these lines:

```csharp
// Add core LLMock API services (optional)
builder.Services.AddLLMockApi(builder.Configuration);

// Map REST API endpoints (optional)
app.MapLLMockApi("/api/mock", includeStreaming: true);
```

**2. Configure in appsettings.json:**

```json
{
  "MockLlmApi": {
    "BaseUrl": "http://localhost:11434/v1/",
    "ModelName": "ministral-3:3b",
    "Temperature": 1.2,

    "SignalRPushIntervalMs": 5000,
    "HubContexts": [
      {
        "Name": "weather",
        "Description": "Weather data with temperature, condition, humidity, and wind speed"
      },
      {
        "Name": "stocks",
        "Description": "Stock market data with symbol, current price, change percentage, and trading volume"
      }
    ]
  }
}
```

**3. Connect from client:**

```javascript
// Using @microsoft/signalr
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hub/mock")
    .withAutomaticReconnect()
    .build();

// Subscribe to a context
connection.on("DataUpdate", (message) => {
    console.log(`${message.context}:`, message.data);
    // message.data contains generated JSON matching the shape
    // message.timestamp is unix timestamp in ms
});

await connection.start();
await connection.invoke("SubscribeToContext", "weather");
```

### Hub Context Configuration

Each hub context simulates a complete API request and generates data continuously:

```json
{
  "Name": "orders",                 // Context name (SignalR group identifier)
  "Description": "Order data..."    // Plain English description (LLM generates JSON from this)
  // Optional:
  // "IsActive": true,              // Start in active/stopped state (default: true)
  // "Shape": "{...}",              // Explicit JSON shape or JSON Schema
  // "IsJsonSchema": false           // Auto-detected if not specified
}
```

**Recommended: Use Plain English Descriptions**

Let the LLM automatically generate appropriate JSON structures:

```json
{
  "Name": "sensors",
  "Description": "IoT sensor data with device ID, temperature, humidity, battery level, and last reading timestamp"
}
```

The LLM automatically generates an appropriate JSON schema from your description - no manual Shape required!

### Dynamic Context Creation API

Create and manage SignalR contexts at runtime using the management API:

#### Create Context

```bash
POST /api/mock/contexts
Content-Type: application/json

{
  "name": "crypto",
  "description": "Cryptocurrency prices with symbol, USD price, 24h change percentage, and market cap"
}
```

Response:
```json
{
  "message": "Context 'crypto' registered successfully",
  "context": {
    "name": "crypto",
    "description": "Cryptocurrency prices...",
    "method": "GET",
    "path": "/crypto",
    "shape": "{...generated JSON schema...}",
    "isJsonSchema": true
  }
}
```

#### List All Contexts

```bash
GET /api/mock/contexts
```

Response:
```json
{
  "contexts": [
    {
      "name": "weather",
      "description": "Realistic weather data with temperature, conditions, humidity, and wind speed for a single location",
      "method": "GET",
      "path": "/weather/current",
      "shape": "{...}"
    },
    {
      "name": "crypto",
      "description": "Cryptocurrency prices...",
      "shape": "{...}"
    }
  ],
  "count": 2
}
```

Note: The list endpoint merges contexts configured in appsettings.json with any dynamically created contexts at runtime. Descriptions from appsettings are included even if those contexts have not yet been dynamically registered.

#### Get Specific Context

```bash
GET /api/mock/contexts/weather
```

Response:
```json
{
  "name": "weather",
  "method": "GET",
  "path": "/weather/current",
  "shape": "{\"temperature\":0,\"condition\":\"string\"}",
  "isJsonSchema": false
}
```

#### Delete Context

```bash
DELETE /api/mock/contexts/crypto
```

Response:
```json
{
  "message": "Context 'crypto' deleted successfully"
}
```

#### Start Context (Resume Data Generation)

```bash
POST /api/mock/contexts/crypto/start
```

Response:
```json
{
  "message": "Context 'crypto' started successfully"
}
```

Starts generating data for a stopped context without affecting connected clients.

#### Stop Context (Pause Data Generation)

```bash
POST /api/mock/contexts/crypto/stop
```

Response:
```json
{
  "message": "Context 'crypto' stopped successfully"
}
```

Stops generating new data but keeps the context registered. Clients remain connected but receive no updates until started again.

### Complete Client Example

```html
<!DOCTYPE html>
<html>
<head>
    <script src="https://cdn.jsdelivr.net/npm/@microsoft/signalr@8.0.0/dist/browser/signalr.min.js"></script>
</head>
<body>
    <h1>Live Weather Data</h1>
    <div id="weather-data"></div>

    <script>
        const connection = new signalR.HubConnectionBuilder()
            .withUrl("/hub/mock")
            .withAutomaticReconnect()
            .build();

        connection.on("DataUpdate", (message) => {
            if (message.context === "weather") {
                const weatherDiv = document.getElementById("weather-data");
                weatherDiv.innerHTML = `
                    <h2>Current Weather</h2>
                    <p>Temperature: ${message.data.temperature}°F</p>
                    <p>Condition: ${message.data.condition}</p>
                    <p>Humidity: ${message.data.humidity}%</p>
                    <p>Updated: ${new Date(message.timestamp).toLocaleTimeString()}</p>
                `;
            }
        });

        connection.start()
            .then(() => {
                console.log("Connected to SignalR hub");
                return connection.invoke("SubscribeToContext", "weather");
            })
            .then(() => {
                console.log("Subscribed to weather context");
            })
            .catch(err => console.error(err));
    </script>
</body>
</html>
```

### Dynamic Context Creation from UI

```javascript
async function createDynamicContext() {
    // Create the context
    const response = await fetch("/api/mock/contexts", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
            name: "stocks",
            description: "Stock market data with ticker symbol, current price, daily change percentage, and trading volume"
        })
    });

    const result = await response.json();
    console.log("Context created:", result.context);

    // Subscribe to receive data
    await connection.invoke("SubscribeToContext", "stocks");
    console.log("Now receiving live stock data!");
}
```

### SignalR Hub Methods

The `MockLlmHub` supports the following methods:

**SubscribeToContext(string context)**
- Subscribes the client to receive data updates for a specific context
- Client will receive `DataUpdate` events with generated data
- **New in v1.1.0:** Automatically increments the context's connection count

**UnsubscribeFromContext(string context)**
- Unsubscribes the client from a context
- Client will no longer receive updates for that context
- **New in v1.1.0:** Automatically decrements the context's connection count

**Events received by client:**

**DataUpdate** - Contains generated mock data
```javascript
{
    context: "weather",       // Context name
    method: "GET",            // Simulated HTTP method
    path: "/weather/current", // Simulated path
    timestamp: 1699564820000, // Unix timestamp (ms)
    data: {                   // Generated JSON matching the shape
        temperature: 72,
        condition: "Sunny",
        humidity: 45,
        windSpeed: 8
    }
}
```

**Subscribed** - Confirmation of subscription
```javascript
{
    context: "weather",
    message: "Subscribed to weather"
}
```

**Unsubscribed** - Confirmation of unsubscription
```javascript
{
    context: "weather",
    message: "Unsubscribed from weather"
}
```

### Configuration Options

```json
{
  "MockLlmApi": {
    "SignalRPushIntervalMs": 5000,  // Interval between data pushes (ms)
    "HubContexts": [...]             // Array of pre-configured contexts
  }
}
```

### JSON Schema Support

Hub contexts support both simple JSON shapes and full JSON Schema:

**Simple Shape:**
```json
{
  "Name": "users",
  "Shape": "{\"id\":0,\"name\":\"string\",\"email\":\"string\"}"
}
```

**JSON Schema:**
```json
{
  "Name": "products",
  "Shape": "{\"type\":\"object\",\"properties\":{\"id\":{\"type\":\"number\"},\"name\":{\"type\":\"string\"},\"price\":{\"type\":\"number\"}},\"required\":[\"id\",\"name\",\"price\"]}",
  "IsJsonSchema": true
}
```

The system auto-detects JSON Schema by looking for `$schema`, `type`, or `properties` fields.

### Architecture

```mermaid
graph TD
    Client[SignalR Client] -->|Subscribe| Hub[MockLlmHub]
    Hub -->|Join Group| Group[SignalR Group]
    BG[Background Service] -->|Generate Data| LLM[Ollama LLM]
    LLM -->|JSON Response| BG
    BG -->|Push Data| Group
    Group -->|DataUpdate Event| Client
    API[Management API] -->|CRUD| Manager[DynamicHubContextManager]
    Manager -->|Register/Unregister| BG
```

**Components:**
- **MockLlmHub**: SignalR hub handling client connections and subscriptions
- **MockDataBackgroundService**: Hosted service continuously generating data
- **DynamicHubContextManager**: Thread-safe manager for runtime context registration
- **HubContextConfig**: Configuration model for each data context

### Use Cases

**1. Dashboard Prototyping**
```javascript
// Subscribe to multiple data sources
await connection.invoke("SubscribeToContext", "sales");
await connection.invoke("SubscribeToContext", "traffic");
await connection.invoke("SubscribeToContext", "alerts");
// Now receiving live updates for all three!
```

**2. IoT Simulation**
```json
{
  "Name": "sensors",
  "Description": "IoT temperature sensors with device ID, current temperature, battery percentage, and signal strength",
  "Path": "/iot/sensors"
}
```

**3. Financial Data**
```json
{
  "Name": "trading",
  "Description": "Real-time stock trades with timestamp, symbol, price, volume, and buyer/seller IDs",
  "Path": "/trading/live"
}
```

**4. Gaming Leaderboard**
```json
{
  "Name": "leaderboard",
  "Description": "Gaming leaderboard with player name, score, rank, level, and country",
  "Path": "/game/leaderboard"
}
```

### Context Lifecycle Management

**New in v1.1.0:** Full lifecycle control over SignalR contexts with real-time status tracking!

Each context has the following properties:
- **IsActive**: Whether the context is generating data (default: true)
- **ConnectionCount**: Number of currently connected clients (auto-tracked)

Contexts can be in two states:
- **Active**: Background service generates new data every `SignalRPushIntervalMs` (default: 5 seconds)
- **Stopped**: Context remains registered, clients stay connected, but no new data is generated

**Key Features:**
- Start/stop contexts without disconnecting clients
- Real-time connection count tracking
- Status badges showing active/stopped state
- Duplicate context prevention
- Contexts from appsettings.json automatically appear in the UI

**Response Caching for SignalR:**
**New in v1.1.0:** Intelligent caching reduces LLM load and improves consistency!

- **Batch Prefilling**: Generates 5-10 responses at once (configurable via `MaxCachePerKey`)
- **Per-Context Queues**: Each context maintains its own cache of pre-generated responses
- **Background Refilling**: Automatically refills cache when it drops below 50% capacity
- **Smart Scheduling**: Minimizes LLM calls while maintaining data freshness

This significantly reduces LLM load for high-frequency contexts, especially with multiple clients.

**Example Workflow:**
```bash
# Create a context
POST /api/mock/contexts
{ "name": "metrics", "description": "Server metrics" }

# Stop data generation (clients remain connected)
POST /api/mock/contexts/metrics/stop

# Resume data generation
POST /api/mock/contexts/metrics/start

# Remove context entirely
DELETE /api/mock/contexts/metrics
```

## OpenAPI / Swagger Mock Generation

**New Feature:** Automatically generate mock endpoints from OpenAPI/Swagger specifications! Point to any OpenAPI 3.0/Swagger 2.0 spec (URL or file) and the library will create mock endpoints for all defined operations.

### Quick Start with OpenAPI

```bash
dotnet add package mostlylucid.mockllmapi
```

**Program.cs:**
```csharp
using mostlylucid.mockllmapi;

var builder = WebApplication.CreateBuilder(args);

// Add OpenAPI mock services
builder.Services.AddLLMockOpenApi(builder.Configuration);

var app = builder.Build();
app.UseRouting();

// Map OpenAPI-based mock endpoints
app.MapLLMockOpenApi();

app.Run();
```

**appsettings.json:**
```json
{
  "MockLlmApi": {
    "BaseUrl": "http://localhost:11434/v1/",
    "ModelName": "ministral-3:3b",
    "Temperature": 1.2,
    "OpenApiSpecs": [
      {
        "Name": "petstore",
        "Source": "https://petstore3.swagger.io/api/v3/openapi.json",
        "BasePath": "/petstore",
        "EnableStreaming": false
      },
      {
        "Name": "myapi",
        "Source": "./specs/my-api.yaml",
        "BasePath": "/api/v1"
      }
    ]
  }
}
```

That's it! All endpoints from your OpenAPI spec are now available as intelligent LLM-powered mocks.

### How It Works

1. **Spec Loading**: Loads OpenAPI specs from URLs or local files (JSON or YAML)
2. **Schema Conversion**: Converts OpenAPI schemas to JSON shape templates
3. **Endpoint Mapping**: Automatically maps all operations to ASP.NET Core endpoints
4. **LLM Generation**: Uses LLM to generate realistic mock data matching the schema
5. **Path Parameters**: Handles path parameters like `/users/{id}` automatically

### Configuration Options

Each OpenAPI spec supports these configuration options:

| Property | Type | Description |
|----------|------|-------------|
| `Name` | string | Unique identifier for this spec (required) |
| `Source` | string | URL or file path to OpenAPI spec (required) |
| `BasePath` | string | Override base path (default: uses spec's `servers[0].url`) |
| `EnableStreaming` | bool | Add `/stream` suffix for SSE streaming (default: false) |
| `IncludeTags` | string[] | Only generate endpoints with these tags |
| `ExcludeTags` | string[] | Skip endpoints with these tags |
| `IncludePaths` | string[] | Only generate these paths (supports wildcards like `/users/*`) |
| `ExcludePaths` | string[] | Skip these paths (supports wildcards) |

### Advanced Configuration Examples

**Filter by tags:**
```json
{
  "Name": "petstore",
  "Source": "https://petstore3.swagger.io/api/v3/openapi.json",
  "IncludeTags": ["pet", "store"]
}
```

**Filter by paths:**
```json
{
  "Name": "api",
  "Source": "./specs/api.yaml",
  "IncludePaths": ["/users/*", "/products/*"],
  "ExcludePaths": ["/admin/*"]
}
```

**Enable streaming:**
```json
{
  "Name": "api",
  "Source": "./specs/api.yaml",
  "BasePath": "/api",
  "EnableStreaming": true
}
```

### Testing OpenAPI Mocks

Given this OpenAPI spec configuration:

```json
{
  "Name": "petstore",
  "Source": "./specs/petstore.json",
  "BasePath": "/petstore"
}
```

And a spec defining `GET /pet/{petId}` that returns a `Pet` object, you can test:

```bash
# Get a pet by ID
curl http://localhost:5116/petstore/pet/123

# Response (generated by LLM based on Pet schema):
{
  "id": 123,
  "name": "Fluffy",
  "category": {
    "id": 1,
    "name": "Cats"
  },
  "photoUrls": ["https://example.com/fluffy.jpg"],
  "tags": [
    {"id": 1, "name": "cute"},
    {"id": 2, "name": "playful"}
  ],
  "status": "available"
}
```

### Modular Usage

OpenAPI mocks work independently of REST/GraphQL/SignalR:

```csharp
// Just OpenAPI mocks
builder.Services.AddLLMockOpenApi(builder.Configuration);
app.MapLLMockOpenApi();

// Or combine with other features
builder.Services.AddLLMockRest(builder.Configuration);
builder.Services.AddLLMockOpenApi(builder.Configuration);
app.MapLLMockRest("/api/mock");
app.MapLLMockOpenApi();
```

### Supported Spec Formats

- **OpenAPI 3.0** (JSON or YAML)
- **OpenAPI 3.1** (JSON or YAML)
- **Swagger 2.0** (JSON or YAML)

### Key Features

- **Automatic Endpoint Generation**: All paths and operations from spec become mock endpoints
- **Schema-Driven**: Response shapes derived from OpenAPI response schemas
- **Path Parameters**: Handles `/users/{id}`, `/posts/{postId}/comments/{commentId}`, etc.
- **Multiple Specs**: Load and mount multiple API specs simultaneously
- **Smart Filtering**: Include/exclude operations by tags or paths
- **Streaming Support**: Optional SSE streaming for any endpoint
- **LLM-Powered**: Generates realistic, varied data appropriate for each schema

### Use Cases

- **API-First Development**: Start coding against spec before backend is ready
- **Frontend Prototyping**: Develop UI while API is being built
- **Integration Testing**: Test client code without real API dependencies
- **Demo Environments**: Showcase apps without production API access
- **Contract Testing**: Verify clients handle all schema variations correctly
- **Load Testing**: Generate realistic test data at scale

### Limitations

- Currently supports response schemas only (request validation not enforced)
- Security schemes are ignored (no authentication required)
- Only application/json content type supported for responses
- Path parameter values are not validated against schema constraints

### Testing OpenAPI Management

The included `management.http` file contains comprehensive examples for all management endpoints:

**Spec Management**:
- Load spec from URL (Petstore, GitHub API, etc.)
- Load spec from local file
- Load spec from raw JSON/YAML
- List all loaded specs
- Get spec details with endpoints
- Reload spec (refresh from source)
- Delete spec

**Endpoint Testing**:
- Test GET endpoints with path parameters
- Test POST/PUT/DELETE endpoints
- Test endpoints with query parameters
- Test multiple endpoints in sequence

**Advanced Workflows**:
- Load multiple specs simultaneously
- Compare OpenAPI mocks vs regular mocks
- Batch operations
- Error handling examples

**Example from management.http**:
```http
### Load Petstore API
POST http://localhost:5116/api/openapi/specs
Content-Type: application/json

{
  "name": "petstore",
  "source": "https://petstore3.swagger.io/api/v3/openapi.json",
  "basePath": "/petstore"
}

### Test an endpoint
POST http://localhost:5116/api/openapi/test
Content-Type: application/json

{
  "specName": "petstore",
  "path": "/pet/123",
  "method": "GET"
}
```

See the complete [management.http](LLMApi/management.http) file for 20+ ready-to-use examples.

### Demo Applications

The package includes complete demo applications with interactive interfaces featuring full context management:

#### Windows Desktop Client (`LLMockApiClient/`) — WPF Application (In Development)

> **⚠️ DEVELOPMENT STATUS**: The Windows desktop client is currently under active development. While many features are functional, some functionality may be incomplete or subject to change. Use for testing and development purposes.

A comprehensive WPF desktop application for interacting with the LLMock API:

**Features:**
- **Dashboard**: Real-time connection status and system monitoring
- **SignalR Real-Time**: Create contexts and subscribe to live data streams
- **SSE Streaming**: Three streaming modes (tokens, objects, array items)
- **OpenAPI Manager**: Load specs from URL/JSON, test endpoints
- **gRPC Services**: Upload .proto files, test gRPC methods
- **Play with APIs**: Interactive HTTP testing playground
- **Multi-Backend Support**: Configure and switch between multiple backends
- **Traffic Monitor**: Live HTTP request/response logging
- **Dark/Light Theme**: Toggle between themes with smooth transitions

**Documentation:** [LLMockApiClient README](./LLMockApiClient/README.md)

**Perfect for:** Desktop testing workflows, visual API exploration, development and debugging

---

#### Web-Based Demos

#### SignalR Demo (`/`) — Real-Time Data Streaming with Management UI

**New in v1.1.0:** Enhanced 3-column layout with full context lifecycle management!

**Features:**
- **Create Context Panel**: Enter plain English descriptions, system generates appropriate JSON schema
- **Active Contexts Panel**:
  - Live list of all contexts with status badges (Active/Stopped)
  - Automatically displays contexts from appsettings.json on startup
  - Real-time connection count for each context
  - Per-context controls: Connect, Disconnect, Start, Stop, Delete
  - Auto-refresh on changes
- **Live Data Panel**: Real-time updates from subscribed contexts (every 5 seconds)
  - **JSON Syntax Highlighting**: Beautiful dark-themed display with color-coded elements
  - Keys (red), strings (green), numbers (orange), booleans (cyan), null (purple)
  - Clean, readable format with proper timestamp display
  - No external dependencies - lightweight built-in highlighter

**Quick-Start Examples:** One-click buttons for 5 pre-configured scenarios:
- **IoT Sensors**: Temperature sensors with device ID, readings, battery percentage
- **Stock Market**: Real-time prices with ticker, price, change percentage, volume
- **E-commerce Orders**: Orders with ID, customer, items array, status, total
- **Server Metrics**: Monitoring data with hostname, CPU, memory, disk, connections
- **Gaming Leaderboard**: Player stats with name, score, rank, level, country

**Perfect for:** Dashboards, live monitoring, IoT simulations, real-time feeds, prototyping

#### SSE Streaming Demo (`/Streaming`) — Progressive JSON Generation

**New in v1.1.0:** Quick-start example buttons for instant streaming!

**Features:**
- Configure HTTP method, path, and optional JSON shape
- Watch JSON being generated token-by-token in real-time
- Live statistics: chunk count, duration, data size
- Connection status indicators

**Quick-Start Examples:** One-click buttons for 4 streaming scenarios:
- **User List**: Array of user objects with ID, name, email
- **Product Catalog**: Product inventory with SKU, name, price, stock status
- **Order Details**: Nested orders with customer info and items array
- **Weather Data**: Current conditions with temperature, humidity, wind speed

**Perfect for:** Observing LLM generation, debugging shapes, understanding streaming behavior, testing SSE

#### OpenAPI Manager Demo (`/OpenApi`) — Dynamic Spec Loading & Testing

**New Feature:** Interactive OpenAPI specification management with real-time updates!

**Features:**
- **Load Specs Dynamically**: From URL or paste raw JSON/YAML directly in browser
- **Real-Time Updates**: SignalR-powered live notifications for all actions
- **Endpoint Discovery**: Automatic extraction of all operations from spec
- **In-Browser Testing**: Test any endpoint with one click, see LLM-generated mock data
- **Lifecycle Management**: Load, reload, delete specs without restarting server
- **Beautiful UI**: Color-coded HTTP methods, syntax-highlighted JSON responses
- **Connection Status**: Live indicator shows SignalR connection state
- **Comprehensive Help**: Inline documentation and example URLs

**Quick-Start Examples:**
- Load Petstore API from URL
- Paste custom OpenAPI JSON
- View all endpoints from spec
- Test endpoints directly in page
- See realistic mock data generated by LLM

**Perfect for:** API prototyping, frontend development, contract testing, spec validation, demos

**Run the demos:**
```bash
cd LLMApi
dotnet run
```

Navigate to:
- `http://localhost:5116` - SignalR real-time data streaming with management UI
- `http://localhost:5116/Streaming` - SSE progressive generation
- `http://localhost:5116/OpenApi` - OpenAPI spec manager with dynamic loading

All demos include:
- Comprehensive inline documentation
- Interactive quick-start examples
- Code snippets for integration
- Real-time status indicators
- Full context lifecycle controls

## Advanced Features

### Response Schema Echo (Shape) Export

You can optionally have the middleware echo back the JSON shape/schema that was used to generate the mock response.

Configuration:
- Option: IncludeShapeInResponse (bool, default false)
- Per-request override: add query parameter includeSchema=true (or 1)
- Header emitted: X-Response-Schema (only when a shape was provided and size ≤ 4000 chars)
- Streaming: the final SSE event includes a schema field when enabled

Examples:

- Enable globally (appsettings.json):
```json
{
  "mostlylucid.mockllmapi": {
    "IncludeShapeInResponse": true
  }
}
```

- Enable per request (overrides config):
```bash
curl "http://localhost:5000/api/mock/users?shape=%7B%22id%22%3A0%2C%22name%22%3A%22string%22%7D&includeSchema=true"
```
Response includes header:
```
X-Response-Schema: {"id":0,"name":"string"}
```

- Streaming: final event contains schema field when enabled
```
...
data: {"content":"{full json}","done":true,"schema":{"id":0,"name":"string"}}
```

Notes:
- If no shape was provided (via query/header/body), the header is not added
- Very large shapes (> 4000 chars) are not added to the header to avoid transport issues, but normal response continues

Use cases:
- Client-side TypeScript type generation
- API documentation and schema validation
- Debugging shape parsing
- Runtime validation

### Custom Prompt Templates

Override the default prompts with your own:

```json
{
  "mostlylucid.mockllmapi": {
    "CustomPromptTemplate": "Generate mock data for {method} {path}. Body: {body}. Use seed: {randomSeed}"
  }
}
```

Available placeholders:
- `{method}` - HTTP method (GET, POST, etc.)
- `{path}` - Full request path with query string
- `{body}` - Request body
- `{randomSeed}` - Generated random seed (GUID)
- `{timestamp}` - Unix timestamp
- `{shape}` - Shape specification (if provided)

### Error Simulation

Test your client's error handling with comprehensive error simulation capabilities.

**Four ways to configure errors** (in precedence order):

1. **Query Parameters** (highest precedence):

**IMPORTANT**: Query parameter values MUST be URL-encoded. Spaces become `%20`, `&` becomes `%26`, `:` becomes `%3A`, etc.

```bash
# Properly encoded (spaces as %20)
curl "http://localhost:5000/api/mock/users?error=404&errorMessage=Not%20found&errorDetails=User%20does%20not%20exist"

# More complex example with special characters
# Decoded: "Invalid input: email & phone required"
curl "http://localhost:5000/api/mock/users?error=400&errorMessage=Invalid%20input%3A%20email%20%26%20phone%20required"
```

2. **HTTP Headers**:
```bash
curl -H "X-Error-Code: 401" \
     -H "X-Error-Message: Unauthorized" \
     -H "X-Error-Details: Token expired" \
     http://localhost:5000/api/mock/users
```

3. **Shape JSON** (using `$error` property):
```bash
# Simple: just status code
curl "http://localhost:5000/api/mock/users?shape=%7B%22%24error%22%3A404%7D"

# Complex: with message and details
curl "http://localhost:5000/api/mock/users?shape=%7B%22%24error%22%3A%7B%22code%22%3A422%2C%22message%22%3A%22Validation%20failed%22%2C%22details%22%3A%22Email%20invalid%22%7D%7D"
```

4. **Request Body** (using `error` property):
```bash
curl -X POST http://localhost:5000/api/mock/users \
  -H "Content-Type: application/json" \
  -d '{
    "error": {
      "code": 409,
      "message": "Conflict",
      "details": "User already exists"
    }
  }'
```

**Error Response Formats:**

Regular/Streaming endpoints:
```json
{
  "error": {
    "code": 404,
    "message": "Not Found",
    "details": "Optional additional context"
  }
}
```

GraphQL endpoint:
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

**SignalR Error Simulation:**

Configure errors in SignalR contexts for testing real-time error handling:

```json
{
  "HubContexts": [
    {
      "Name": "errors",
      "Description": "Error simulation stream",
      "ErrorConfig": {
        "Code": 500,
        "Message": "Server error",
        "Details": "Database connection lost"
      }
    }
  ]
}
```

Or dynamically via the management API:

```bash
curl -X POST http://localhost:5000/api/management/contexts \
  -H "Content-Type: application/json" \
  -d '{
    "name": "errors",
    "description": "Test errors",
    "error": 503,
    "errorMessage": "Service unavailable",
    "errorDetails": "Maintenance in progress"
  }'
```

**Supported HTTP Status Codes:**

The package includes default messages for common HTTP status codes:
- **4xx Client Errors**: 400 (Bad Request), 401 (Unauthorized), 403 (Forbidden), 404 (Not Found), 405 (Method Not Allowed), 408 (Request Timeout), 409 (Conflict), 422 (Unprocessable Entity), 429 (Too Many Requests)
- **5xx Server Errors**: 500 (Internal Server Error), 501 (Not Implemented), 502 (Bad Gateway), 503 (Service Unavailable), 504 (Gateway Timeout)

Custom messages and details override the defaults.

**Use Cases:**
- Test client retry logic and exponential backoff
- Validate error message display in UI
- Test authentication/authorization flows
- Simulate rate limiting scenarios
- Practice graceful degradation patterns
- Test error logging and monitoring

See `LLMApi/LLMApi.http` for comprehensive examples of all error simulation methods.

### Multiple Instances

Mount multiple mock APIs with different configurations:

```csharp
// Development data with high randomness
builder.Services.Addmostlylucid.mockllmapi("Dev", options =>
{
    options.Temperature = 1.5;
    options.ModelName = "llm-model";
});

// Stable test data
builder.Services.Addmostlylucid.mockllmapi("Test", options =>
{
    options.Temperature = 0.3;
    options.ModelName = "llm-model";
});

app.Mapmostlylucid.mockllmapi("/api/dev");
app.Mapmostlylucid.mockllmapi("/api/test");
```

## Shape Specification

Three ways to control response structure:

1. **Header** (recommended): `X-Response-Shape: {"field":"type"}`
2. **Query param**: `?shape=%7B%22field%22%3A%22type%22%7D` (URL-encoded JSON)
3. **Body field**: `{"shape": {...}, "actualData": ...}`

### Caching Multiple Responses via Shape

You can instruct the middleware to pre-generate and cache multiple response variants for a specific request/shape by adding a special field inside the shape object: "$cache": N.

- The cache key is derived from HTTP method + path (including query) + the sanitized shape (with $cache removed) using System.IO.Hashing XXHash64.
- Up to N responses are prefetched from the LLM and stored in-memory (capped by MaxCachePerKey in options; default 5).
- Subsequent non-streaming requests for the same key are served from a depleting queue; when exhausted, a fresh batch of N is prefetched automatically.
- Streaming endpoints are not cached.

Examples

- Header shape:
  X-Response-Shape: {"$cache":3,"orderId":"string","status":"string","items":[{"sku":"string","qty":0}]}

- Body shape:
  {
  "shape": {
  "$cache": 5,
  "invoiceId": "string",
  "customer": { "id": "string", "name": "string" },
  "items": [ { "sku": "string", "qty": 0, "price": 0.0 } ],
  "total": 0.0
  }
  }

- Query param (URL-encoded):
  ?shape=%7B%22%24cache%22%3A2%2C%22users%22%3A%5B%7B%22id%22%3A0%2C%22name%22%3A%22string%22%7D%5D%7D

Configuration

- MaxCachePerKey (int, default 5): caps the number requested by "$cache" per key.

Notes

- The "$cache" hint is removed from the shape before it is sent to the LLM.
- If "$cache" is omitted or 0, the request behaves as before (no caching/warmup).
- Cached variants are kept in-memory for the app lifetime; restart clears the cache.


## Testing

### Testing Utilities Package (NEW!)

**mostlylucid.mockllmapi.Testing** - A companion NuGet package that makes testing with the mock API even easier!

```bash
dotnet add package mostlylucid.mockllmapi.Testing
```

**Quick Example:**
```csharp
using mostlylucid.mockllmapi.Testing;

// Create a configured HttpClient for testing
var client = HttpClientExtensions.CreateMockLlmClient(
    baseAddress: "http://localhost:5116",
    pathPattern: "/users",
    configure: endpoint => endpoint
        .WithShape(new { id = 0, name = "", email = "" })
        .WithCache(5)
        .WithError(404) // Simulate errors easily
);

// Use in your tests
var response = await client.GetAsync("/users");
var users = await response.Content.ReadFromJsonAsync<User[]>();
```

**Key Features:**
- **Fluent API**: Easy configuration with `WithShape()`, `WithError()`, `WithCache()`, etc.
- **HttpClient Integration**: Works seamlessly with `HttpClient` and `IHttpClientFactory`
- **Multiple Endpoints**: Configure different behaviors for different paths
- **Error Simulation**: Test error handling with various status codes
- **Streaming Support**: Test SSE streaming endpoints
- **Dependency Injection**: Built-in support for typed and named clients

**Configuration Examples:**
```csharp
// Multiple endpoints with different configurations
var client = HttpClientExtensions.CreateMockLlmClient(
    "http://localhost:5116",
    configure: handler => handler
        .ForEndpoint("/users", config => config
            .WithShape(new { id = 0, name = "", email = "" })
            .WithCache(10))
        .ForEndpoint("/posts", config => config
            .WithShape(new { id = 0, title = "", content = "" })
            .WithStreaming()
            .WithSseMode("CompleteObjects"))
        .ForEndpoint("/error", config => config
            .WithError(500, "Internal server error"))
);

// Dependency Injection support
services.AddMockLlmHttpClient<IUserApiClient>(
    baseApiPath: "/api/mock",
    configure: handler => handler
        .ForEndpoint("/users", config => config.WithShape(...))
);
```

**How It Works:**
The `MockLlmHttpHandler` is a `DelegatingHandler` that intercepts HTTP requests and automatically applies your configuration via query parameters and headers before forwarding to the mock API. This means you can use real `HttpClient` instances in your tests while controlling mock behavior declaratively.

**See the [Testing Package README](./mostlylucid.mockllmapi.Testing/README.md) for complete documentation and examples.**

### HTTP File Testing

Use the included `LLMApi.http` file with:
- Visual Studio / Rider HTTP client
- VS Code REST Client extension
- Any HTTP client

### Unit Tests

The project includes comprehensive unit tests:

```bash
# Run all tests
dotnet test

# Run with detailed output
dotnet test --verbosity detailed
```

**Test Coverage (228 tests, 100% pass rate):**
- **Body reading** (empty, JSON content, form bodies, file uploads)
- **Form body parsing** (single values, multiple values, special characters)
- **Multipart form data** (file metadata, mixed fields and files)
- **JSON escaping** (quotes, backslashes, newlines, tabs, unicode, emoji)
- **Content extraction** (OpenAI format, complex nested JSON, error handling)
- **Manual JSON construction** (form data, arrays, nested structures)
- **Shape extraction** (query param, header, body field, precedence)
- **Prompt generation** (randomness, shape inclusion, streaming modes)
- **Request building** (temperature, model, messages)
- **Edge cases** (invalid JSON, missing data, special characters)
- **Integration tests** (full HTTP workflow, form submissions, file uploads, deep paths)

**New in v2.3.0:**
- **FormBodyHandlingTests.cs** - 12 tests for form body parsing and JSON construction
- **JsonHandlingTests.cs** - 25 tests for JSON escaping, extraction, and edge cases
- **IntegrationTests.cs** - Integration test framework for full HTTP endpoint testing

See [TEST_SUMMARY.md](./TEST_SUMMARY.md) for complete test documentation and coverage metrics.

## Architecture

See [Architecture Overview](#architecture-overview) above for the high-level system diagram and component description.

### Detailed Request Flow

```mermaid
sequenceDiagram
    participant C as Client
    participant A as LLMApi
    participant H as AutoApiHelper
    participant O as Ollama
    participant M as llm-model

    C->>A: GET/POST/PUT/DELETE /api/auto/**
    A->>H: Extract context (method, path, body, shape)
    H->>H: Generate random seed + timestamp
    H->>H: Build prompt with randomness
    H-->>A: Prompt + temperature=1.2
    A->>O: POST /v1/chat/completions
    O->>M: Run inference
    M-->>O: Generated JSON
    O-->>A: Response
    A-->>C: JSON Response
```

### Shape Control Flow

```mermaid
flowchart TD
    Start[Request Arrives] --> CheckQuery{Shape in<br/>Query Param?}
    CheckQuery -->|Yes| UseQuery[Use Query Shape]
    CheckQuery -->|No| CheckHeader{Shape in<br/>Header?}
    CheckHeader -->|Yes| UseHeader[Use Header Shape]
    CheckHeader -->|No| CheckBody{Shape in<br/>Body Field?}
    CheckBody -->|Yes| UseBody[Use Body Shape]
    CheckBody -->|No| NoShape[No Shape Constraint]

    UseQuery --> BuildPrompt[Build Prompt]
    UseHeader --> BuildPrompt
    UseBody --> BuildPrompt
    NoShape --> BuildPrompt

    BuildPrompt --> AddRandom[Add Random Seed<br/>+ Timestamp]
    AddRandom --> SendLLM[Send to LLM]

    style UseQuery fill:#4CAF50
    style UseHeader fill:#4CAF50
    style UseBody fill:#4CAF50
    style NoShape fill:#FFC107
```

**Projects:**
- `mostlylucid.mockllmapi`: NuGet package library
- `LLMApi`: Demo application
- `LLMApi.Tests`: xUnit test suite (228 tests - 100% pass rate)

## Why Use mostlylucid.mockllmapi?

- **Rapid Prototyping**: Frontend development without waiting for backend
- **Complete HTTP Support**: JSON, form bodies, file uploads - all content types covered
- **Consistent Workflows**: API contexts maintain realistic relationships across multi-step processes
- **Demos**: Show realistic data flows without hardcoded fixtures
- **Testing**: Generate varied test data for edge cases - or consistent data with contexts
- **Memory-Safe**: File uploads streamed and dumped - tested with 100MB+ files
- **API Design**: Experiment with response shapes before implementing
- **Learning**: Example of LLM integration in .NET minimal APIs
- **Zero Maintenance**: No database, no state, no mock data files to maintain
- **Flexible**: Use any combination of REST, GraphQL, SSE, SignalR, or OpenAPI specs
- **Production-Ready**: 228 tests, 100% pass rate, comprehensive documentation

## Building the NuGet Package

```bash
cd mostlylucid.mockllmapi
dotnet pack -c Release
```

Package will be in `bin/Release/mostlylucid.mockllmapi.{version}.nupkg`

## Contributing

This is a sample project demonstrating LLM-powered mock APIs. Feel free to fork and customize!

## License

This is free and unencumbered software released into the public domain. See [LICENSE](LICENSE) for details or visit [unlicense.org](https://unlicense.org).


