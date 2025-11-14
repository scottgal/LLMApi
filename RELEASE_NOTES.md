# Release Notes

## v2.2.0 - Pluggable Tools & Pre-Configured APIs

**Focus**: MCP-compatible tool system, pre-configured REST APIs, and intelligent context memory

This release introduces a powerful pluggable tools system for calling external APIs and chaining mock endpoints, along with pre-configured REST API definitions that eliminate repetitive configuration. Context memory is now self-managing with automatic expiration and comprehensive field extraction.

### Major Features

#### 1. Pluggable Tools & Actions System 🔥

**Problem Solved**: No way to call external APIs or chain mock endpoints to create realistic workflows and decision trees.

**Solution**: MCP-compatible tool system with HTTP and Mock tool executors

**Tool Types**:

**HTTP Tools** - Call external REST APIs:
```json
{
  "Name": "getUserData",
  "Type": "http",
  "HttpConfig": {
    "Endpoint": "https://api.example.com/users/{userId}",
    "Method": "GET",
    "AuthType": "bearer",
    "AuthToken": "${API_TOKEN}",  // Environment variable support
    "ResponsePath": "$.data"       // JSONPath extraction
  }
}
```

**Mock Tools** - Chain mock endpoints for decision trees:
```json
{
  "Name": "getOrderHistory",
  "Type": "mock",
  "MockConfig": {
    "Endpoint": "/api/mock/users/{userId}/orders",
    "Method": "GET",
    "QueryParams": { "limit": "10" },
    "Shape": "{\"orders\":[{\"id\":\"string\",\"total\":0.0}]}"
  }
}
```

**Key Features**:
- **Template Substitution**: `{paramName}` placeholders in URLs, headers, bodies
- **Environment Variables**: `${ENV_VAR_NAME}` for secure credential storage
- **Authentication**: Bearer, Basic, API Key support
- **JSONPath Extraction**: Extract specific fields from responses
- **Tool Chaining**: Execute multiple tools in sequence
- **Safety Limits**: MaxToolChainDepth and MaxConcurrentTools prevent runaway execution
- **Result Caching**: Cache expensive operations per request
- **MCP Compatible**: Architecture ready for Model Context Protocol integration

**Execution Modes**:
- `Disabled`: Tools not available (default)
- `Explicit`: Call via `?useTool=name` or `X-Use-Tool` header (Phase 1 - current)
- `LlmDriven`: LLM decides which tools to call (Phase 2 - future)

**Usage Example**:
```http
# Execute tool explicitly
GET /api/mock/user-profile?useTool=getUserData&userId=123

# Chain multiple tools
GET /api/mock/dashboard?useTool=getUserData,getOrderHistory&userId=123

# Tool results automatically merged into LLM context
Response: {
  "user": { /* enriched with real API data */ },
  "orders": [ /* from tool chain */ ]
}
```

**Documentation**: `docs/TOOLS_ACTIONS.md` (500+ lines)

#### 2. Pre-Configured REST APIs 🔥

**Problem Solved**: Repetitive configuration for common API patterns - shape, context, tools, rate limiting, etc.

**Solution**: Define complete API configurations once, call by name

**Configuration Example**:
```json
{
  "RestApis": [
    {
      "Name": "user-profile",
      "Method": "GET",
      "Path": "profile/{userId}",
      "Shape": "{\"user\":{},\"orders\":[],\"recommendations\":[]}",
      "ContextName": "user-session",
      "Tools": ["getUserData", "getOrderHistory"],
      "Tags": ["users", "composite"],
      "CacheCount": 5,
      "RateLimitDelay": "500-1000"
    }
  ]
}
```

**Supported Features**:
- **Shape Specification**: JSON shape or reference to OpenAPI spec
- **OpenAPI Integration**: `OpenApiSpec` + `OpenApiOperationId` for schema-driven generation
- **Shared Context**: Automatic context management via `ContextName`
- **Tool Integration**: Pre-configure which tools to execute
- **Tags/Groups**: Organize APIs by category
- **Rate Limiting**: Per-API delay configuration
- **N-Completions**: Generate multiple variants
- **Streaming**: Enable SSE streaming
- **Error Simulation**: Built-in error configuration
- **Default Parameters**: Query params and headers

**Benefits**:
- **Zero Repetition**: Configure once, use everywhere
- **Consistent Testing**: Same shape/context across team
- **Quick Prototyping**: Call by name instead of building requests
- **Documentation**: APIs are self-documenting in config

**Usage** (via management endpoints - coming in v2.2.1):
```http
# List all configured APIs
GET /api/configured

# Execute configured API
GET /api/configured/user-profile?userId=123
```

**appsettings.Full.json**: 8 complete examples including e-commerce workflows, monitoring APIs, and test scenarios

#### 3. Dynamic Context Memory with Automatic Expiration 🔥

**Problem Solved**: Context memory previously accumulated indefinitely, requiring manual cleanup and potentially causing memory leaks in long-running test sessions or CI/CD pipelines.

**Solution**: ASP.NET Core `IMemoryCache` with sliding expiration

```csharp
// NEW: Automatic expiration with sliding window
public class MemoryCacheContextStore : IContextStore
{
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _slidingExpiration; // Default: 15 minutes

    // Contexts automatically expire after inactivity
    // Every access refreshes the expiration timer
    // Zero risk of memory leaks!
}
```

**Key Benefits**:
- **Zero Manual Cleanup**: Contexts disappear automatically after 15 minutes of inactivity
- **Sliding Expiration**: Timer resets on every API call - active contexts never expire
- **Memory Efficient**: Perfect for CI/CD pipelines - no state accumulation between runs
- **Configurable Duration**: Set from 5 minutes to 24 hours based on your needs

**Configuration**:
```json
{
  "mostlylucid.mockllmapi": {
    "ContextExpirationMinutes": 15  // Default: 15 (range: 5-1440)
  }
}
```

**Recommended Settings**:
- **5 minutes**: Quick unit tests, CI/CD pipelines
- **15 minutes**: Default - general development testing (recommended)
- **30 minutes**: Complex multi-step workflows
- **60+ minutes**: Long exploratory testing sessions

#### 2. Intelligent Shared Data Extraction

**Problem Solved**: Only hardcoded fields (`id`, `userId`, `name`, `email`) were tracked, limiting consistency for domain-specific fields like SKUs, account numbers, reference codes, etc.

**Solution**: Recursive field extraction that captures **ALL** fields automatically

**Before (v2.1.0)**:
```json
// Only 4 hardcoded fields extracted
{"lastId": "123", "lastUserId": "456", "lastName": "Alice", "lastEmail": "alice@example.com"}
```

**Now (v2.2.0)**:
```json
// ALL fields extracted up to 2 levels deep
{
  "orderId": "123",
  "sku": "WIDGET-01",
  "price": "49.99",
  "customer.customerId": "456",
  "customer.tier": "gold",
  "customer.address.city": "Seattle",  // Nested 2 levels
  "items.length": "1",
  "items[0].productId": "789",
  "items[0].quantity": "2",
  // Legacy keys still work:
  "lastId": "123"
}
```

**New Capabilities**:
- **Nested Objects**: Extracts `address.city`, `payment.cardNumber` with dot notation
- **Array Tracking**: Stores array lengths (`items.length: 5`)
- **First Item Data**: Captures `items[0].productId` automatically
- **Custom Fields**: Any domain-specific field is now tracked
- **Unlimited Domains**: Works for e-commerce, finance, healthcare, gaming, etc.
- **Backward Compatible**: Legacy `lastId`, `lastUserId` keys still work

**Real-World Impact**:
```http
### Create order with SKU and tier
POST /api/mock/orders?context=checkout
Response: {"orderId": 123, "sku": "WIDGET-01", "customer": {"tier": "gold"}}

### Apply discount - automatically uses same SKU and tier!
POST /api/mock/discounts?context=checkout
Response: {
  "discountId": 789,
  "sku": "WIDGET-01",      ← Same SKU from previous call
  "customerTier": "gold",  ← Same tier from previous call
  "discount": 0.15
}
```

The LLM now sees **all** extracted fields in the prompt and maintains consistency across domain-specific data without any configuration.

### Technical Details

**Pluggable Tools System**:

**ToolConfig** (`Models/ToolConfig.cs`):
- Comprehensive configuration model for all tool types
- Support for HTTP, Mock, and extensible tool types
- Parameter schemas with type validation
- Timeout and caching configuration

**HttpToolExecutor** (`Services/Tools/HttpToolExecutor.cs`):
- Template substitution in URLs, headers, bodies using `{paramName}`
- Environment variable resolution with `${ENV_VAR_NAME}`
- Authentication: Bearer, Basic, API Key
- JSONPath response extraction
- Comprehensive error handling

**MockToolExecutor** (`Services/Tools/MockToolExecutor.cs`):
- Calls other mock endpoints for decision tree workflows
- Supports all mock endpoint features (shape, context, etc.)
- Enables complex multi-step API simulations

**ToolRegistry** (`Services/Tools/ToolRegistry.cs`):
- Discovers and validates configured tools
- Extensible executor registration via `IToolExecutor` interface
- Generates tool definitions for LLM (Phase 2 ready)

**ToolOrchestrator** (`Services/Tools/ToolOrchestrator.cs`):
- Coordinates tool execution with safety limits
- MaxToolChainDepth tracking per request (prevents infinite loops)
- MaxConcurrentTools limit
- Result caching per request
- Formats results for LLM context merging

**Pre-Configured REST APIs**:

**RestApiConfig** (`Models/RestApiConfig.cs`):
- Complete API definition model
- OpenAPI spec reference support
- Tool integration, tags, default parameters
- All mock features: shape, context, rate limiting, streaming, caching

**RestApiRegistry** (`Services/RestApiRegistry.cs`):
- Loads and validates all configured APIs
- Thread-safe lookup with `ConcurrentDictionary`
- Auto-reloads on configuration changes (`IOptionsMonitor`)
- Tag-based filtering and grouping
- Validation: required fields, valid HTTP methods, positive values

**Context Memory**:

**MemoryCacheContextStore** (`Services/MemoryCacheContextStore.cs`):
- Uses `IMemoryCache` with `SlidingExpiration` option
- Registers `PostEvictionCallback` for automatic cleanup logging
- Thread-safe with `ConcurrentDictionary` for name tracking
- Stale entry detection and removal on enumeration

**OpenApiContextManager** (`Services/OpenApiContextManager.cs`):
- New `ExtractAllFields()` method with recursive extraction
- Recursive field extraction with configurable depth (default: 2 levels)
- Handles nested objects, arrays, and primitive values
- **Truncation fix**: Long strings (>100 chars) now truncated in SharedData to prevent bloat
- Backward compatible with legacy field extraction

**Configuration** (`LLMockApiOptions.cs`):
- New `Tools` array for pluggable tool definitions
- New `ToolExecutionMode` enum (Disabled/Explicit/LlmDriven)
- New `MaxConcurrentTools` and `MaxToolChainDepth` safety limits
- New `RestApis` array for pre-configured API definitions
- New `ContextExpirationMinutes` property (default: 15, range: 5-1440)
- All registered in DI via `LLMockApiExtensions.RegisterCoreServices()`

### Testing

**8 New Tests** in `OpenApiContextManagerTests.cs`:
- ✅ Dynamic extraction of all primitive fields
- ✅ Nested object extraction with dot notation
- ✅ Array length tracking
- ✅ First array item field extraction
- ✅ Multiple nesting levels (2 deep by default)
- ✅ Mixed data types (strings, numbers, booleans)
- ✅ Backward compatibility with legacy keys
- ✅ Edge cases (empty objects, null values, deep nesting limit)

### Documentation Updates

**New Documentation**:
- `docs/TOOLS_ACTIONS.md` (500+ lines): Complete guide to pluggable tools system
  - Overview and MCP compatibility
  - Tool types (HTTP and Mock)
  - Configuration reference with all options
  - Authentication methods (Bearer, Basic, API Key)
  - Usage examples and patterns
  - Decision tree patterns with mock tools
  - Advanced scenarios (conditional logic, dynamic endpoints, POST templates)
  - Phase 2/3 roadmap (LLM-driven tool selection, advanced decision trees)
  - API reference with TypeScript schemas
  - Troubleshooting and performance considerations

- `docs/RATE_LIMITING_BATCHING.md`: Comprehensive rate limiting guide
  - Configuration options and strategies
  - N-completions with batching
  - Per-endpoint statistics tracking
  - Performance testing examples

**Updated Files**:
- `README.md`: New v2.2.0 section with tools and pre-configured APIs
- `appsettings.Full.json`: Complete examples for:
  - 5 tool configurations (HTTP and Mock tools)
  - 8 pre-configured REST API definitions
  - Tool authentication examples
  - Decision tree patterns
- `LLMApi.http`: 20+ new HTTP request examples
  - Tool execution via query params and headers
  - Template substitution examples
  - Tool chaining patterns
  - Authentication examples
  - JSONPath extraction
  - Performance monitoring
- `docs/API-CONTEXTS.md`: Complete rewrite of Context Storage and Shared Data Extraction
  - IMemoryCache architecture
  - Sliding expiration behavior
  - Dynamic extraction examples
  - Real-world use cases with nested data
- `CLAUDE.md`: Updated with all new configuration options
  - Tools & Actions configuration
  - Pre-configured REST APIs
  - Context expiration settings

### Breaking Changes

**None** - Fully backward compatible with v2.1.0:
- Legacy field extraction (`lastId`, `lastUserId`, etc.) still works
- Existing context code continues to work unchanged
- New features are automatic - no code changes needed

### Migration Guide

**No migration needed!** Upgrade and enjoy automatic benefits:

1. **Context Expiration**: Automatic - contexts now clean themselves up
2. **Enhanced Extraction**: Automatic - all fields are now tracked

**Optional**: Configure expiration time if default 15 minutes doesn't fit your workflow:
```json
{
  "mostlylucid.mockllmapi": {
    "ContextExpirationMinutes": 30  // Increase for longer test sessions
  }
}
```

### Performance Impact

**Memory Usage**: Reduced overall due to automatic expiration
- Contexts no longer accumulate indefinitely
- CI/CD pipelines remain memory-efficient
- Long-running services self-clean

**Extraction Overhead**: Minimal (< 1ms per response)
- Recursive extraction is highly optimized
- Only runs on responses with contexts
- Depth limit prevents runaway recursion

**Token Usage**: Slightly increased
- More fields in shared data = slightly longer prompts
- Typical increase: 50-200 tokens per context
- Offset by automatic expiration (fewer contexts overall)

### Compatibility

- **Fully backward compatible** with v2.1.0
- All existing features continue to work
- No breaking changes
- Upgrade-in-place recommended

---

## v2.1.0 (2025-01-06) - Quality & Validation Release

**Focus**: Enhanced reliability, comprehensive testing, and improved developer experience

This release focuses on improving chunking reliability, providing comprehensive validation tooling, and streamlining configuration management. All features from v2.0 remain fully compatible.

### Major Improvements

#### 1. Comprehensive HTTP Validation Suite
- **70+ Test Cases**: Complete validation coverage in `LLMApi.http`
  - OpenAPI management (load, list, get, unload, describe)
  - API context management (create, update, pause, resume, delete)
  - gRPC proto management (upload, list, get, services, call)
  - Continuous SSE streaming validation
  - Chunking tests with large arrays
  - Context history and custom descriptions
  - Backend selection (X-LLM-Backend header)
  - Schema validation (includeSchema + X-Response-Schema)
  - Combined feature validation
  - Error simulation (comprehensive HTTP error codes)

#### 2. Enhanced Chunking Reliability
- **Explicit Array Formatting**: Enhanced prompts with ultra-explicit instructions for JSON array generation
  - Added critical formatting rules in `PromptBuilder.cs`: "Your FIRST character MUST be: ["
  - Reinforced array formatting in `ChunkingCoordinator.cs` chunk context
  - Prevents comma-separated object output (e.g., `{...},{...}` instead of `[{...},{...}]`)
- **Improved Instruction Following**: Better guidance for LLMs during multi-chunk requests
- **Known Limitations**: Documented model/temperature recommendations for optimal chunking (see `docs/OLLAMA_MODELS.md`)

#### 3. Configuration Streamlining
- **Clean `appsettings.json`**: Removed verbose model comments, cleaner structure
- **`docs/OLLAMA_MODELS.md`**: New comprehensive reference guide (285 lines)
  - 10+ model configurations with hardware requirements
  - Temperature guidelines for different use cases
  - Context window sizing recommendations
  - Multi-backend configuration examples
  - Chunking troubleshooting guide
  - Model installation instructions

#### 4. Documentation Improvements
- **Full Swagger Documentation**: All 25+ management endpoints now have complete Swagger docs
  - Tags organization (OpenAPI Management, API Contexts, gRPC, etc.)
  - Summaries and detailed descriptions
  - Request/response examples
- **Backend API Reference**: New `docs/BACKEND_API_REFERENCE.md` (600+ lines)
  - Complete endpoint documentation
  - Query parameters and headers reference
  - Error response formats
  - SignalR hub documentation

### Bug Fixes
- Fixed URL encoding issue in HTTP test for OpenAPI endpoint descriptions (`:`, `/`, `{`, `}` characters)

### Files Modified
**Code Changes:**
- `mostlylucid.mockllmapi/Services/PromptBuilder.cs`: Enhanced array formatting instructions (lines 82-94)
- `mostlylucid.mockllmapi/Services/ChunkingCoordinator.cs`: Added array formatting to chunk context (line 447)
- `LLMApi/appsettings.json`: Streamlined configuration

**New Documentation:**
- `docs/OLLAMA_MODELS.md`: Comprehensive model configuration guide
- `docs/BACKEND_API_REFERENCE.md`: Complete management API reference

**Updated Files:**
- `LLMApi/LLMApi.http`: Expanded from 448 to 847 lines (70+ new validation tests)

### Known Limitations
**Chunking at High Temperature**:
- LLMs may generate comma-separated objects instead of arrays at temperature 1.2+
- **Workarounds**:
  1. Lower temperature to 0.8-1.0 for chunked requests
  2. Use llama3.2:3b or mistral-nemo (better instruction following)
  3. Reduce item count to avoid chunking
  4. Disable auto-chunking with `?autoChunk=false`

See `docs/OLLAMA_MODELS.md` for detailed troubleshooting.

### Compatibility
- **Fully backward compatible** with v2.0.0
- All existing features continue to work
- No breaking changes

---

## v2.0.0 (2025-01-06) - MAJOR RELEASE

**NO BREAKING CHANGES** - Despite the major version bump, all existing code continues to work!

This is a major milestone release that transforms LLMock API into a comprehensive, production-ready mocking platform. Version 2.0 adds realistic SSE streaming modes, multi-backend load balancing, comprehensive backend selection, and extensive documentation.

### Major Features

#### 1. Realistic SSE Streaming Modes (MAJOR)

Three distinct SSE streaming modes for testing different real-world API patterns:

**LlmTokens Mode** (Default - Backward Compatible)
- Token-by-token streaming for AI chat interfaces
- Format: `{"chunk":"text","accumulated":"fulltext","done":false}`
- Use case: Testing chatbot UIs, LLM applications

**CompleteObjects Mode** (NEW)
- Complete JSON objects as separate SSE events
- Format: `{"data":{object},"index":0,"total":10,"done":false}`
- Use case: Twitter/X API, stock tickers, real-time feeds, IoT sensors

**ArrayItems Mode** (NEW)
- Array items with rich metadata
- Format: `{"item":{object},"index":0,"total":100,"arrayName":"users","hasMore":true,"done":false}`
- Use case: Paginated results, bulk exports, search results

**Configuration:**
```json
{
  "MockLlmApi": {
    "SseMode": "CompleteObjects"  // LlmTokens | CompleteObjects | ArrayItems
  }
}
```

**Per-Request Override:**
```http
GET /api/mock/stream/users?sseMode=CompleteObjects
GET /api/mock/stream/data?sseMode=ArrayItems
GET /api/mock/stream/chat?sseMode=LlmTokens
```

**Client Example (CompleteObjects):**
```javascript
const eventSource = new EventSource('/api/mock/stream/users?sseMode=CompleteObjects');

eventSource.onmessage = (event) => {
    const response = JSON.parse(event.data);

    if (response.done) {
        console.log('Complete!');
        eventSource.close();
    } else {
        console.log(`User ${response.index + 1}/${response.total}:`, response.data);
        // response.data contains the complete user object
    }
};
```

#### 2. Multi-Backend Load Balancing (MAJOR)

Distribute requests across multiple LLM backends for high throughput:

**Configuration:**
```json
{
  "MockLlmApi": {
    "Backends": [
      {
        "Name": "ollama-llama3",
        "Provider": "ollama",
        "Weight": 3,
        "Enabled": true
      },
      {
        "Name": "ollama-mistral",
        "Provider": "ollama",
        "Weight": 2,
        "Enabled": true
      },
      {
        "Name": "lmstudio-default",
        "Provider": "lmstudio",
        "Weight": 1,
        "Enabled": true
      }
    ]
  }
}
```

**SignalR Hub with Load Balancing:**
```json
{
  "HubContexts": [
    {
      "Name": "high-throughput-data",
      "BackendNames": ["ollama-llama3", "ollama-mistral", "lmstudio-default"]
    }
  ]
}
```

**Features:**
- Weighted round-robin distribution
- Per-request backend selection
- Automatic failover to default if backends unavailable
- Works with SignalR, SSE, REST, and GraphQL

#### 3. Comprehensive Backend Selection

**Per-Request Selection** (Multiple Methods):
```http
# Via query parameter
GET /api/mock/users?backend=openai-gpt4

# Via header
GET /api/mock/users
X-LLM-Backend: openai-gpt4

# SignalR hub context
{
  "HubContexts": [
    {
      "Name": "analytics",
      "BackendName": "openai-gpt4-turbo"
    }
  ]
}
```

**Multiple Providers Simultaneously:**
- Ollama (local models)
- OpenAI (cloud API)
- LM Studio (local server)
- Mistral-Nemo (128k context)

#### 4. Mistral-Nemo 128k Context Support

Configuration for Mistral-Nemo with massive 128k context window:

```json
{
  "Backends": [
    {
      "Name": "ollama-mistral-nemo",
      "Provider": "ollama",
      "ModelName": "mistral-nemo",
      "MaxTokens": 128000,
      "Enabled": true
    }
  ]
}
```

**Use Cases:**
- Generating thousands of detailed records in a single request
- Complex nested data structures with deep relationships
- Large batch operations (use with `MaxItems=10000+`)
- Comprehensive test datasets
- GraphQL queries with extensive nested fields

**SignalR Example:**
```json
{
  "HubContexts": [
    {
      "Name": "massive-dataset-128k",
      "Description": "Massive dataset generation with 128k context",
      "BackendName": "ollama-mistral-nemo"
    }
  ]
}
```

#### 5. Swagger/OpenAPI UI

Interactive API documentation with Swagger UI:

- **Swagger UI**: Available at `/swagger`
- **OpenAPI Specification**: Auto-generated from endpoints
- **Interactive Testing**: Try endpoints directly in browser
- **Comprehensive Descriptions**: Full documentation for all endpoints
- **Navigation**: Linked from demo page header

**Enable in Program.cs:**
```csharp
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

app.UseSwagger();
app.UseSwaggerUI();
```

### Documentation Overhaul

#### New Documentation Files
- **`docs/SSE_STREAMING_MODES.md`** (2,500+ lines) - Complete SSE guide
  - Detailed explanation of all three modes
  - Client code examples for each mode
  - Use case decision matrix
  - Comparison charts
  - Migration guide
  - Troubleshooting section

- **`LLMApi/SSE_Streaming.http`** - 30+ HTTP examples for SSE modes
  - Examples for all three SSE modes
  - Combined feature examples (backend selection, context, errors)
  - Practical use cases (stock tickers, bulk exports, logs)
  - Client JavaScript code examples

#### Updated Documentation
- **`docs/CONFIGURATION_REFERENCE.md`** - Added SSE modes section
- **`docs/MULTIPLE_LLM_BACKENDS.md`** - Enhanced with load balancing
- **`appsettings.Full.json`** - Added SSE mode examples and Mistral-Nemo

### 🧪 Testing Improvements

**New Test Coverage:**
- **22 SSE mode tests** (all passing)
  - Enum value validation
  - Configuration parsing
  - Query parameter override
  - Event format validation for all modes
  - Case-insensitive parsing

**Total Test Suite:**
- **218 tests** (213 passing, 5 gRPC tests skipped)
- Zero compilation errors or warnings
- Full backward compatibility verified

### 🔧 Configuration Enhancements

**New Configuration Options:**

```json
{
  "MockLlmApi": {
    // SSE Streaming Modes (NEW)
    "SseMode": "LlmTokens",  // LlmTokens | CompleteObjects | ArrayItems

    // Multiple LLM Backends with Load Balancing (v1.8.0)
    "Backends": [
      {
        "Name": "backend-name",
        "Provider": "ollama",  // ollama | openai | lmstudio
        "BaseUrl": "http://localhost:11434/v1/",
        "ModelName": "llama3",
        "ApiKey": null,
        "MaxTokens": 8192,
        "Enabled": true,
        "Weight": 3,  // For load balancing
        "Priority": 10
      }
    ],

    // Legacy Single Backend (Still Supported)
    "BaseUrl": "http://localhost:11434/v1/",
    "ModelName": "llama3",

    // Auto-Chunking (v1.8.0)
    "EnableAutoChunking": true,
    "MaxInputTokens": 4096,
    "MaxOutputTokens": 2048,
    "MaxItems": 1000,

    // Streaming Configuration
    "StreamingChunkDelayMinMs": 0,
    "StreamingChunkDelayMaxMs": 0,

    // Cache Configuration
    "CacheSlidingExpirationMinutes": 15,
    "CacheAbsoluteExpirationMinutes": 60,
    "MaxCachePerKey": 5
  }
}
```

### 🌍 Environment Variables

Comprehensive environment variable support with full documentation:

```bash
# SSE Mode
export MockLlmApi__SseMode="CompleteObjects"

# Backend Selection
export MockLlmApi__Backends__0__Name="ollama-llama3"
export MockLlmApi__Backends__0__Provider="ollama"
export MockLlmApi__Backends__0__BaseUrl="http://localhost:11434/v1/"
export MockLlmApi__Backends__0__ModelName="llama3"
export MockLlmApi__Backends__0__Enabled="true"
export MockLlmApi__Backends__0__Weight="3"

# SignalR Hub Contexts with Backend Selection
export MockLlmApi__HubContexts__0__Name="analytics"
export MockLlmApi__HubContexts__0__BackendName="openai-gpt4-turbo"

# Or load balancing
export MockLlmApi__HubContexts__1__BackendNames__0="ollama-llama3"
export MockLlmApi__HubContexts__1__BackendNames__1="ollama-mistral"
```

### 📦 New Files

**Core Implementation:**
- `mostlylucid.mockllmapi/Models/SseMode.cs` - SSE mode enum
- `mostlylucid.mockllmapi/Services/Providers/ILlmProvider.cs` - Provider interface
- `mostlylucid.mockllmapi/Services/Providers/OllamaProvider.cs` - Ollama provider
- `mostlylucid.mockllmapi/Services/Providers/OpenAIProvider.cs` - OpenAI provider
- `mostlylucid.mockllmapi/Services/Providers/LMStudioProvider.cs` - LM Studio provider
- `mostlylucid.mockllmapi/Services/Providers/LlmProviderFactory.cs` - Provider factory
- `mostlylucid.mockllmapi/Services/LlmBackendSelector.cs` - Backend selection logic

**Testing:**
- `LLMApi.Tests/SseModeTests.cs` - 22 SSE mode tests

**Documentation:**
- `docs/SSE_STREAMING_MODES.md` - Complete SSE guide (2,500+ lines)
- `LLMApi/SSE_Streaming.http` - 30+ SSE examples

### 📝 Updated Files

**Core Services:**
- `mostlylucid.mockllmapi/LLMockApiOptions.cs` - Added `SseMode` property, `LlmBackends` array
- `mostlylucid.mockllmapi/RequestHandlers/StreamingRequestHandler.cs` - Added SSE mode routing
- `mostlylucid.mockllmapi/Services/LlmClient.cs` - Added backend selection overloads
- `mostlylucid.mockllmapi/Services/MockDataBackgroundService.cs` - Added SignalR backend selection
- `mostlylucid.mockllmapi/Models/HubContextConfig.cs` - Added `BackendName` and `BackendNames`

**Documentation:**
- `README.md` - Updated to v2.0, comprehensive feature list
- `docs/CONFIGURATION_REFERENCE.md` - Added SSE modes and backend selection
- `docs/MULTIPLE_LLM_BACKENDS.md` - Enhanced with load balancing examples
- `appsettings.Full.json` - Added comprehensive examples

**Demo Application:**
- `LLMApi/Program.cs` - Added Swagger configuration
- `LLMApi/Pages/_Layout.cshtml` - Added Swagger UI link

### 🎨 Use Case Examples

**Stock Market Feed (CompleteObjects):**
```http
GET /api/mock/stream/stocks?sseMode=CompleteObjects&shape={"ticker":"AAPL","price":150.25,"change":2.5}
```

**Bulk Customer Export (ArrayItems):**
```http
GET /api/mock/stream/export-customers?sseMode=ArrayItems&shape={"customers":[{"id":"string","name":"string"}]}
```

**AI Chat Interface (LlmTokens):**
```http
GET /api/mock/stream/chat?sseMode=LlmTokens&shape={"message":"Hello!"}
```

**High-Throughput IoT Sensors (Load Balanced):**
```json
{
  "HubContexts": [
    {
      "Name": "iot-sensors",
      "BackendNames": ["ollama-llama3", "ollama-mistral", "lmstudio-default"]
    }
  ]
}
```

**Massive Dataset with 128k Context:**
```http
GET /api/mock/stream/bulk-data?sseMode=ArrayItems&backend=ollama-mistral-nemo
```

### Migration from v1.x

**No Code Changes Required!**

Version 2.0 is 100% backward compatible with v1.x:

```csharp
// v1.x code - still works exactly the same
builder.Services.AddLLMockApi(builder.Configuration);
app.MapLLMockApi("/api/mock", includeStreaming: true);

// SSE streaming defaults to LlmTokens mode (original behavior)
// Legacy single backend config (BaseUrl/ModelName) still works
```

**Opt-In to New Features:**

```csharp
// Same setup, just update appsettings.json
{
  "MockLlmApi": {
    "SseMode": "CompleteObjects",  // Switch to realistic streaming
    "Backends": [...]  // Add multiple backends
  }
}
```

### Breaking Changes

**NONE!**

Despite the major version bump to 2.0, there are zero breaking changes:
- Default SSE mode is `LlmTokens` (original behavior)
- Legacy `BaseUrl`/`ModelName` config still supported
- All existing endpoints work unchanged
- All existing tests pass
- Full backward compatibility maintained

### 📈 Performance Improvements

- **Multi-Backend Load Balancing**: Distribute load across multiple LLM instances
- **Weighted Round-Robin**: Smart distribution based on backend capabilities
- **Backend-Specific Token Limits**: Optimize for each model's capabilities
- **CompleteObjects Mode**: Fewer SSE events = lower overhead than token-by-token

### Why Version 2.0?

This release represents a fundamental transformation:

**v1.x: Mock API with LLM-powered generation**
- Single LLM backend
- One SSE streaming mode
- Basic configuration

**v2.0: Production-Ready Mock Platform**
- Multiple LLM backends with load balancing
- Three realistic SSE streaming modes
- Comprehensive backend selection
- Production-scale configuration (128k contexts)
- Enterprise-ready documentation
- Interactive API documentation (Swagger)

Version 2.0 positions LLMock API as a comprehensive mocking platform capable of handling production-scale testing requirements across diverse use cases.

### Complete Documentation

- **[SSE Streaming Modes](./docs/SSE_STREAMING_MODES.md)** - Complete guide to SSE modes
- **[Multiple LLM Backends](./docs/MULTIPLE_LLM_BACKENDS.md)** - Backend configuration guide
- **[Configuration Reference](./docs/CONFIGURATION_REFERENCE.md)** - All configuration options
- **[HTTP Examples](./LLMApi/SSE_Streaming.http)** - Ready-to-run SSE examples
- **[Chunking and Caching](./CHUNKING_AND_CACHING.md)** - Auto-chunking guide
- **[API Contexts](./docs/API-CONTEXTS.md)** - Context management guide
- **[gRPC Support](./docs/GRPC_SUPPORT.md)** - gRPC mocking guide
- **[README](./README.md)** - Quick start and overview

### 🙏 Thank You

Thank you to all users and contributors who have helped shape LLMock API into a comprehensive mocking platform. Your feedback and use cases have driven these improvements!

---

## v1.8.0 (2025-01-06)

See previous release notes for v1.8.0 features (Multiple LLM Backend Support, Automatic Request Chunking, Enhanced Cache Configuration).

---

## Previous Releases

See full release history below for v1.7.x, v1.6.x, v1.5.x, and earlier versions.

