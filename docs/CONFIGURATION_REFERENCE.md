# Complete Configuration Reference

**Version:** 1.8.0+
**File:** `appsettings.Full.json` - Complete example with all options

This document provides a comprehensive reference for all configuration options available in the Mock LLM API.

## Table of Contents

- [Quick Reference](#quick-reference)
- [Backend Configuration](#backend-configuration)
- [LLM Generation Settings](#llm-generation-settings)
- [Automatic Chunking](#automatic-chunking)
- [Caching Configuration](#caching-configuration)
- [Resilience Policies](#resilience-policies)
- [Custom Prompts](#custom-prompts)
- [Delay Simulation](#delay-simulation)
- [Response Options](#response-options)
- [GraphQL Settings](#graphql-settings)
- [SignalR Configuration](#signalr-configuration)
- [OpenAPI Specifications](#openapi-specifications)
- [Environment Variables](#environment-variables)

---

## Quick Reference

### Minimal Configuration (Single Backend)

```json
{
  "MockLlmApi": {
    "BaseUrl": "http://localhost:11434/v1/",
    "ModelName": "llama3"
  }
}
```

### Multiple Backends

```json
{
  "MockLlmApi": {
    "Backends": [
      {
        "Name": "local-llama",
        "Provider": "ollama",
        "BaseUrl": "http://localhost:11434/v1/",
        "ModelName": "llama3",
        "Enabled": true
      },
      {
        "Name": "cloud-gpt4",
        "Provider": "openai",
        "BaseUrl": "https://api.openai.com/v1/",
        "ModelName": "gpt-4",
        "ApiKey": "sk-...",
        "Enabled": false
      }
    ]
  }
}
```

---

## Backend Configuration

### Legacy Single Backend (Backward Compatible)

These settings automatically create a default Ollama backend:

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `BaseUrl` | string | `"http://localhost:11434/v1/"` | LLM API endpoint |
| `ModelName` | string | `"llama3"` | Model identifier |

**Note:** Ignored if `Backends` array is configured.

### Multiple Backends (v1.8.0+)

Configure multiple LLM providers for flexibility and reliability:

```json
{
  "Backends": [
    {
      "Name": "backend-name",
      "Provider": "ollama",
      "BaseUrl": "http://localhost:11434/v1/",
      "ModelName": "llama3",
      "ApiKey": null,
      "MaxTokens": 8192,
      "Enabled": true,
      "Weight": 1,
      "MaxConcurrentRequests": 0,
      "HealthCheckPath": null,
      "Priority": 0
    }
  ]
}
```

#### Backend Properties

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| `Name` | string | ✅ | - | Unique identifier for per-request selection |
| `Provider` | string | ✅ | `"ollama"` | Provider type: `"ollama"`, `"openai"`, `"lmstudio"` |
| `BaseUrl` | string | ✅ | - | Full API endpoint URL (must include `/v1/` for most) |
| `ModelName` | string | ✅ | - | Model identifier (e.g., `"llama3"`, `"gpt-4"`) |
| `ApiKey` | string | ❌ | `null` | API key (required for OpenAI, optional for others) |
| `MaxTokens` | int? | ❌ | `null` | Max output tokens (overrides global `MaxOutputTokens`) |
| `Enabled` | bool | ❌ | `true` | Whether backend is active |
| `Weight` | int | ❌ | `1` | Load balancing weight (higher = more traffic) |
| `MaxConcurrentRequests` | int | ❌ | `0` | Request limit (0 = unlimited) |
| `HealthCheckPath` | string | ❌ | `null` | Health check endpoint (future use) |
| `Priority` | int | ❌ | `0` | Selection priority (higher = preferred) |

#### Per-Request Backend Selection

**Via Header:**
```http
GET /api/mock/users
X-LLM-Backend: backend-name
```

**Via Query Parameter:**
```http
GET /api/mock/users?backend=backend-name
```

**Priority:** Header > Query > Default selection

#### Provider Examples

**Ollama (Local Server)**
```json
{
  "Name": "ollama-llama3",
  "Provider": "ollama",
  "BaseUrl": "http://localhost:11434/v1/",
  "ModelName": "llama3",
  "MaxTokens": 8192,
  "Enabled": true
}
```

**OpenAI (Cloud API)**
```json
{
  "Name": "openai-gpt4",
  "Provider": "openai",
  "BaseUrl": "https://api.openai.com/v1/",
  "ModelName": "gpt-4",
  "ApiKey": "sk-proj-...",
  "MaxTokens": 8192,
  "Enabled": true
}
```

**LM Studio (Local Server)**
```json
{
  "Name": "lmstudio-local",
  "Provider": "lmstudio",
  "BaseUrl": "http://localhost:1234/v1/",
  "ModelName": "local-model",
  "MaxTokens": 8192,
  "Enabled": true
}
```

**Mistral-Nemo (128k Context - Large Scale Data)**
```json
{
  "Name": "ollama-mistral-nemo",
  "Provider": "ollama",
  "BaseUrl": "http://localhost:11434/v1/",
  "ModelName": "mistral-nemo",
  "MaxTokens": 128000,
  "Enabled": true,
  "Weight": 2,
  "Priority": 6
}
```

**Use Case:** Mistral-Nemo 12B provides a massive 128k context window, making it ideal for:
- Generating thousands of detailed records in a single request
- Complex nested data structures with deep relationships
- Large batch operations (use with `MaxItems=10000+`)
- Comprehensive test datasets
- GraphQL queries with extensive nested fields

**Performance Note:** While the 128k context allows massive outputs, actual generation speed depends on your hardware. Combine with `EnableAutoChunking=true` for optimal streaming delivery.

---

## LLM Generation Settings

Global settings affecting all LLM requests:

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Temperature` | double | `1.2` | Randomness (0.0=deterministic, 2.0=creative) |
| `MaxInputTokens` | int | `2048` | Maximum input context size |
| `MaxOutputTokens` | int | `2048` | Maximum output tokens (for chunking) |
| `TimeoutSeconds` | int | `30` | HTTP request timeout |

**Example:**
```json
{
  "Temperature": 1.2,
  "MaxInputTokens": 4096,
  "MaxOutputTokens": 2048,
  "TimeoutSeconds": 30
}
```

---

## Automatic Chunking

Automatically splits large requests into chunks:

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `EnableAutoChunking` | bool | `true` | Enable automatic chunking |
| `MaxItems` | int | `1000` | Max items per response AND cache size |

**Disable Per-Request:**
```http
GET /api/mock/users?count=100&autoChunk=false
```

**See:** [CHUNKING_AND_CACHING.md](../CHUNKING_AND_CACHING.md)

---

## Caching Configuration

Control response caching behavior:

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxCachePerKey` | int | `5` | Cached variants per unique request |
| `CacheSlidingExpirationMinutes` | int | `15` | Expire after inactivity |
| `CacheAbsoluteExpirationMinutes` | int? | `60` | Maximum lifetime (null=none) |
| `CacheRefreshThresholdPercent` | int | `50` | Pre-fetch threshold (0-100, future) |
| `CachePriority` | int | `1` | Priority: 0=Low, 1=Normal, 2=High, 3=NeverRemove |
| `EnableCacheStatistics` | bool | `false` | Track hits/misses |
| `EnableCacheCompression` | bool | `false` | Compress cached responses |

**Example:**
```json
{
  "MaxCachePerKey": 5,
  "CacheSlidingExpirationMinutes": 15,
  "CacheAbsoluteExpirationMinutes": 60,
  "CachePriority": 1,
  "EnableCacheStatistics": true,
  "EnableCacheCompression": false
}
```

**Control Per-Request:**
```http
GET /api/mock/users?count=10
X-Cache-Count: 3
```

**See:** [CHUNKING_AND_CACHING.md](../CHUNKING_AND_CACHING.md)

---

## Resilience Policies

Polly v8 retry and circuit breaker patterns:

### Retry Policy

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `EnableRetryPolicy` | bool | `true` | Enable exponential backoff retry |
| `MaxRetryAttempts` | int | `3` | Maximum retry attempts |
| `RetryBaseDelaySeconds` | double | `1.0` | Base delay (exponential: 1s, 2s, 4s) |

### Circuit Breaker

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `EnableCircuitBreaker` | bool | `true` | Enable circuit breaker pattern |
| `CircuitBreakerFailureThreshold` | int | `5` | Failures before opening circuit |
| `CircuitBreakerDurationSeconds` | int | `30` | Duration circuit stays open |

**Example:**
```json
{
  "EnableRetryPolicy": true,
  "MaxRetryAttempts": 3,
  "RetryBaseDelaySeconds": 1.0,
  "EnableCircuitBreaker": true,
  "CircuitBreakerFailureThreshold": 5,
  "CircuitBreakerDurationSeconds": 30
}
```

---

## Custom Prompts

Override default LLM prompts:

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `CustomPromptTemplate` | string | `null` | Template for non-streaming requests |
| `CustomStreamingPromptTemplate` | string | `null` | Template for streaming requests |

**Placeholders:** `{method}`, `{path}`, `{body}`, `{randomSeed}`, `{timestamp}`

**Example:**
```json
{
  "CustomPromptTemplate": "Generate JSON for {method} {path}. Body: {body}. Random seed: {randomSeed}"
}
```

---

## SSE Streaming Modes

Configure Server-Sent Events (SSE) streaming behavior:

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `SseMode` | enum | `LlmTokens` | SSE streaming mode: `LlmTokens`, `CompleteObjects`, `ArrayItems` |

### Modes

**LlmTokens (Default)**
- Streams LLM generation token-by-token
- Use case: AI chat interfaces, LLM testing
- Format: `{"chunk":"text","accumulated":"full","done":false}`

**CompleteObjects**
- Streams complete JSON objects as separate events
- Use case: Twitter/X API, stock tickers, real-time feeds
- Format: `{"data":{object},"index":0,"total":10,"done":false}`

**ArrayItems**
- Streams array items individually with metadata
- Use case: Paginated results, bulk exports, search results
- Format: `{"item":{object},"index":0,"total":100,"arrayName":"users","hasMore":true,"done":false}`

**Example:**
```json
{
  "SseMode": "CompleteObjects",
  "StreamingChunkDelayMinMs": 100,
  "StreamingChunkDelayMaxMs": 500
}
```

**Per-Request Override:**
```http
GET /api/mock/stream/users?sseMode=CompleteObjects
GET /api/mock/stream/data?sseMode=ArrayItems
GET /api/mock/stream/chat?sseMode=LlmTokens
```

**See:** [docs/SSE_STREAMING_MODES.md](./SSE_STREAMING_MODES.md) for complete documentation

---

## Delay Simulation

Add realistic delays for testing:

### Request Delay

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `RandomRequestDelayMinMs` | int | `0` | Minimum delay before response |
| `RandomRequestDelayMaxMs` | int | `0` | Maximum delay (random between min/max) |

### Streaming Chunk Delay

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `StreamingChunkDelayMinMs` | int | `0` | Minimum delay between SSE chunks |
| `StreamingChunkDelayMaxMs` | int | `0` | Maximum delay (random between min/max) |

**Example:**
```json
{
  "RandomRequestDelayMinMs": 100,
  "RandomRequestDelayMaxMs": 500,
  "StreamingChunkDelayMinMs": 50,
  "StreamingChunkDelayMaxMs": 200
}
```

---

## Response Options

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `IncludeShapeInResponse` | bool | `false` | Include JSON schema in response |
| `EnableVerboseLogging` | bool | `false` | Detailed logging output |

**Override Per-Request:**
```http
GET /api/mock/users?includeSchema=true
```

---

## GraphQL Settings

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `GraphQLMaxTokens` | int? | `500` | Max tokens for GraphQL responses |

**Recommendations:**
- Small models (tinyllama): 200-300
- Medium models (llama3): 500
- Large models (gpt-4): 1000

**Example:**
```json
{
  "GraphQLMaxTokens": 500
}
```

---

## SignalR Configuration

Real-time streaming data via SignalR:

### Global Settings

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `SignalRPushIntervalMs` | int | `5000` | Push interval (milliseconds) |

### Hub Contexts

```json
{
  "HubContexts": [
    {
      "Name": "weather",
      "Description": "Weather data generation",
      "Method": "GET",
      "Path": "/data/weather",
      "Body": null,
      "Shape": "{\"temperature\":25,\"condition\":\"sunny\"}",
      "IsJsonSchema": false,
      "ApiContextName": "weather-station-1",
      "IsActive": false,
      "ConnectionCount": 0,
      "ErrorConfig": null
    }
  ]
}
```

#### HubContextConfig Properties

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `Name` | string | ✅ | Hub context identifier |
| `Description` | string | ❌ | Plain English data description |
| `Method` | string | ❌ | HTTP method for prompt (default: `"GET"`) |
| `Path` | string | ❌ | Path for prompt (default: `"/data"`) |
| `Body` | string | ❌ | Request body for prompt |
| `Shape` | string | ❌ | JSON shape or schema |
| `IsJsonSchema` | bool? | ❌ | Treat shape as JSON Schema |
| `ApiContextName` | string | ❌ | Shared context name |
| `BackendName` | string | ❌ | Single LLM backend to use (e.g., `"openai-gpt4"`, `"ollama-llama3"`) |
| `BackendNames` | string[] | ❌ | Multiple backends for load balancing (takes precedence over `BackendName`) |
| `IsActive` | bool | ❌ | Enable data generation (default: `true`) |
| `ConnectionCount` | int | ❌ | Current connections (read-only) |
| `ErrorConfig` | object | ❌ | Error simulation config |

**Backend Selection Strategy:**
- **Single Backend (`BackendName`):** Use one specific backend for all requests from this hub
- **Load Balanced (`BackendNames`):** Distribute requests across multiple backends using weighted round-robin
- **Complex Data:** Use powerful models like GPT-4 for GraphQL queries, analytics dashboards, and deeply nested structures
- **Simple Data:** Use fast local models like Ollama (llama3, tinyllama) for high-frequency IoT sensors, tracking data, or simple metrics
- **High Throughput:** Use multiple backends with `BackendNames` to distribute load and maximize throughput
- **Null/Unset:** Falls back to default backend selection (round-robin across all enabled backends)

#### Backend Selection Examples

**Use GPT-4 for complex GraphQL queries:**
```json
{
  "Name": "graphql-results",
  "Description": "GraphQL query with deeply nested user profiles, posts, and relationships",
  "BackendName": "openai-gpt4-turbo",
  "Shape": null,
  "IsActive": true
}
```

**Use local Ollama for high-frequency simple data:**
```json
{
  "Name": "iot-sensors",
  "Description": "High-volume IoT sensor readings",
  "BackendName": "ollama-llama3",
  "Shape": "{\"deviceId\":\"sensor-001\",\"value\":23.5,\"unit\":\"celsius\"}",
  "IsActive": true
}
```

**Load balance across multiple backends for high throughput:**
```json
{
  "Name": "high-throughput-data",
  "Description": "High-volume data generation requiring maximum throughput",
  "BackendNames": [
    "ollama-llama3",      // Weight: 3 (gets more requests)
    "ollama-mistral",     // Weight: 2 (gets fewer requests)
    "lmstudio-default"    // Weight: 1 (gets fewest requests)
  ],
  "IsActive": true
}
```

**Mix backends for different use cases:**
```json
{
  "HubContexts": [
    {
      "Name": "analytics-dashboard",
      "Description": "Complex business metrics with predictions",
      "BackendName": "openai-gpt4-turbo",
      "BackendNames": null,
      "IsActive": true
    },
    {
      "Name": "stock-ticker",
      "Description": "Simple stock price updates",
      "BackendName": "ollama-llama3",
      "BackendNames": null,
      "IsActive": true
    },
    {
      "Name": "distributed-sensors",
      "Description": "Thousands of IoT sensors requiring load balancing",
      "BackendName": null,
      "BackendNames": ["ollama-llama3", "ollama-mistral", "lmstudio-default"],
      "IsActive": true
    }
  ]
}
```

#### Error Simulation

```json
{
  "Name": "errors",
  "ErrorConfig": {
    "Code": 500,
    "Message": "Internal Server Error",
    "Details": "Database unavailable"
  }
}
```

---

## OpenAPI Specifications

Auto-generate mock endpoints from OpenAPI/Swagger specs:

```json
{
  "OpenApiSpecs": [
    {
      "Name": "petstore",
      "Source": "https://petstore3.swagger.io/api/v3/openapi.json",
      "BasePath": "/petstore",
      "ContextName": "petstore-demo",
      "EnableStreaming": false,
      "IncludeTags": null,
      "ExcludeTags": ["admin"],
      "IncludePaths": null,
      "ExcludePaths": ["/internal/*"]
    }
  ]
}
```

### OpenApiSpecConfig Properties

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `Name` | string | ✅ | Unique spec identifier |
| `Source` | string | ✅ | URL or file path to spec (JSON/YAML) |
| `BasePath` | string | ❌ | Mount point (default: from spec) |
| `ContextName` | string | ❌ | Shared context for consistency |
| `EnableStreaming` | bool | ❌ | Add `/stream` endpoints (default: `false`) |
| `IncludeTags` | string[] | ❌ | Only include operations with these tags |
| `ExcludeTags` | string[] | ❌ | Exclude operations with these tags |
| `IncludePaths` | string[] | ❌ | Include paths (supports wildcards: `"/users/*"`) |
| `ExcludePaths` | string[] | ❌ | Exclude paths (supports wildcards) |

**Source Examples:**
- URL: `"https://api.example.com/swagger.json"`
- Local file: `"./specs/my-api.yaml"`
- Absolute path: `"C:/specs/api-definition.json"`

---

## Environment Variables

All configuration options can be overridden via environment variables using the format: `MockLlmApi__PropertyName`

For nested properties, use double underscores (`__`) as separators.

### Complete Environment Variable Reference

| Environment Variable | Type | Optional | Range/Values | Default | Purpose |
|---------------------|------|----------|--------------|---------|---------|
| `MockLlmApi__BaseUrl` | string | ✅ | Valid URL | `http://localhost:11434/v1/` | Legacy backend URL |
| `MockLlmApi__ModelName` | string | ✅ | Any string | `llama3` | Legacy model name |
| `MockLlmApi__Temperature` | double | ✅ | 0.0 - 2.0 | `1.2` | LLM randomness |
| `MockLlmApi__MaxInputTokens` | int | ✅ | 128 - 32768 | `2048` | Max input context |
| `MockLlmApi__MaxOutputTokens` | int | ✅ | 128 - 32768 | `2048` | Max output tokens |
| `MockLlmApi__TimeoutSeconds` | int | ✅ | 5 - 600 | `30` | HTTP timeout |
| `MockLlmApi__EnableAutoChunking` | bool | ✅ | true/false | `true` | Auto-chunk large requests |
| `MockLlmApi__MaxItems` | int | ✅ | 1 - 100000 | `1000` | Max items per response |
| `MockLlmApi__CustomPromptTemplate` | string | ✅ | Any string | `null` | Custom LLM prompt |
| `MockLlmApi__CustomStreamingPromptTemplate` | string | ✅ | Any string | `null` | Custom streaming prompt |
| `MockLlmApi__EnableVerboseLogging` | bool | ✅ | true/false | `false` | Detailed logs |
| `MockLlmApi__IncludeShapeInResponse` | bool | ✅ | true/false | `false` | Include schema in response |
| `MockLlmApi__MaxCachePerKey` | int | ✅ | 0 - 100 | `5` | Cached variants per key |
| `MockLlmApi__CacheSlidingExpirationMinutes` | int | ✅ | 1 - 1440 | `15` | Sliding cache expiration |
| `MockLlmApi__CacheAbsoluteExpirationMinutes` | int? | ✅ | 1 - 10080, null | `60` | Absolute cache expiration |
| `MockLlmApi__CacheRefreshThresholdPercent` | int | ✅ | 0 - 100 | `50` | Pre-fetch threshold |
| `MockLlmApi__CachePriority` | int | ✅ | 0 - 3 | `1` | Cache priority (0=Low, 3=NeverRemove) |
| `MockLlmApi__EnableCacheStatistics` | bool | ✅ | true/false | `false` | Track cache stats |
| `MockLlmApi__EnableCacheCompression` | bool | ✅ | true/false | `false` | Compress cached data |
| `MockLlmApi__StreamingChunkDelayMinMs` | int | ✅ | 0 - 10000 | `0` | Min SSE chunk delay |
| `MockLlmApi__StreamingChunkDelayMaxMs` | int | ✅ | 0 - 10000 | `0` | Max SSE chunk delay |
| `MockLlmApi__RandomRequestDelayMinMs` | int | ✅ | 0 - 10000 | `0` | Min request delay |
| `MockLlmApi__RandomRequestDelayMaxMs` | int | ✅ | 0 - 10000 | `0` | Max request delay |
| `MockLlmApi__EnableRetryPolicy` | bool | ✅ | true/false | `true` | Enable retry with backoff |
| `MockLlmApi__MaxRetryAttempts` | int | ✅ | 0 - 10 | `3` | Max retry attempts |
| `MockLlmApi__RetryBaseDelaySeconds` | double | ✅ | 0.1 - 60.0 | `1.0` | Retry base delay |
| `MockLlmApi__EnableCircuitBreaker` | bool | ✅ | true/false | `true` | Enable circuit breaker |
| `MockLlmApi__CircuitBreakerFailureThreshold` | int | ✅ | 1 - 100 | `5` | Failures before open |
| `MockLlmApi__CircuitBreakerDurationSeconds` | int | ✅ | 1 - 600 | `30` | Circuit open duration |
| `MockLlmApi__GraphQLMaxTokens` | int? | ✅ | 100 - 10000, null | `500` | GraphQL response limit |
| `MockLlmApi__SignalRPushIntervalMs` | int | ✅ | 100 - 60000 | `5000` | SignalR push interval |

### Backend-Specific Environment Variables

Backend configurations use array indexing syntax:

| Pattern | Type | Optional | Purpose |
|---------|------|----------|---------|
| `MockLlmApi__Backends__<N>__Name` | string | ❌ Required | Unique backend identifier |
| `MockLlmApi__Backends__<N>__Provider` | string | ❌ Required | Provider type (ollama/openai/lmstudio) |
| `MockLlmApi__Backends__<N>__BaseUrl` | string | ❌ Required | Full API endpoint URL |
| `MockLlmApi__Backends__<N>__ModelName` | string | ❌ Required | Model identifier |
| `MockLlmApi__Backends__<N>__ApiKey` | string | ✅ Optional | API key (required for OpenAI) |
| `MockLlmApi__Backends__<N>__MaxTokens` | int? | ✅ Optional | Max tokens for this backend |
| `MockLlmApi__Backends__<N>__Enabled` | bool | ✅ Optional | Enable/disable backend (default: true) |
| `MockLlmApi__Backends__<N>__Weight` | int | ✅ Optional | Load balancing weight (default: 1) |
| `MockLlmApi__Backends__<N>__MaxConcurrentRequests` | int | ✅ Optional | Request limit (0=unlimited) |
| `MockLlmApi__Backends__<N>__Priority` | int | ✅ Optional | Selection priority (default: 0) |

**`<N>` = Zero-based array index (0, 1, 2, ...)**

### OpenAPI Spec Environment Variables

| Pattern | Type | Optional | Purpose |
|---------|------|----------|---------|
| `MockLlmApi__OpenApiSpecs__<N>__Name` | string | ❌ Required | Spec identifier |
| `MockLlmApi__OpenApiSpecs__<N>__Source` | string | ❌ Required | URL or file path to spec |
| `MockLlmApi__OpenApiSpecs__<N>__BasePath` | string | ✅ Optional | Mount point override |
| `MockLlmApi__OpenApiSpecs__<N>__ContextName` | string | ✅ Optional | Shared context name |
| `MockLlmApi__OpenApiSpecs__<N>__EnableStreaming` | bool | ✅ Optional | Add /stream endpoints (default: false) |

### SignalR Hub Environment Variables

| Pattern | Type | Optional | Purpose |
|---------|------|----------|---------|
| `MockLlmApi__HubContexts__<N>__Name` | string | ❌ Required | Hub identifier |
| `MockLlmApi__HubContexts__<N>__Description` | string | ✅ Optional | Data description |
| `MockLlmApi__HubContexts__<N>__Shape` | string | ✅ Optional | JSON shape |
| `MockLlmApi__HubContexts__<N>__ApiContextName` | string | ✅ Optional | Shared context |
| `MockLlmApi__HubContexts__<N>__BackendName` | string | ✅ Optional | Single backend name (e.g., `ollama-llama3`) |
| `MockLlmApi__HubContexts__<N>__BackendNames__<M>` | string | ✅ Optional | Multiple backends for load balancing (array indexed by M) |
| `MockLlmApi__HubContexts__<N>__IsActive` | bool | ✅ Optional | Enable hub (default: true) |

### Environment Variable Examples

#### Basic Configuration

```bash
# Linux/macOS
export MockLlmApi__BaseUrl="http://localhost:11434/v1/"
export MockLlmApi__ModelName="llama3"
export MockLlmApi__Temperature="1.5"
export MockLlmApi__EnableVerboseLogging="true"
export MockLlmApi__MaxOutputTokens="4096"

# Windows CMD
set MockLlmApi__BaseUrl=http://localhost:11434/v1/
set MockLlmApi__ModelName=llama3
set MockLlmApi__Temperature=1.5

# Windows PowerShell
$env:MockLlmApi__BaseUrl="http://localhost:11434/v1/"
$env:MockLlmApi__ModelName="llama3"
$env:MockLlmApi__Temperature="1.5"
```

#### Multiple Backends

```bash
# Backend 0: Local Ollama
export MockLlmApi__Backends__0__Name="ollama-llama3"
export MockLlmApi__Backends__0__Provider="ollama"
export MockLlmApi__Backends__0__BaseUrl="http://localhost:11434/v1/"
export MockLlmApi__Backends__0__ModelName="llama3"
export MockLlmApi__Backends__0__Enabled="true"
export MockLlmApi__Backends__0__MaxTokens="8192"

# Backend 1: OpenAI (with secure API key)
export MockLlmApi__Backends__1__Name="openai-gpt4"
export MockLlmApi__Backends__1__Provider="openai"
export MockLlmApi__Backends__1__BaseUrl="https://api.openai.com/v1/"
export MockLlmApi__Backends__1__ModelName="gpt-4"
export MockLlmApi__Backends__1__ApiKey="sk-proj-YOUR-REAL-KEY-HERE"
export MockLlmApi__Backends__1__Enabled="false"
export MockLlmApi__Backends__1__MaxTokens="8192"

# Backend 2: LM Studio
export MockLlmApi__Backends__2__Name="lmstudio-local"
export MockLlmApi__Backends__2__Provider="lmstudio"
export MockLlmApi__Backends__2__BaseUrl="http://localhost:1234/v1/"
export MockLlmApi__Backends__2__ModelName="local-model"
export MockLlmApi__Backends__2__Enabled="true"
```

#### Resilience Policies

```bash
# Retry policy
export MockLlmApi__EnableRetryPolicy="true"
export MockLlmApi__MaxRetryAttempts="5"
export MockLlmApi__RetryBaseDelaySeconds="2.0"

# Circuit breaker
export MockLlmApi__EnableCircuitBreaker="true"
export MockLlmApi__CircuitBreakerFailureThreshold="10"
export MockLlmApi__CircuitBreakerDurationSeconds="60"
```

#### Caching & Chunking

```bash
# Chunking
export MockLlmApi__EnableAutoChunking="true"
export MockLlmApi__MaxItems="5000"
export MockLlmApi__MaxOutputTokens="4096"

# Caching
export MockLlmApi__MaxCachePerKey="10"
export MockLlmApi__CacheSlidingExpirationMinutes="30"
export MockLlmApi__CacheAbsoluteExpirationMinutes="120"
export MockLlmApi__EnableCacheStatistics="true"
export MockLlmApi__EnableCacheCompression="false"
export MockLlmApi__CachePriority="2"
```

#### SignalR Hub Contexts

```bash
# Hub 0: Single backend (GPT-4 for complex data)
export MockLlmApi__HubContexts__0__Name="analytics-dashboard"
export MockLlmApi__HubContexts__0__Description="Complex business analytics with nested metrics"
export MockLlmApi__HubContexts__0__BackendName="openai-gpt4-turbo"
export MockLlmApi__HubContexts__0__IsActive="true"

# Hub 1: Load balanced across multiple backends (high throughput)
export MockLlmApi__HubContexts__1__Name="iot-sensors"
export MockLlmApi__HubContexts__1__Description="High-volume IoT sensor readings"
export MockLlmApi__HubContexts__1__BackendNames__0="ollama-llama3"
export MockLlmApi__HubContexts__1__BackendNames__1="ollama-mistral"
export MockLlmApi__HubContexts__1__BackendNames__2="lmstudio-default"
export MockLlmApi__HubContexts__1__IsActive="true"

# Hub 2: Default backend selection (no BackendName or BackendNames)
export MockLlmApi__HubContexts__2__Name="weather-data"
export MockLlmApi__HubContexts__2__Description="Simple weather updates"
export MockLlmApi__HubContexts__2__IsActive="true"
```

#### OpenAPI Specs

```bash
export MockLlmApi__OpenApiSpecs__0__Name="petstore"
export MockLlmApi__OpenApiSpecs__0__Source="https://petstore3.swagger.io/api/v3/openapi.json"
export MockLlmApi__OpenApiSpecs__0__BasePath="/petstore"
export MockLlmApi__OpenApiSpecs__0__EnableStreaming="false"
```

#### Docker Compose Example

```yaml
version: '3.8'
services:
  llmock-api:
    image: llmock-api:latest
    environment:
      # Backend configuration
      - MockLlmApi__Backends__0__Name=ollama-llama3
      - MockLlmApi__Backends__0__Provider=ollama
      - MockLlmApi__Backends__0__BaseUrl=http://ollama:11434/v1/
      - MockLlmApi__Backends__0__ModelName=llama3
      - MockLlmApi__Backends__0__Enabled=true

      # Performance tuning
      - MockLlmApi__MaxOutputTokens=4096
      - MockLlmApi__EnableAutoChunking=true
      - MockLlmApi__MaxItems=1000

      # Resilience
      - MockLlmApi__EnableRetryPolicy=true
      - MockLlmApi__MaxRetryAttempts=3
      - MockLlmApi__EnableCircuitBreaker=true
```

#### Kubernetes ConfigMap

```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: llmock-api-config
data:
  MockLlmApi__BaseUrl: "http://ollama-service:11434/v1/"
  MockLlmApi__ModelName: "llama3"
  MockLlmApi__Temperature: "1.2"
  MockLlmApi__EnableAutoChunking: "true"
  MockLlmApi__MaxOutputTokens: "4096"
---
apiVersion: v1
kind: Secret
metadata:
  name: llmock-api-secrets
type: Opaque
stringData:
  MockLlmApi__Backends__0__ApiKey: "sk-proj-SECRET-KEY"
```

### Security Best Practices

1. **Never commit API keys to source control**
   ```bash
   # ✅ Good: Use environment variables
   export MockLlmApi__Backends__0__ApiKey="$OPENAI_API_KEY"

   # ❌ Bad: Hardcode in appsettings.json
   { "ApiKey": "sk-proj-actual-key" }
   ```

2. **Use placeholder values in appsettings.json**
   ```json
   {
     "ApiKey": "PLACEHOLDER_REPLACED_BY_ENV_VAR"
   }
   ```

3. **Rotate keys regularly**
   ```bash
   # Update environment variable and restart app
   export MockLlmApi__Backends__0__ApiKey="sk-proj-NEW-KEY"
   ```

4. **Use secrets management in production**
   - Azure Key Vault
   - AWS Secrets Manager
   - HashiCorp Vault
   - Kubernetes Secrets

### Troubleshooting Environment Variables

**Variables not taking effect:**
- Ensure app is restarted after setting variables
- Check for typos (double underscores `__`, not single)
- Verify case sensitivity (use exact casing: `MockLlmApi` not `mocklLMApi`)

**Array indexing issues:**
- Indices must be sequential: 0, 1, 2, 3...
- Gaps in indices will stop array parsing
- Verify index numbers match your intent

**Boolean parsing:**
- Valid values: `"true"`, `"false"` (case-insensitive)
- Invalid: `"yes"`, `"1"`, `"True"` (will not parse correctly)

**Type conversion errors:**
- Integers: Use numeric strings `"1234"`, not `"1,234"`
- Doubles: Use dot decimal `"1.5"`, not comma `"1,5"`
- Null values: Use empty string `""` or omit variable

---

## Configuration Validation

### Common Issues

**"No LLM backend available"**
- Check: At least one backend has `"Enabled": true`
- Check: `BaseUrl` is set (legacy) OR `Backends` array is not empty

**"OpenAI provider requires an API key"**
- Add: `"ApiKey": "sk-..."` to OpenAI backend config
- Or: Set via environment variable

**Backend not selected via header**
- Check: `Name` matches exactly (case-insensitive)
- Check: Backend has `"Enabled": true`
- Check: Header format: `X-LLM-Backend: backend-name`

### Best Practices

1. **Use environment variables for secrets**
   ```bash
   export MockLlmApi__Backends__0__ApiKey="sk-..."
   ```

2. **Disable expensive backends by default**
   ```json
   { "Name": "openai-gpt4", "Enabled": false }
   ```

3. **Use descriptive backend names**
   ```json
   { "Name": "ollama-llama3-8b" }  // Good
   { "Name": "backend1" }          // Avoid
   ```

4. **Set appropriate token limits**
   ```json
   { "Provider": "openai", "ModelName": "gpt-4-turbo", "MaxTokens": 32768 }
   ```

---

## Complete Example

See `appsettings.Full.json` for a complete configuration example with all options demonstrated.

## Related Documentation

- [Multiple LLM Backends Guide](./MULTIPLE_LLM_BACKENDS.md)
- [Chunking and Caching Guide](../CHUNKING_AND_CACHING.md)
- [Main README](../README.md)
- [Release Notes](../RELEASE_NOTES.md)
