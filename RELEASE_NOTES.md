# Release Notes

## v1.8.0 (2025-01-06)nk 

**NO BREAKING CHANGES** - All existing code continues to work!

### Major Features

#### Multiple LLM Backend Support
- **Multi-Provider Architecture**: Connect to multiple LLM providers simultaneously (Ollama, OpenAI, LM Studio)
- **Per-Request Selection**: Route requests to specific backends via `X-LLM-Backend` header or `?backend=` query param
- **Per-Backend Token Limits**: Configure `MaxTokens` for each backend to match model capabilities
- **Zero Breaking Changes**: Legacy single-backend configs work unchanged—automatically converted to new format
- **Provider Abstraction**: Unified `ILlmProvider` interface across different LLM services
- **Provider Factory**: `LlmProviderFactory` manages provider instances and routing
- **Resilience Integration**: Retry and circuit breaker policies work across all providers
- **Future-Ready**: Foundation for round-robin load balancing, health checks, and automatic failover

**Configuration:**
```json
{
  "LLMockApi": {
    "Backends": [
      {
        "Name": "ollama-llama3",
        "Provider": "ollama",
        "BaseUrl": "http://localhost:11434/v1/",
        "ModelName": "llama3",
        "MaxTokens": 8192,
        "Enabled": true
      },
      {
        "Name": "openai-gpt4",
        "Provider": "openai",
        "BaseUrl": "https://api.openai.com/v1/",
        "ModelName": "gpt-4",
        "ApiKey": "sk-...",
        "MaxTokens": 8192,
        "Enabled": true
      }
    ]
  }
}
```

**Per-Request Selection:**
```http
GET /api/mock/users?count=5
X-LLM-Backend: openai-gpt4
```

**Backward Compatibility:**
```json
{
  "BaseUrl": "http://localhost:11434/v1/",
  "ModelName": "llama3"
}
```
Still works! Automatically creates default Ollama backend.

#### Automatic Request Chunking
- **Intelligent Token Management**: Automatically breaks large requests into optimal chunks that fit within LLM token limits
- **Transparent Operation**: Works behind the scenes—no API changes required, enabled by default
- **Context Preservation**: Maintains consistency across chunks (IDs, names, relationships, data patterns)
- **Configurable**: Set `MaxOutputTokens` and `MaxItems` to match your LLM capabilities
- **Comprehensive Logging**: Detailed visibility into chunking decisions, strategy, and execution
- **Opt-Out**: Use `?autoChunk=false` query parameter to disable per-request
- **Token Estimation**: Smart analysis of shape complexity (nesting, arrays, properties) for accurate chunk sizing

#### Enhanced Cache Configuration
- **Sliding Expiration**: `CacheSlidingExpirationMinutes` (default: 15) - cache entries expire after inactivity
- **Absolute Expiration**: `CacheAbsoluteExpirationMinutes` (default: 60) - maximum cache lifetime
- **Size Limits**: `MaxItems` (default: 1000) - caps both response sizes AND total cache items
- **Cache Priority**: `CachePriority` (0-3) for memory management under pressure
- **Statistics Tracking**: `EnableCacheStatistics` for cache hit/miss monitoring
- **Compression**: `EnableCacheCompression` for memory-constrained environments
- **Refresh Threshold**: `CacheRefreshThresholdPercent` for future background pre-fetch

#### Context Storage Improvements
- **15-Minute Sliding Expiration**: API contexts automatically expire after inactivity
- **Memory Safety**: Prevents memory leaks with automatic cleanup and eviction callbacks
- **Sliding Window**: Context lifetime refreshes on each use
- **Case-Insensitive**: Context names work regardless of casing ("User-Session" = "user-session")
- **IMemoryCache Backend**: Consistent implementation with context store

### Configuration Examples

```json
{
  "MockLlmApi": {
    // Auto-Chunking (NEW)
    "MaxOutputTokens": 2048,
    "EnableAutoChunking": true,
    "MaxItems": 1000,

    // Enhanced Cache (NEW)
    "CacheSlidingExpirationMinutes": 15,
    "CacheAbsoluteExpirationMinutes": 60,
    "CacheRefreshThresholdPercent": 50,
    "CachePriority": 1,
    "EnableCacheStatistics": false,
    "EnableCacheCompression": false
  }
}
```

### Usage Examples

**Request 100 Items (Auto-Chunks):**
```http
GET /api/mock/users?count=100
```
Automatically splits into chunks, returns combined result.

**Disable Chunking:**
```http
GET /api/mock/users?count=100&autoChunk=false
```

**Logs:**
```
[INFO] Request needs chunking: 100 items × 150 tokens = 15000 tokens > 1536 available
[INFO] AUTO-CHUNKING ENABLED: Breaking request into 4 chunks (25 items/chunk)
[INFO] AUTO-CHUNKING: Executing chunk 1/4 (items 1-25 of 100)
[INFO] AUTO-CHUNKING COMPLETE: Combined 4 chunks into 100 items
```

### Documentation
- **[MULTIPLE_LLM_BACKENDS.md](./docs/MULTIPLE_LLM_BACKENDS.md)** - Complete guide to multi-provider configuration
- **[CHUNKING_AND_CACHING.md](./CHUNKING_AND_CACHING.md)** - Complete guide to chunking and caching systems
- **[ChunkingAndCaching.http](./ChunkingAndCaching.http)** - Ready-to-run HTTP examples (27 examples)
- **[README.md](./README.md)** - Updated with v1.8.0 features

### Testing
- All 191 tests passing (5 gRPC tests skipped)
- Build succeeded with zero errors
- Backward compatible with all previous versions

---

## v1.7.1 (2025-01-05)

**NO BREAKING CHANGES** - All existing code continues to work!

### Enhancements

#### gRPC Demo Page
- Added comprehensive gRPC demo page at `/grpc` with 4-panel interface
- Upload/manage .proto files with visual feedback
- Test JSON endpoints with syntax-highlighted responses
- Test binary Protobuf endpoints
- View server reflection data

### Bug Fixes
- Fixed HTTP 415 errors on gRPC binary endpoint by removing overly restrictive content-type filtering
- Fixed test compatibility with new API response format (definitions → protos)

## v1.7.0 (2025-01-05)

**NO BREAKING CHANGES** - All existing code continues to work!

### New Features

#### gRPC Service Mocking (MAJOR)

Mock gRPC services using Protocol Buffer definitions with LLM-powered response generation!

**Upload .proto Files**: Upload Protocol Buffer definition files to automatically generate gRPC service mocks
- Supports standard .proto syntax (proto3 and proto2)
- Parses service definitions, RPC methods, and message types
- Management API at `/api/grpc/proto` for uploading and managing proto files

**Dual Protocol Support**: Both JSON and binary Protobuf for different use cases
- **JSON over HTTP**: Easy testing with curl, Postman, or any HTTP client
  - Endpoint: `POST /api/grpc/{serviceName}/{methodName}`
  - Perfect for development and debugging
- **Binary Protobuf**: Production-grade gRPC wire protocol
  - Endpoint: `POST /api/grpc/proto/{serviceName}/{methodName}`
  - Compatible with real gRPC clients
  - Dynamic runtime serialization without code generation

**LLM-Powered Response Generation**: Realistic data matching your proto schemas
- Reads message definitions from uploaded .proto files
- Generates JSON shapes from proto field definitions
- Creates contextually appropriate mock data via LLM
- Serializes to binary Protobuf format for true gRPC responses

**Dynamic Protobuf Serialization**: Runtime encoding/decoding without protoc
- New `DynamicProtobufHandler` service for binary Protobuf operations
- Supports all Protobuf field types (scalar, nested messages, repeated fields)
- Handles wire types (varint, fixed32, fixed64, length-delimited)
- Proper field number ordering and encoding

**Management API Endpoints**:
```bash
# Upload proto file
POST /api/grpc/proto

# List all uploaded proto files
GET /api/grpc/proto

# Get specific proto file
GET /api/grpc/proto/{name}

# Delete proto file
DELETE /api/grpc/proto/{name}
```

**Service Call Endpoints**:
```bash
# JSON call (for testing)
POST /api/grpc/{serviceName}/{methodName}
Content-Type: application/json
{ "field": "value" }

# Binary Protobuf call (production-grade)
POST /api/grpc/proto/{serviceName}/{methodName}
Content-Type: application/grpc+proto
<binary protobuf data>
```

**Example Usage**:
```proto
syntax = "proto3";

service Greeter {
  rpc SayHello (HelloRequest) returns (HelloReply);
}

message HelloRequest {
  string name = 1;
}

message HelloReply {
  string message = 1;
  int32 request_id = 2;
}
```

After uploading this .proto file:
```bash
# Test with JSON
curl -X POST http://localhost:5116/api/grpc/Greeter/SayHello \
  -H "Content-Type: application/json" \
  -d '{"name": "World"}'

# Response: {"message": "Hello, World!", "request_id": 12345}

# Or use real gRPC clients with binary endpoint
grpcurl -d '{"name": "World"}' \
  -plaintext localhost:5116 \
  /api/grpc/proto/Greeter/SayHello
```

**Current Limitations** (documented in [GRPC_SUPPORT.md](./docs/GRPC_SUPPORT.md)):
- Unary RPC calls only (streaming support planned)
- Simplified binary parsing (does not decode actual request data)
- No server reflection support
- Field validation not enforced

#### SSE Streaming Improvements

**Progressive JSON Building**: Streaming responses now include accumulated content
- Each SSE event now sends both the new chunk AND the accumulated JSON so far
- Clients can see partial JSON structure as it builds
- Better user experience for progressive rendering
- Changed from: `{"chunk": "text", "done": false}`
- Changed to: `{"chunk": "text", "accumulated": "partial JSON...", "done": false}`

**Before**:
```javascript
data: {"chunk": "{", "done": false}
data: {"chunk": "\"id\"", "done": false}
data: {"chunk": ":", "done": false}
// Client sees raw fragments, can't parse until complete
```

**After**:
```javascript
data: {"chunk": "{", "accumulated": "{", "done": false}
data: {"chunk": "\"id\"", "accumulated": "{\"id\"", "done": false}
data: {"chunk": ":", "accumulated": "{\"id\":", "done": false}
// Client can display progressive JSON as it builds
```

### New Files

**gRPC Implementation**:
- `mostlylucid.mockllmapi/RequestHandlers/GrpcRequestHandler.cs` - Handles gRPC method invocations with LLM
- `mostlylucid.mockllmapi/Services/ProtoDefinitionManager.cs` - Manages uploaded .proto files
- `mostlylucid.mockllmapi/Services/ProtoParser.cs` - Parses .proto syntax into structured definitions
- `mostlylucid.mockllmapi/Services/DynamicProtobufHandler.cs` - Runtime Protobuf serialization/deserialization
- `mostlylucid.mockllmapi/GrpcManagementEndpoints.cs` - Proto file upload and management API
- `mostlylucid.mockllmapi/Models/ProtoDefinition.cs` - Model for proto file definitions
- `mostlylucid.mockllmapi/Models/ProtoService.cs` - Model for gRPC services
- `mostlylucid.mockllmapi/Models/ProtoMethod.cs` - Model for RPC methods
- `mostlylucid.mockllmapi/Models/ProtoMessage.cs` - Model for message types
- `mostlylucid.mockllmapi/Models/ProtoField.cs` - Model for message fields

**Documentation**:
- `docs/GRPC_SUPPORT.md` - Comprehensive gRPC feature guide (400+ lines)
  - Architecture diagrams using Mermaid
  - Complete .proto file examples
  - Testing strategies for both JSON and binary
  - Implementation details and limitations
  - Troubleshooting guide

### Updated Files

**Core Extensions**:
- `mostlylucid.mockllmapi/LLMockApiExtensions.cs` - Added gRPC endpoint registration
  - New `RegisterGrpcServices()` method
  - New `MapGrpcEndpoints()` method for both JSON and binary endpoints

**Streaming Handler**:
- `mostlylucid.mockllmapi/RequestHandlers/StreamingRequestHandler.cs` - Enhanced SSE with accumulated content
  - Modified lines 169-174 to include accumulated JSON in each event
  - Improved final event to include complete content

### Test Coverage

- **25 new gRPC tests** covering:
  - Proto file parsing (services, methods, messages, fields)
  - Proto management API (upload, list, get, delete)
  - Service calls (JSON and binary)
  - Error handling (missing proto, invalid methods)
- **All 196 tests passing** (171 existing + 25 new)
- Zero compilation errors or warnings

### Documentation Improvements

- Created comprehensive [gRPC Support Guide](./docs/GRPC_SUPPORT.md)
  - Gentle authoritative tone (blog post style)
  - Multiple Mermaid diagrams showing architecture and flow
  - Extensive code examples for both JSON and binary endpoints
  - "How It Works" sections with detailed explanations
  - Current limitations and known issues
  - Testing strategies and troubleshooting
- Updated README.md with gRPC as 6th major feature
- Updated "What's New" section for v1.7.0
- Added link to gRPC documentation in Feature Documentation section

### Configuration

No new configuration required! gRPC support works out of the box when you call:

```csharp
// Enable gRPC services
builder.Services.AddLLMockApi(builder.Configuration);

// Map gRPC endpoints
app.MapLLMockApi("/api/mock");
```

gRPC endpoints are automatically available at:
- `/api/grpc/{serviceName}/{methodName}` (JSON)
- `/api/grpc/proto/{serviceName}/{methodName}` (binary Protobuf)
- `/api/grpc/proto` (management API)

### Breaking Changes

**None!** This is a fully backward-compatible release.

### Upgrade Notes

- gRPC support is available immediately after upgrade
- Upload .proto files via management API
- Use JSON endpoints for quick testing
- Use binary endpoints for production-grade mocking
- All existing REST, GraphQL, SSE, and SignalR functionality unchanged

### Migration Examples

**Before (v1.6.1)**:
```csharp
// Your existing code
app.MapLLMockApi("/api/mock");
// Everything still works!
```

**After (v1.7.0 - new gRPC features available)**:
```bash
# Upload a proto file
curl -X POST http://localhost:5116/api/grpc/proto \
  -F "file=@greeter.proto" \
  -F "name=greeter"

# Call gRPC method with JSON
curl -X POST http://localhost:5116/api/grpc/Greeter/SayHello \
  -H "Content-Type: application/json" \
  -d '{"name": "World"}'

# Or use real gRPC clients with binary endpoint
grpcurl -d '{"name": "World"}' \
  -plaintext localhost:5116 \
  /api/grpc/proto/Greeter/SayHello
```

---

## v1.6.1 (2025-01-05)

**Documentation Fix**

### Fixed
- **Missing Release Notes**: Package maintainer forgot to update `release notes.txt` in v1.6.0 NuGet package
  - The v1.6.0 release went out with release notes still showing v1.5.0
  - This v1.6.1 release corrects that oversight
  - Added comprehensive v1.6.0 release notes to `release notes.txt` (included in NuGet package)
  - No code changes - purely documentation

### What Changed
- Updated `mostlylucid.mockllmapi/release notes.txt` with full v1.6.0 feature documentation
- Added this humble v1.6.1 entry acknowledging the oversight

### Apologies
Sorry for the confusion! The v1.6.0 features (Error Simulation and Context Management API) are all there and working perfectly - just the release notes file didn't get updated in the package.

**If you installed v1.6.0, you don't need to upgrade to v1.6.1** - they're functionally identical. This is purely a documentation fix.

---

## v1.6.0 (2025-01-05)

**NO BREAKING CHANGES** - All existing code continues to work!

### New Features

#### Comprehensive Error Simulation (MAJOR)

Test your client's error handling with production-like error responses across all endpoint types!

**Four Ways to Configure Errors** (in precedence order):

1. **Query Parameters** (highest precedence):
   ```bash
   GET /api/mock/users?error=404&errorMessage=Not%20found&errorDetails=User%20does%20not%20exist
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
   ?shape={"$error":404}

   # Complex: with message and details
   ?shape={"$error":{"code":422,"message":"Validation failed","details":"Email invalid"}}
   ```

4. **Request Body** (using `error` property):
   ```json
   {
     "error": {
       "code": 409,
       "message": "Conflict",
       "details": "User already exists"
     }
   }
   ```

**Supported HTTP Status Codes:**
- **4xx Client Errors**: 400, 401, 403, 404, 405, 408, 409, 422, 429
- **5xx Server Errors**: 500, 501, 502, 503, 504
- All codes include sensible default messages that can be overridden

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

**Works Everywhere:**
-  REST endpoints
-  Streaming (SSE) endpoints
-  GraphQL endpoint
-  SignalR hubs (via `HubContextConfig.ErrorConfig`)

**Use Cases:**
- Test retry logic and exponential backoff
- Validate error message display in UI
- Test authentication/authorization flows
- Simulate rate limiting scenarios (429)
- Practice graceful degradation patterns
- Test error logging and monitoring
- Client-side error boundary testing

**SignalR Error Simulation:**
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

#### API Context Management Endpoints (MAJOR)

Programmatically view, inspect, and modify API context history!

**New Management API** at `/api/contexts`:

- `GET /api/contexts` - List all contexts with summary info
- `GET /api/contexts/{name}` - Get full context details
  - Query params: `?includeCallDetails=true&maxCalls=10`
- `GET /api/contexts/{name}/prompt` - View formatted LLM prompt
- `POST /api/contexts/{name}/calls` - Manually add calls
- `PATCH /api/contexts/{name}/shared-data` - Update shared data
- `POST /api/contexts/{name}/clear` - Clear context history
- `DELETE /api/contexts/{name}` - Delete context completely
- `DELETE /api/contexts` - Clear all contexts

**Enable in Program.cs:**
```csharp
app.MapLLMockApiContextManagement("/api/contexts");
```

**Example Usage:**
```bash
# List all contexts
curl http://localhost:5000/api/contexts

# Get specific context
curl http://localhost:5000/api/contexts/checkout-flow

# View what LLM sees
curl http://localhost:5000/api/contexts/checkout-flow/prompt

# Manually add a call
curl -X POST http://localhost:5000/api/contexts/my-context/calls \
  -H "Content-Type: application/json" \
  -d '{
    "method": "GET",
    "path": "/api/mock/products/123",
    "responseBody": "{\"id\":\"123\",\"name\":\"Widget\",\"price\":29.99}"
  }'

# Update shared data
curl -X PATCH http://localhost:5000/api/contexts/my-context/shared-data \
  -H "Content-Type: application/json" \
  -d '{"userId":"user-789","cartId":"cart-456"}'
```

**Use Cases:**
- Debug context state during development
- Pre-populate contexts for testing
- Export context history for analysis
- Build automated test suites
- Inspect LLM input for debugging
- Monitor context growth over time

### New Files
- `mostlylucid.mockllmapi/Models/ErrorConfig.cs` - Error configuration model with default messages
- `mostlylucid.mockllmapi/ApiContextManagementEndpoints.cs` - Context management API endpoints
- `LLMApi.Tests/ErrorHandlingTests.cs` - 28 comprehensive error handling tests

### Documentation Improvements
- Added comprehensive error simulation section to README.md (~120 lines)
- Added error simulation section to CLAUDE.md with encoding guide
- Added 13 error examples to `LLMApi.http` covering all scenarios
- Added URL encoding documentation and warnings
- Added 90+ lines of context management examples to `contexts.http`
- All query parameter examples now properly URL-encoded with decoded comments

### Technical Details

**Error Handling Implementation:**
- `ErrorConfig` class with `ToJson()` and `ToGraphQLJson()` methods
- Automatic JSON escaping for safe output
- Error extraction in `ShapeExtractor` with precedence rules
- `$error` hints are sanitized from shapes before LLM prompt generation
- All request handlers (Regular, Streaming, GraphQL) support errors
- HTTP status codes are correctly set on responses

**URL Encoding Requirements:**
- ️ **IMPORTANT**: Query parameter values MUST be URL-encoded
- Common encodings: space→`%20`, `&`→`%26`, `:`→`%3A`, `'`→`%27`, `,`→`%2C`
- All `.http` file examples now include decoded comments for clarity
- Headers and body content do NOT require URL encoding

**Context Management:**
- Built on existing `OpenApiContextManager` service
- Returns `ApiContext` with full history and shared data
- Supports filtering with `includeCallDetails` and `maxCalls` parameters
- Proper 404 handling with helpful error messages listing available contexts

### Configuration

No new configuration required! Error simulation and context management work out of the box.

Optional: Map context management endpoints if needed:
```csharp
app.MapLLMockApiContextManagement("/api/contexts");
```

### Test Coverage

- **28 new error handling tests** covering:
  - `ErrorConfig` model with all default messages
  - Error extraction from all 4 sources (query, header, shape, body)
  - Error precedence rules
  - All request handler types (REST, GraphQL, Streaming)
  - Both JSON and GraphQL error formats
  - Special character escaping
- **All 171 tests passing** (143 existing + 28 new)
- Zero compilation errors or warnings introduced

### Breaking Changes

**None!** This is a fully backward-compatible release.

### Upgrade Notes
- Error simulation is opt-in - only triggered when error parameters are provided
- Context management API is opt-in - call `MapLLMockApiContextManagement()` to enable
- No changes to existing functionality or APIs
- All existing code works without modification

### Migration Examples

**Before (v1.5.0):**
```csharp
// Your code
app.MapLLMockApi("/api/mock");
// Everything still works!
```

**After (v1.6.0 - optional new features):**
```csharp
// Add context management
app.MapLLMockApi("/api/mock");
app.MapLLMockApiContextManagement("/api/contexts");

// Test error handling
// GET /api/mock/users?error=404
```

---

## v1.5.0 (2025-01-05)

**NO BREAKING CHANGES** - All existing code continues to work!

### New Features

#### API Contexts - Shared Memory Across Requests (MAJOR)

**Cross-Endpoint Context Support**: All endpoint types (REST, Streaming, GraphQL, SignalR) now support shared context for maintaining consistency across related requests!

- **Pass Context Name via**: Query param (`?context=name`), HTTP header (`X-Api-Context: name`), or request body (`{"context": "name"}`)
- **Alternative Parameter**: Use `api-context` instead of `context` for clarity
- **Automatic Context Tracking**: System remembers recent calls (method, path, request, response) in each context
- **Smart Data Extraction**: Automatically extracts and tracks IDs, names, emails from responses
- **Consistency Across Calls**: LLM generates new responses that maintain consistency with previous context history

**Use Cases:**
- **E-commerce Flows**: Create user → add items to cart → checkout - cart remembers user ID from first call
- **Stock Tickers**: Generate realistic price variance based on previous prices (no wild jumps)
- **Game State**: Track player stats, inventory, scores consistently across multiple API calls
- **User Journeys**: Multi-step workflows where later steps reference earlier data

**Example:**
```bash
# First call - create user
curl "http://localhost:5000/api/mock/users?context=session1" \
  -H "X-Response-Shape: {\"id\":0,\"name\":\"string\",\"email\":\"string\"}"
# Response: {"id": 12345, "name": "Alice", "email": "alice@example.com"}

# Second call - create order (LLM uses user data from context)
curl "http://localhost:5000/api/mock/orders?context=session1" \
  -H "X-Response-Shape: {\"orderId\":0,\"userId\":0,\"total\":0.0}"
# Response: {"orderId": 67890, "userId": 12345, "total": 49.99}
# Notice userId matches! LLM used context history
```

**SignalR Context Support**: SignalR hubs can now reference API contexts for realistic data variance:
```json
{
  "HubContexts": [
    {
      "Name": "stock-prices",
      "Description": "Stock ticker with realistic price changes",
      "ApiContextName": "stocks"  // NEW: References shared context
    }
  ]
}
```

#### Token Budget Management & Intelligent Truncation

- **MaxInputTokens Configuration**: Control token budget per model (default: 2048)
- **80/20 Priority Allocation**: Base prompt gets 80% of tokens, context history gets 20%
- **Automatic Truncation**: When context history exceeds budget, older calls are dropped
- **Detailed Logging**: See exactly which calls were dropped and why:
  ```
  Context 'session1': Dropped 3/8 calls to fit 2048 token limit (20% of max).
  Dropped: [GET /users/1, GET /users/2, POST /orders]
  ```
- **Token Estimation**: Approximates 1 token ≈ 4 characters for quick budget calculations

#### Model-Specific Configuration Examples

Added comprehensive configuration examples for 6 popular Ollama models in appsettings.json:
- **Llama 3 / 3.1** (4K context) - Recommended
- **TinyLlama** (2K context) - Fast but limited
- **Qwen 3 14B** (4K context) - Good quality
- **Mistral/Mixtral** (8K context) - High quality
- **Llama 2** (4K context) - Older
- **Phi-3** (4K context) - Microsoft

Each includes recommended `MaxInputTokens` values for optimal performance.

### New Files
- `mostlylucid.mockllmapi/Services/ContextExtractor.cs` - Extracts context names from requests
- `docs/API-CONTEXTS.md` - Complete guide to context features with Mermaid diagrams
- `docs/OPENAPI-FEATURES.md` - Complete guide to OpenAPI dynamic mocking features

### Documentation Improvements
- Added context usage examples to `management.http` (~220 lines of examples)
- Created comprehensive blog-format documentation with architecture diagrams
- Updated README with navigation to new feature docs
- Added troubleshooting guide for token limits

### Technical Details

**Context Extraction Precedence**: Query param > HTTP header > Request body

**Context Storage**: ConcurrentDictionary with case-insensitive keys for thread safety

**Context Lifecycle**:
1. Extract context name from request
2. Retrieve context history (recent calls, shared data, summary)
3. Build prompt with context history (20% of token budget)
4. Generate response using LLM
5. Store response in context for future calls

**Automatic Summarization**: When context exceeds 15 recent calls, older calls are summarized to save memory

**Shared Data Tracking**: Automatically extracts common patterns (IDs, names, emails) for consistency

### Configuration

```json
{
  "MockLlmApi": {
    "MaxInputTokens": 4096,  // NEW: Token budget per model

    // Model examples with recommended token limits
    "ModelName": "llama3",
    "Temperature": 1.2
  }
}
```

### Upgrade Notes
- Context support is opt-in - pass context parameter to enable
- MaxInputTokens defaults to 2048, adjust based on your model
- No breaking changes to existing functionality
- All 147 tests passing

---

## v1.2.1 (2025-01-04)

**Critical Bug Fix**

### Fixed
- **Retry Policy HTTP Request Reuse Issue**: Fixed a critical bug where `HttpRequestMessage` objects were being reused across retry attempts, causing "request has already been sent" errors
  - **Root Cause**: In .NET, `HttpRequestMessage` can only be sent once. When Polly's retry policy attempted to retry a failed request, it was trying to reuse the same request object
  - **Solution**: Now creates a fresh `HttpRequestMessage` inside the retry lambda for each attempt, ensuring clean retries
  - **Impact**: All three LLM client methods are now properly retry-safe:
    - `GetCompletionAsync()` - Regular completions
    - `GetStreamingCompletionAsync()` - Streaming responses
    - `GetNCompletionsAsync()` - Multiple completions
  - **Test Results**: All 92 unit tests now pass (previously had intermittent retry failures)

### Technical Details
Changed from this (broken):
```csharp
//  BAD: Request created once, reused for retries
var httpReq = new HttpRequestMessage(...);
await _resiliencePipeline.ExecuteAsync(async ct =>
    await client.SendAsync(httpReq, ct), cancellationToken);
```

To this (fixed):
```csharp
//  GOOD: Fresh request for each retry attempt
await _resiliencePipeline.ExecuteAsync(async ct => {
    using var httpReq = new HttpRequestMessage(...);
    return await client.SendAsync(httpReq, ct);
}, cancellationToken);
```

This ensures that retries work correctly when requests fail or timeout, making the resilience policies fully functional as intended.

---

## v1.2.0 (2025-01-04)

**NO BREAKING CHANGES** - All existing code works exactly as before. Full backward compatibility guaranteed!

### New Features

#### Polly Resilience Policies (MAJOR)
- **Exponential Backoff Retry**: Automatically retries failed LLM requests with exponential delays (1s, 2s, 4s...)
  - Configurable retry attempts (default: 3)
  - Jitter included to prevent thundering herd
  - Handles connection errors, timeouts, and HTTP errors
  - Comprehensive logging for each retry attempt
- **Circuit Breaker Pattern**: Protects against cascading failures
  - Opens after consecutive failures (default: 5)
  - Stays open for configured duration (default: 30 seconds)
  - Three states: Closed (normal), Open (rejecting), Half-Open (testing)
  - Logs all state transitions
- **Enabled by Default**: Both policies are active out of the box for production resilience
- **Fully Configurable**: All thresholds, delays, and behaviors can be customized via appsettings.json
- **Applies to All Protocols**: REST, GraphQL, SSE streaming, and SignalR all benefit from resilience policies

#### GraphQL API Support
- **Native GraphQL Endpoint**: POST to `/api/mock/graphql` with standard GraphQL queries
- **Query-Driven Data Generation**: The GraphQL query itself defines the response structure - no separate shape specification needed
- **Full GraphQL Request Support**:
  - Standard query syntax
  - Variables with type definitions
  - Operation names
  - Nested fields and relationships
- **Proper GraphQL Response Format**: Returns `{ "data": {...} }` or `{ "data": null, "errors": [...] }`
- **Intelligent LLM Integration**: Sends GraphQL query structure to LLM for contextually appropriate mock data generation

#### Modular Architecture (MAJOR)
- **Complete Protocol Independence**: Each protocol (REST, Streaming, GraphQL, SignalR) can now be added and mapped independently
- **New Modular Add Methods**:
  - `AddLLMockRest()` - REST only
  - `AddLLMockStreaming()` - SSE streaming only
  - `AddLLMockGraphQL()` - GraphQL only
  - `AddLLMockSignalR()` - SignalR only (already existed, now enhanced)
  - `AddLLMockApi()` - All protocols (backward compatible)
- **New Modular Map Methods**:
  - `MapLLMockRest(pattern)` - REST endpoints only
  - `MapLLMockStreaming(pattern)` - SSE endpoints only
  - `MapLLMockGraphQL(pattern)` - GraphQL endpoint only
  - `MapLLMockSignalR(hubPattern, managementPattern)` - SignalR (already existed)
  - `MapLLMockApi(pattern, includeStreaming, includeGraphQL)` - All protocols (backward compatible)
- **Smart Service Registration**: Services are only registered once, even when multiple Add methods are called
- **Benefits**:
  - Reduced memory footprint (30-40% less when using single protocol)
  - Faster startup time
  - Clearer code intent
  - Mix and match protocols as needed
  - Multiple instances with different configurations

#### Code Improvements
- **Shared JSON Extraction Utility**: Created `JsonExtractor.cs` to eliminate code duplication across request handlers
  - Handles markdown code blocks in LLM responses
  - Validates JSON structure
  - Extracts clean JSON from mixed-content responses
- **Refactored Request Handlers**: Updated `RegularRequestHandler` to use shared `JsonExtractor`
- **Separated Registration Logic**: Core services, REST, Streaming, GraphQL, and SignalR services are now registered independently

### API Changes

#### Backward Compatible
All existing code continues to work without any changes:
```csharp
// This still works exactly as before
builder.Services.AddLLMockApi(configuration);
app.MapLLMockApi("/api/mock", includeStreaming: true, includeGraphQL: true);
```

#### New Modular APIs
```csharp
// REST only
builder.Services.AddLLMockRest(configuration);
app.MapLLMockRest("/api/mock");

// GraphQL only
builder.Services.AddLLMockGraphQL(configuration);
app.MapLLMockGraphQL("/api/mock");

// Streaming only
builder.Services.AddLLMockStreaming(configuration);
app.MapLLMockStreaming("/api/mock");

// Mix and match
builder.Services.AddLLMockRest(configuration);
builder.Services.AddLLMockGraphQL(configuration);
app.MapLLMockRest("/api/rest");
app.MapLLMockGraphQL("/api/graphql");
```

### New Files
- `mostlylucid.mockllmapi/Services/JsonExtractor.cs` - Shared JSON extraction utility
- `mostlylucid.mockllmapi/RequestHandlers/GraphQLRequestHandler.cs` - GraphQL request handler

### Documentation
- Added comprehensive GraphQL section to README.md
- Added 5 GraphQL test examples to `LLMApi.http`:
  - Simple user query
  - Query with variables
  - Nested fields with arrays
  - E-commerce product catalog
  - Complex organizational data
- Updated feature list from 3 to 4 independent features

### Use Cases
GraphQL support is perfect for:
- Frontend developers working with GraphQL client libraries (Apollo, Relay, urql)
- Testing GraphQL integrations without backend implementation
- Prototyping GraphQL APIs with realistic nested data
- Demos requiring complex, hierarchical data structures
- Learning GraphQL query patterns with instant feedback

### Upgrade Notes
- GraphQL endpoint is enabled by default when you call `MapLLMockApi()`
- No breaking changes - existing REST and SSE endpoints work as before
- To disable GraphQL: `app.MapLLMockApi("/api/mock", includeGraphQL: false)`

---

## v1.1.0 (Previous Release)

### Features
- SignalR real-time data streaming
- Dynamic context creation and management
- Context lifecycle controls (start/stop/delete)
- Real-time connection count tracking
- Response caching for SignalR contexts
- Interactive demo applications
- JSON syntax highlighting in UI

---

## v1.0.x (Initial Releases)

### Features
- REST API mocking with LLM-powered data generation
- Server-Sent Events (SSE) streaming
- Shape control via header, query, or body
- Response caching via `$cache` hint
- Custom prompt templates
- Temperature control for data variety
- Multi-framework support (.NET 8.0 & 9.0)
