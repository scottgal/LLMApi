# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**mostlylucid.mockllmapi** (v2.1.0) is a production-ready, comprehensive ASP.NET Core mocking platform that generates realistic mock API responses using multiple LLM backends. It provides six independent features that can be used together or separately.

The repository contains three projects:
- **mostlylucid.mockllmapi**: The NuGet package library (main deliverable)
- **LLMApi**: Demo application showing all features
- **LLMApi.Tests**: Comprehensive test suite (16 test files)

## Key Features (All Optional - Use What You Need)

1. **REST API Mocking**: Wildcard endpoints with shape-based JSON generation
2. **GraphQL API Mocking**: Native GraphQL query support with proper error handling
3. **Server-Sent Events (SSE)**: Progressive streaming with multiple modes (tokens, objects, arrays)
4. **SignalR Real-Time**: WebSocket streaming with dynamic context management
5. **OpenAPI/Swagger**: Auto-generate endpoints from OpenAPI specs
6. **gRPC Support**: Dynamic .proto file loading and mock gRPC services

## Core Architecture

### Package Structure (mostlylucid.mockllmapi/)

The NuGet package follows a modular, service-oriented architecture:

#### Main Entry Points

**LLMockApiExtensions.cs** (48KB - largest file)
- Extension methods for all features:
  - `AddLLMockApi()`: Registers core services
  - `AddLLMockSignalR()`: Registers SignalR services
  - `AddLLMockOpenApi()`: Registers OpenAPI services
  - `MapLLMockApi()`: Maps REST/GraphQL/SSE endpoints
  - `MapLLMockSignalR()`: Maps SignalR hub and management endpoints
  - `MapLLMockOpenApi()`: Maps OpenAPI-generated endpoints
  - `MapLLMockGrpc()`: Maps gRPC service endpoints
  - `MapLLMockApiContextManagement()`: Maps context management API
  - `MapLLMockOpenApiManagement()`: Maps OpenAPI spec management API
  - `MapLLMockGrpcManagement()`: Maps gRPC proto management API
- Includes all endpoint implementations (REST, GraphQL, SSE, gRPC)

**LLMockApiOptions.cs** (295 lines)
- Configuration POCO with comprehensive settings
- Section name: `"MockLlmApi"` (NOT "mostlylucid.mockllmapi")
- Key properties:
  - Legacy: `BaseUrl`, `ModelName` (deprecated but still supported)
  - Modern: `LlmBackends` array for multiple backend support
  - Caching: `MaxCachePerKey`, `CacheSlidingExpirationMinutes`, `EnableCacheCompression`
  - Chunking: `EnableAutoChunking`, `MaxContextWindow`, `MaxItems`
  - SSE: `SseMode`, `EnableContinuousStreaming`, `ContinuousStreamingIntervalMs`
  - SignalR: `HubContexts`, `SignalRPushIntervalMs`
  - Resilience: `EnableRetryPolicy`, `EnableCircuitBreaker`, `MaxRetryAttempts`
  - GraphQL: `GraphQLMaxTokens` (default: 500, recommended: 200-300 for reliability)
  - OpenAPI: `OpenApiSpecs` array
  - Contexts: `ContextExpirationMinutes`

**LLMockApiService.cs** (minimal - 1.8KB)
- Legacy compatibility shim
- Most functionality moved to specialized services

#### Request Handlers (RequestHandlers/)

Each feature has a dedicated handler:

**RegularRequestHandler.cs**
- Handles non-streaming REST API requests
- Shape extraction and validation
- Error simulation support
- Cache integration

**StreamingRequestHandler.cs**
- Handles SSE streaming endpoints
- Three modes: LlmTokens, CompleteObjects, ArrayItems
- Continuous streaming support
- Chunking integration for large responses

**GraphQLRequestHandler.cs**
- Parses GraphQL queries and variables
- Extracts schema from query structure
- GraphQL-formatted error responses
- Token limit enforcement for reliability

**GrpcRequestHandler.cs**
- Dynamic protobuf message handling
- gRPC reflection support
- Request/response serialization

**OpenApiRequestHandler.cs**
- Route matching against OpenAPI specs
- Schema-driven response generation
- Parameter validation

#### Services Layer (Services/)

**Core Services:**

**LlmClient.cs**
- Central client for all LLM communication
- Uses LlmProviderFactory for multi-backend support
- Integrated resilience pipeline (Polly)
- Methods: `GetCompletionAsync()`, `GetStreamingCompletionAsync()`, `GetNCompletionsAsync()`
- Per-request backend selection via `HttpRequest` parameter

**PromptBuilder.cs**
- Builds prompts for all request types
- Randomness injection (GUID seed + timestamp)
- Template support for customization
- Context-aware prompts for chunking
- Special handling for GraphQL, gRPC, OpenAPI

**LlmBackendSelector.cs**
- Selects appropriate backend per request
- Precedence: `X-LLM-Backend` header > `?backend=` query > first enabled backend
- Falls back to legacy `BaseUrl`/`ModelName` if no backends configured

**ChunkingCoordinator.cs**
- Automatic chunking for large responses
- Maintains consistency across chunks
- Context tracking between chunks
- Enhanced prompts with explicit array formatting (v2.1 improvement)

**CacheManager.cs**
- In-memory response caching
- Configurable expiration (sliding and absolute)
- Optional compression
- Statistics tracking
- Variant management (max per key)

**Context Services:**

**IContextStore.cs** + **MemoryCacheContextStore.cs**
- Interface and implementation for context storage
- Request/response history tracking
- Automatic expiration after inactivity
- Used by API contexts and SignalR

**ContextExtractor.cs**
- Extracts contextual information from requests
- Maintains conversation continuity

**ShapeExtractor.cs**
- Extracts shape from query params, headers, or body
- Precedence: Query > Header > Body
- Error hint extraction and sanitization
- Removes `$error` and `$cache` from shapes before LLM processing

**OpenAPI Services:**

**DynamicOpenApiManager.cs**
- Loads OpenAPI specs from URLs or files
- Manages multiple specs simultaneously
- Route registration and matching

**OpenApiSpecLoader.cs**
- Fetches and parses OpenAPI/Swagger specs
- Supports both JSON and YAML
- URL and file path loading

**OpenApiSchemaConverter.cs**
- Converts OpenAPI schemas to JSON shapes
- Handles references and nested schemas

**OpenApiContextManager.cs**
- Per-spec context management
- Route-based context isolation

**gRPC Services:**

**DynamicProtobufHandler.cs**
- Dynamic .proto file compilation
- Protobuf message serialization/deserialization
- Type mapping and validation

**ProtoParser.cs**
- Parses .proto file syntax
- Extracts services and messages
- Dependency resolution

**ProtoDefinitionManager.cs**
- Manages uploaded .proto files
- In-memory storage
- Service enumeration

**GrpcReflectionService.cs**
- gRPC server reflection protocol
- Service discovery
- Method enumeration

**SignalR Services:**

**DynamicHubContextManager.cs**
- Manages SignalR context lifecycle
- Start/stop/pause/resume operations
- Dynamic context creation

**MockDataBackgroundService.cs**
- Background worker for SignalR data generation
- Periodic push to connected clients
- Respects context active state

**Helper Services:**

**JsonExtractor.cs**
- Extracts valid JSON from LLM responses
- Handles partial/malformed JSON
- Markdown code block detection

**DelayHelper.cs**
- Random delays for realistic simulation
- Configurable min/max delays

#### LLM Provider Architecture (Services/Providers/)

**ILlmProvider.cs**
- Provider abstraction interface
- Methods: `GetCompletionAsync()`, `GetStreamingCompletionAsync()`, `GetNCompletionsAsync()`, `ConfigureClient()`
- Property: `Name` (provider identifier)

**OllamaProvider.cs**
- Default provider for local Ollama instances
- OpenAI-compatible API format
- Optional API key support
- Base URL: `http://localhost:11434/v1/`

**OpenAIProvider.cs**
- Official OpenAI API provider
- Required API key (throws if missing)
- Full streaming and n-completions support
- Base URL: `https://api.openai.com/v1/`

**LMStudioProvider.cs**
- Local LM Studio provider
- OpenAI-compatible format
- Optional API key
- Base URL: `http://localhost:1234/v1/`

**LlmProviderFactory.cs**
- Factory pattern for provider management
- Registry: `"ollama"`, `"openai"`, `"lmstudio"`
- Fallback to Ollama for unknown providers
- Creates provider instances with configuration

**Provider Selection Flow:**
1. Check for `X-LLM-Backend` header in request
2. Check for `?backend=` query parameter
3. Fall back to first enabled backend in `LlmBackends` array
4. If no backends configured, create default Ollama backend from legacy `BaseUrl`/`ModelName`

#### Models (Models/)

**LlmBackendConfig.cs**
- Backend configuration POCO
- Properties: `Name`, `Provider`, `BaseUrl`, `ModelName`, `ApiKey`, `Enabled`, `Weight`, `MaxTokens`

**HubContextConfig.cs**
- SignalR context configuration
- Properties: `Name`, `Description`, `IsActive`, `ErrorConfig`

**OpenApiSpecConfig.cs**
- OpenAPI spec configuration
- Properties: `Name`, `SourceUrl`, `FilePath`, `MountPath`

**ProtoDefinition.cs**
- gRPC .proto file definition
- Properties: `FileName`, `Content`, `Services`, `Messages`

**ErrorConfig.cs**
- Error simulation configuration
- Default messages for HTTP status codes
- JSON and GraphQL formatting methods

**ShapeInfo.cs**
- Shape metadata with error hints
- Properties: `CleanedShape`, `ErrorCode`, `ErrorMessage`, `ErrorDetails`

**SseMode.cs** (enum)
- SSE streaming modes: `LlmTokens`, `CompleteObjects`, `ArrayItems`

**CacheEntry.cs**
- Cache entry model with metadata

**ChatCompletionModels.cs**
- OpenAI-compatible request/response models

#### SignalR Hubs (Hubs/)

**MockLlmHub.cs**
- Main SignalR hub for real-time data
- Methods: `Subscribe(contextName)`, `Unsubscribe(contextName)`
- Receives pushes from background service

**OpenApiHub.cs**
- SignalR hub for OpenAPI-based streaming
- Context-based subscriptions

#### Management Endpoints

**ApiContextManagementEndpoints.cs**
- REST API for context management
- Endpoints: list, get, create, update, delete, pause, resume
- Mounted at `/api/contexts` (configurable)

**OpenApiManagementEndpoints.cs**
- REST API for OpenAPI spec management
- Endpoints: load, list, get, unload, describe
- Mounted at `/api/openapi` (via MapLLMockOpenApiManagement)

**GrpcManagementEndpoints.cs**
- REST API for gRPC proto management
- Endpoints: upload, list, get, delete, services
- Mounted at `/api/grpc-protos` (configurable)

**SignalRManagementEndpoints.cs**
- REST API for SignalR context management
- Endpoints: contexts, start, stop, create, delete
- Mounted at `/api/signalr` (via MapLLMockSignalR)

### Request Flow (REST API Example)

1. HTTP request arrives at `/api/mock/users?shape={"name":"string","age":0}`
2. `RegularRequestHandler` processes the request
3. `ShapeExtractor` extracts shape from query parameter
4. `LlmBackendSelector` determines which backend to use
5. `PromptBuilder` creates prompt with shape, randomness, and context
6. `CacheManager` checks for cached responses
7. If not cached, `LlmClient` calls `LlmProviderFactory` to get provider
8. Provider makes HTTP call to LLM (Ollama/OpenAI/LMStudio)
9. Resilience pipeline (Polly) handles retries and circuit breaking
10. `JsonExtractor` parses JSON from LLM response
11. Response cached and returned to client

### Key Design Decisions

- **Temperature 1.2**: High randomness ensures varied data across requests
- **Shape precedence**: Query param > Header > Body field
- **Context window allocation**: 75% input, 25% output (MaxContextWindow * 0.75/0.25)
- **Chunking**: Automatic when response would exceed MaxOutputTokens
- **GraphQL token limits**: Lower values (200-300) recommended for reliability
- **Streaming format**: SSE with three modes (tokens, objects, arrays)
- **HttpClientFactory**: Proper disposal and timeout management
- **Resilience**: Exponential backoff + circuit breaker (Polly)
- **Cache**: Sliding + absolute expiration, optional compression
- **Thread safety**: All services registered as Scoped or Singleton appropriately

## Configuration

### Demo App (LLMApi/appsettings.json)

The demo uses clean, minimal configuration with references to external docs:

```json
{
  "MockLlmApi": {
    "LlmBackends": [
      {
        "Name": "ollama",
        "Provider": "ollama",
        "BaseUrl": "http://localhost:11434/v1/",
        "ModelName": "llama3",
        "Enabled": true,
        "Weight": 1
      },
      {
        "Name": "lmstudio",
        "Provider": "lmstudio",
        "BaseUrl": "http://localhost:1234/v1/",
        "ModelName": "meta-llama-3.1-8b-instruct",
        "Enabled": true,
        "Weight": 1
      }
    ],
    "Temperature": 1.2,
    "MaxContextWindow": 8192,
    "TimeoutSeconds": 30,
    "EnableRetryPolicy": true,
    "MaxRetryAttempts": 3,
    "EnableCircuitBreaker": true,
    "GraphQLMaxTokens": 500,
    "SignalRPushIntervalMs": 5000,
    "HubContexts": [
      {
        "Name": "weather",
        "Description": "Realistic weather data...",
        "IsActive": false
      }
    ]
  }
}
```

**See `docs/OLLAMA_MODELS.md` for detailed model configurations and hardware requirements.**

### Demo App (LLMApi/Program.cs)

Comprehensive example showing all features:

```csharp
using mostlylucid.mockllmapi;

var builder = WebApplication.CreateBuilder(args);

// Register all LLMock API features (modular - use what you need)
builder.Services.AddLLMockApi(builder.Configuration);
builder.Services.AddLLMockSignalR(builder.Configuration);
builder.Services.AddLLMockOpenApi(builder.Configuration);

var app = builder.Build();

app.UseRouting();

// Map all features (modular - use what you need)
app.MapLLMockApi("/api/mock", includeStreaming: true);
app.MapLLMockSignalR("/hub/mock", "/api/mock");
app.MapLLMockOpenApi();
app.MapLLMockOpenApiManagement();
app.MapLLMockApiContextManagement("/api/contexts");
app.MapLLMockGrpcManagement("/api/grpc-protos");
app.MapLLMockGrpc("/api/grpc");

app.Run();
```

## Development Commands

### Build Everything

```bash
# Build solution
dotnet build LLMApi.sln

# Build NuGet package
dotnet pack mostlylucid.mockllmapi/mostlylucid.mockllmapi.csproj -c Release
# Output: mostlylucid.mockllmapi/bin/Release/mostlylucid.mockllmapi.2.1.0.nupkg

# Install locally for testing
dotnet add package mostlylucid.mockllmapi --source ./mostlylucid.mockllmapi/bin/Release
```

### Run Demo Application

```bash
# From solution root
dotnet run --project LLMApi/LLMApi.csproj

# Endpoints will be available at:
# - http://localhost:5116/api/mock/** (REST/GraphQL/SSE)
# - http://localhost:5116/hub/mock (SignalR)
# - http://localhost:5116/api/contexts (Context management)
# - http://localhost:5116/api/openapi (OpenAPI management)
# - http://localhost:5116/api/grpc-protos (gRPC management)
# - http://localhost:5116/api/grpc (gRPC services)
# - http://localhost:5116/swagger (Swagger UI)
```

### Testing

```bash
# Run all tests
dotnet test

# Run with verbose output
dotnet test --verbosity detailed

# Test specific project
dotnet test LLMApi.Tests/LLMApi.Tests.csproj

# Run specific test
dotnet test --filter "FullyQualifiedName~ShapeExtractor"
```

Test project structure (16 test files):
- Core: `PromptBuilderTests.cs`, `ShapeExtractorTests.cs`, `JsonExtractorTests.cs`
- Providers: `LlmProviderFactoryTests.cs`, `OllamaProviderTests.cs`
- OpenAPI: `OpenApiSchemaConverterTests.cs`, `OpenApiSpecLoaderTests.cs`
- gRPC: `ProtoParserTests.cs`, `DynamicProtobufHandlerTests.cs`
- Chunking: `ChunkingCoordinatorTests.cs`
- Context: `ContextExtractorTests.cs`
- Config: `LLMockApiOptionsTests.cs`

### Testing with HTTP Files

The demo includes comprehensive HTTP test files:

- **LLMApi.http** (70+ tests): Complete validation suite covering all features
- **contexts.http**: API context management examples
- **grpc.http**: gRPC proto upload and service calls
- **management.http**: SignalR and OpenAPI management
- **SSE_Streaming.http**: SSE streaming modes and continuous streaming
- **ChunkingAndCaching.http**: Chunking and cache validation

Use with Visual Studio, Rider, or REST Client extension.

## Common Development Tasks

### Change NuGet Package Version

Edit `mostlylucid.mockllmapi/mostlylucid.mockllmapi.csproj`:
```xml
<Version>2.2.0</Version>
```

Also update:
- `LLMApi/Program.cs` (Swagger version)
- `README.md` (version badge and header)
- `RELEASE_NOTES.md` (add new entry)
- `mostlylucid.mockllmapi/release notes.txt` (used in package metadata)

### Add New Configuration Option

1. Add property to `LLMockApiOptions.cs` with XML comment
2. Use in appropriate service (e.g., `LlmClient.cs`, `ChunkingCoordinator.cs`)
3. Document in `docs/CONFIGURATION_REFERENCE.md`
4. Add example to `LLMApi/appsettings.Full.json`
5. Add test in `LLMockApiOptionsTests.cs`

### Add New LLM Provider

1. Create `Services/Providers/MyProviderProvider.cs` implementing `ILlmProvider`
2. Register in `LlmProviderFactory.cs` registry
3. Add provider name to `LlmBackendConfig` documentation
4. Add example to `docs/MULTIPLE_LLM_BACKENDS.md`
5. Add tests in `LLMApi.Tests/Providers/MyProviderProviderTests.cs`

### Add New Request Handler

1. Create `RequestHandlers/MyFeatureRequestHandler.cs`
2. Add endpoint mapping in `LLMockApiExtensions.cs`
3. Add configuration to `LLMockApiOptions.cs` if needed
4. Add service registration in `AddLLMockApi()` or new `AddLLMock[Feature]()`
5. Add management endpoints if needed
6. Add tests and HTTP file examples

### Customize Prompts

Prompts are centralized in `PromptBuilder.cs`:
- `BuildRegularPrompt()`: REST API requests
- `BuildStreamingPrompt()`: SSE streaming
- `BuildGraphQLPrompt()`: GraphQL queries
- `BuildGrpcPrompt()`: gRPC calls
- `BuildOpenApiPrompt()`: OpenAPI-based requests

Users can override via `CustomPromptTemplate` in configuration.

## Important Implementation Notes

### Framework and Compatibility

- **Target frameworks**: .NET 8.0 and .NET 9.0 (multi-targeted)
- **Demo project**: Runs on .NET 8.0
- **Package dependencies**: ASP.NET Core (framework reference), Polly, Google.Protobuf, Grpc.AspNetCore, Microsoft.OpenApi.Readers
- **Backward compatibility**: v2.1 is fully compatible with v2.0, legacy `BaseUrl`/`ModelName` still supported

### Architecture Patterns

- **Modular design**: Six independent features, use what you need
- **Dependency injection**: All services properly registered (Scoped/Singleton)
- **Factory pattern**: LlmProviderFactory for multi-provider support
- **Strategy pattern**: Multiple request handlers, providers
- **Repository pattern**: IContextStore abstraction
- **Background services**: MockDataBackgroundService for SignalR
- **Endpoint routing**: Requires `UseRouting()` before mapping endpoints

### Performance and Reliability

- **HttpClientFactory**: Required dependency, proper connection pooling
- **Resilience (Polly)**: Exponential backoff retry + circuit breaker
- **Caching**: In-memory with sliding and absolute expiration
- **Chunking**: Automatic for large responses, maintains consistency
- **Context expiration**: Automatic cleanup after inactivity (default: 15 min)
- **Thread safety**: Services designed for concurrent requests

### Security Considerations

- **Prompt injection**: User input goes directly to LLM - NOT for production with untrusted input
- **API keys**: OpenAI provider requires API key, stored in configuration
- **No authentication**: Demo has no auth - add your own for production
- **CORS**: Not configured in demo - add as needed
- **Rate limiting**: Not implemented - consider for production

### Limitations and Known Issues

- **No JSON validation**: Invalid shapes pass through to LLM (may produce unexpected results)
- **Chunking reliability**: Some models struggle with array formatting at high temperatures (see `docs/OLLAMA_MODELS.md`)
- **GraphQL complexity**: Limited to simple queries, no introspection or schema validation
- **gRPC reflection**: Basic implementation, may not support all reflection features
- **Memory usage**: Large contexts and caches can consume significant memory

## Publishing NuGet Package

### Automated Publishing via GitHub Actions (Recommended)

This project uses **NuGet Trusted Publishers** for secure OIDC-based publishing.

**One-time setup** (see `.github/NUGET_SETUP.md`):
1. Add `NUGET_USERNAME` secret to GitHub
2. Upload v1.0.0 manually to NuGet.org (first version only)
3. Configure Trusted Publishing Policy on NuGet.org:
   - Owner: `scottgal`
   - Repository: `LLMApi`
   - Workflow: `publish-nuget.yml`

**Publishing a new version**:
```bash
# 1. Update version in mostlylucid.mockllmapi/mostlylucid.mockllmapi.csproj
# 2. Update RELEASE_NOTES.md and release notes.txt
# 3. Commit changes
# 4. Create and push a version tag:
git tag v2.2.0
git push origin v2.2.0
```

The GitHub Action automatically:
1. Builds the solution
2. Runs all tests
3. Packs the NuGet package
4. Requests OIDC token from GitHub
5. Exchanges for short-lived NuGet API key (1-hour validity)
6. Publishes to NuGet.org

### Manual Publishing

```bash
# Build release package
dotnet pack mostlylucid.mockllmapi/mostlylucid.mockllmapi.csproj -c Release

# Publish to NuGet.org (requires API key)
dotnet nuget push mostlylucid.mockllmapi/bin/Release/mostlylucid.mockllmapi.2.1.0.nupkg \
  --api-key YOUR_API_KEY \
  --source https://api.nuget.org/v3/index.json

# Or publish to local feed for testing
dotnet nuget push mostlylucid.mockllmapi/bin/Release/mostlylucid.mockllmapi.2.1.0.nupkg \
  --source ~/local-nuget-feed
```

## Error Simulation

The package includes comprehensive error simulation for testing client error handling.

### Error Configuration Methods (Precedence Order)

1. **Query Parameters** (highest)
   ```http
   GET /api/mock/users?error=404&errorMessage=Not%20found&errorDetails=User%20not%20found
   ```
   **IMPORTANT**: URL-encode special characters (space→`%20`, `&`→`%26`, `:`→`%3A`)

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
   GET /api/mock/users?shape={"$error":{"code":422,"message":"Validation failed"}}
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

**REST/SSE endpoints**:
```json
{
  "error": {
    "code": 404,
    "message": "Not Found",
    "details": "Optional context"
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
        "details": "Optional context"
      }
    }
  ]
}
```

**See `LLMApi/LLMApi.http` for 20+ error simulation examples.**

## Troubleshooting

### Common Issues

**"MapLLMockApi requires endpoint routing"**
- Solution: Call `app.UseRouting()` before any `Map*()` calls

**LLM returns empty responses**
- Check Ollama is running: `curl http://localhost:11434`
- Verify model is installed: `ollama list`
- Check `BaseUrl` or `LlmBackends` configuration
- Review logs with `EnableVerboseLogging: true`

**Streaming not working**
- Ensure `Accept: text/event-stream` header
- Check `includeStreaming: true` in `MapLLMockApi` call
- Verify LLM supports streaming

**Data not random enough**
- Increase `Temperature` (try 1.5-2.0)
- Check prompts include random seed (automatic)
- Try different model (some are more creative)

**Chunking produces invalid JSON**
- Lower temperature (try 0.7-1.0)
- Use better model (see `docs/OLLAMA_MODELS.md`)
- Check `MaxContextWindow` matches model's actual context
- Review chunk size calculation

**Timeouts**
- Increase `TimeoutSeconds` in configuration
- Check LLM server performance
- Use faster/smaller model
- Enable `EnableRetryPolicy` for transient failures

**Circuit breaker open**
- LLM backend is failing repeatedly
- Check backend health
- Review `CircuitBreakerFailureThreshold` and `CircuitBreakerDurationSeconds`
- Check logs for underlying errors

**SignalR not receiving data**
- Ensure context is active (`IsActive: true`)
- Check `SignalRPushIntervalMs` configuration
- Verify client subscribed to correct context name
- Review background service logs

**OpenAPI spec not loading**
- Check URL/file path is valid
- Ensure spec is valid OpenAPI 3.0+ or Swagger 2.0
- Review logs for parsing errors
- Test spec at https://editor.swagger.io

**gRPC calls failing**
- Verify .proto file is valid
- Check service and method names are correct
- Ensure message types match request
- Review protobuf compilation errors in logs

## Documentation Map

The project includes extensive documentation:

### User Guides
- **README.md**: Main package documentation and quick start
- **MODULAR_EXAMPLES.md**: Modular usage patterns for each feature
- **SIGNALR_DEMO_GUIDE.md**: Complete SignalR setup and usage
- **CHUNKING_AND_CACHING.md**: Chunking behavior and cache usage

### Feature Documentation (docs/)
- **API-CONTEXTS.md**: Context management and history tracking
- **CONTINUOUS_STREAMING.md**: Continuous SSE streaming guide
- **SSE_STREAMING_MODES.md**: SSE modes (tokens, objects, arrays)
- **GRPC_SUPPORT.md**: gRPC setup and .proto file management
- **OPENAPI-FEATURES.md**: OpenAPI/Swagger integration
- **MULTIPLE_LLM_BACKENDS.md**: Multi-backend configuration

### Reference Documentation (docs/)
- **BACKEND_API_REFERENCE.md**: Complete endpoint reference (600+ lines)
- **CONFIGURATION_REFERENCE.md**: All configuration options
- **OLLAMA_MODELS.md**: Model configurations and recommendations (285 lines)

### Release Information
- **RELEASE_NOTES.md**: Complete version history
- **mostlylucid.mockllmapi/release notes.txt**: Included in package

### Advanced Topics
- **SEMANTIC_COGNITION_MESH.md**: Architectural concepts and patterns

## License

This project is released into the public domain under the [Unlicense](https://unlicense.org). You are free to use, modify, and distribute this code without restriction.

## Version History Summary

- **v2.1.0** (2025-01-06): Quality release - enhanced chunking, 70+ HTTP tests, clean config, full Swagger docs
- **v2.0.0**: Skipped to refine v2.1 features
- **v1.8.0**: Multi-backend support (Ollama, OpenAI, LMStudio)
- **v1.7.0**: gRPC support and .proto file management
- **v1.6.0**: Context management and history tracking
- **v1.5.0**: OpenAPI/Swagger integration
- **v1.4.0**: SignalR real-time streaming
- **v1.3.0**: SSE streaming modes
- **v1.2.0**: GraphQL support
- **v1.1.0**: Caching and resilience
- **v1.0.0**: Initial release with REST API mocking

**See RELEASE_NOTES.md for complete details.**
