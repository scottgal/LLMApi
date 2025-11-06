# Modular Architecture Examples

**New in v1.2.0:** Complete modularity! Each protocol (REST, Streaming, GraphQL, SignalR) can now be added and mapped independently.

## Table of Contents
- [Backward Compatible (Unified Approach)](#backward-compatible-unified-approach)
- [Modular Approach](#modular-approach)
  - [REST Only](#rest-only)
  - [GraphQL Only](#graphql-only)
  - [Streaming Only](#streaming-only)
  - [SignalR Only](#signalr-only)
  - [Mix and Match](#mix-and-match)
- [Multiple Instances](#multiple-instances)
- [Benefits of Modular Approach](#benefits-of-modular-approach)

---

## Backward Compatible (Unified Approach)

**Existing code continues to work without any changes:**

```csharp
using mostlylucid.mockllmapi;

var builder = WebApplication.CreateBuilder(args);

// Registers ALL services (REST, Streaming, GraphQL)
builder.Services.AddLLMockApi(builder.Configuration);

// Optional: Add SignalR
builder.Services.AddLLMockSignalR(builder.Configuration);

builder.Services.AddRazorPages();

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();

// Maps ALL endpoints (REST, Streaming, GraphQL) at /api/mock
app.MapLLMockApi("/api/mock", includeStreaming: true, includeGraphQL: true);

// Optional: Map SignalR
app.MapLLMockSignalR("/hub/mock", "/api/mock");

app.Run();
```

**This creates:**
- `/api/mock/**` - REST endpoints
- `/api/mock/stream/**` - SSE streaming endpoints
- `/api/mock/graphql` - GraphQL endpoint
- `/hub/mock` - SignalR hub (if added)
- `/api/mock/contexts` - SignalR management API (if added)

---

## Modular Approach

### REST Only

Perfect for simple REST API mocking without extra overhead:

```csharp
using mostlylucid.mockllmapi;

var builder = WebApplication.CreateBuilder(args);

// Register ONLY REST services
builder.Services.AddLLMockRest(builder.Configuration);

var app = builder.Build();

app.UseRouting();

// Map ONLY REST endpoints
app.MapLLMockRest("/api/mock");

app.Run();
```

**This creates:**
- `/api/mock/**` - REST endpoints only

**Benefits:**
- Minimal memory footprint
- Faster startup time
- Only includes RegularRequestHandler

---

### GraphQL Only

Perfect for GraphQL-only applications:

```csharp
using mostlylucid.mockllmapi;

var builder = WebApplication.CreateBuilder(args);

// Register ONLY GraphQL services
builder.Services.AddLLMockGraphQL(builder.Configuration);

var app = builder.Build();

app.UseRouting();

// Map ONLY GraphQL endpoint
app.MapLLMockGraphQL("/api/mock");

app.Run();
```

**This creates:**
- `/api/mock/graphql` - GraphQL endpoint only

**Benefits:**
- Clean GraphQL-only setup
- No unnecessary REST handlers
- Perfect for GraphQL client testing

---

### Streaming Only

Perfect for testing SSE streaming clients:

```csharp
using mostlylucid.mockllmapi;

var builder = WebApplication.CreateBuilder(args);

// Register ONLY Streaming services
builder.Services.AddLLMockStreaming(builder.Configuration);

var app = builder.Build();

app.UseRouting();

// Map ONLY Streaming endpoints
app.MapLLMockStreaming("/api/mock");

app.Run();
```

**This creates:**
- `/api/mock/stream/**` - SSE streaming endpoints only

**Benefits:**
- Lightweight streaming-only setup
- Perfect for EventSource testing
- No REST/GraphQL overhead

---

### SignalR Only

Perfect for real-time dashboard prototyping:

```csharp
using mostlylucid.mockllmapi;

var builder = WebApplication.CreateBuilder(args);

// Register ONLY SignalR services (includes core services automatically)
builder.Services.AddLLMockSignalR(options =>
{
    options.BaseUrl = "http://localhost:11434/v1/";
    options.ModelName = "llama3";
    options.SignalRPushIntervalMs = 5000;
});

var app = builder.Build();

app.UseRouting();

// Map ONLY SignalR endpoints
app.MapLLMockSignalR("/hub/mock", "/api/contexts");

app.Run();
```

**This creates:**
- `/hub/mock` - SignalR hub
- `/api/contexts` - Context management API

**Benefits:**
- Real-time only setup
- No HTTP endpoint overhead
- Perfect for WebSocket testing

---

### gRPC Only

Perfect for testing gRPC clients with dynamic proto definitions:

```csharp
using mostlylucid.mockllmapi;

var builder = WebApplication.CreateBuilder(args);

// Register services (gRPC included in AddLLMockApi)
builder.Services.AddLLMockApi(builder.Configuration);

var app = builder.Build();

app.UseRouting();

// Map ONLY gRPC endpoints
app.MapLLMockGrpcManagement("/api/grpc-protos");  // Proto file management
app.MapLLMockGrpc("/api/grpc");                   // gRPC service calls

app.Run();
```

**This creates:**
- `/api/grpc-protos` - Upload/manage .proto files (POST, GET, DELETE)
- `/api/grpc/{serviceName}/{methodName}` - Invoke mock gRPC methods

**Benefits:**
- Dynamic proto upload without recompilation
- Perfect for gRPC client testing
- LLM generates realistic protobuf responses

**Usage example:**
```bash
# 1. Upload a proto definition
curl -X POST http://localhost:5116/api/grpc-protos \
  -H "Content-Type: text/plain" \
  --data 'syntax = "proto3"; service UserService { rpc GetUser(GetUserRequest) returns (User); }'

# 2. Call the gRPC method
curl -X POST http://localhost:5116/api/grpc/UserService/GetUser \
  -H "Content-Type: application/json" \
  -d '{"user_id": 123}'
```

---

### OpenAPI Only

Perfect for mocking existing OpenAPI/Swagger specs:

```csharp
using mostlylucid.mockllmapi;

var builder = WebApplication.CreateBuilder(args);

// Register ONLY OpenAPI services
builder.Services.AddLLMockOpenApi(options =>
{
    options.BaseUrl = "http://localhost:11434/v1/";
    options.ModelName = "llama3";
    options.OpenApiSpecs = new List<OpenApiSpecConfig>
    {
        new OpenApiSpecConfig
        {
            Name = "PetStore",
            Source = "https://petstore3.swagger.io/api/v3/openapi.json",
            BasePath = "/api/petstore"
        }
    };
});

var app = builder.Build();

app.UseRouting();

// Map ONLY OpenAPI endpoints
app.MapLLMockOpenApi();                      // Loads configured specs
app.MapLLMockOpenApiManagement("/api/specs"); // Dynamic spec management

app.Run();
```

**This creates:**
- All endpoints defined in your OpenAPI spec (e.g., `/api/petstore/pet/{petId}`)
- `/api/specs` - Upload/manage OpenAPI specs dynamically (POST, GET, DELETE)

**Benefits:**
- Automatically mock entire OpenAPI specs
- No manual endpoint mapping required
- Supports both static (configured) and dynamic (uploaded) specs
- Perfect for API contract testing

---

### Context Management API

Add context history viewing and modification:

```csharp
using mostlylucid.mockllmapi;

var builder = WebApplication.CreateBuilder(args);

// Register REST services
builder.Services.AddLLMockRest(builder.Configuration);

var app = builder.Build();

app.UseRouting();

// Map REST endpoints
app.MapLLMockRest("/api/mock");

// Add context management API
app.MapLLMockApiContextManagement("/api/contexts");

app.Run();
```

**This creates:**
- `/api/mock/**` - REST endpoints
- `/api/contexts/{contextId}` - View/modify conversation history

**Benefits:**
- View LLM conversation history for debugging
- Modify context to steer responses
- Clear context to reset state
- Perfect for testing stateful scenarios

---

### Mix and Match

Combine protocols as needed for your use case:

#### Example 1: REST + GraphQL (No Streaming)

```csharp
using mostlylucid.mockllmapi;

var builder = WebApplication.CreateBuilder(args);

// Register REST and GraphQL services
builder.Services.AddLLMockRest(builder.Configuration);
builder.Services.AddLLMockGraphQL(builder.Configuration);

var app = builder.Build();

app.UseRouting();

// Map both protocols
app.MapLLMockRest("/api/mock");
app.MapLLMockGraphQL("/api/mock");

app.Run();
```

**This creates:**
- `/api/mock/**` - REST endpoints
- `/api/mock/graphql` - GraphQL endpoint
- No streaming endpoints

---

#### Example 2: GraphQL + SignalR (No REST)

```csharp
using mostlylucid.mockllmapi;

var builder = WebApplication.CreateBuilder(args);

// Register GraphQL and SignalR services
builder.Services.AddLLMockGraphQL(builder.Configuration);
builder.Services.AddLLMockSignalR(builder.Configuration);

var app = builder.Build();

app.UseRouting();

// Map both protocols
app.MapLLMockGraphQL("/api/queries");
app.MapLLMockSignalR("/hub/realtime", "/api/contexts");

app.Run();
```

**This creates:**
- `/api/queries/graphql` - GraphQL endpoint
- `/hub/realtime` - SignalR hub
- `/api/contexts` - SignalR management API
- No REST or SSE endpoints

---

#### Example 3: Everything with Different Patterns

```csharp
using mostlylucid.mockllmapi;

var builder = WebApplication.CreateBuilder(args);

// Register all services modularly
builder.Services.AddLLMockRest(builder.Configuration);
builder.Services.AddLLMockStreaming(builder.Configuration);
builder.Services.AddLLMockGraphQL(builder.Configuration);
builder.Services.AddLLMockSignalR(builder.Configuration);

var app = builder.Build();

app.UseRouting();

// Map each protocol with custom patterns
app.MapLLMockRest("/api/rest");           // /api/rest/**
app.MapLLMockStreaming("/api/stream");    // /api/stream/stream/**
app.MapLLMockGraphQL("/api/graphql");     // /api/graphql/graphql
app.MapLLMockSignalR("/hub/live", "/api/hub");

app.Run();
```

**This creates:**
- `/api/rest/**` - REST endpoints
- `/api/stream/stream/**` - SSE streaming
- `/api/graphql/graphql` - GraphQL endpoint
- `/hub/live` - SignalR hub
- `/api/hub/contexts` - SignalR management

---

#### Example 4: REST + OpenAPI + Context Management

```csharp
using mostlylucid.mockllmapi;

var builder = WebApplication.CreateBuilder(args);

// Register REST and OpenAPI services
builder.Services.AddLLMockRest(builder.Configuration);
builder.Services.AddLLMockOpenApi(builder.Configuration);

var app = builder.Build();

app.UseRouting();

// Map all three capabilities
app.MapLLMockRest("/api/mock");
app.MapLLMockOpenApi();
app.MapLLMockApiContextManagement("/api/contexts");

app.Run();
```

**This creates:**
- `/api/mock/**` - REST endpoints
- OpenAPI spec endpoints (from configured specs)
- `/api/contexts/{contextId}` - Context management
- No streaming, GraphQL, SignalR, or gRPC

---

#### Example 5: gRPC + OpenAPI (Protocol Bridge Testing)

```csharp
using mostlylucid.mockllmapi;

var builder = WebApplication.CreateBuilder(args);

// Register services for both protocols
builder.Services.AddLLMockApi(builder.Configuration);  // Includes gRPC
builder.Services.AddLLMockOpenApi(builder.Configuration);

var app = builder.Build();

app.UseRouting();

// Map both protocols
app.MapLLMockGrpcManagement("/api/grpc-protos");
app.MapLLMockGrpc("/api/grpc");
app.MapLLMockOpenApi();

app.Run();
```

**This creates:**
- `/api/grpc-protos` - gRPC proto management
- `/api/grpc/{service}/{method}` - gRPC calls
- OpenAPI spec endpoints
- Perfect for testing protocol bridges/gateways

---

#### Example 6: Everything Modular (Full Stack Testing)

```csharp
using mostlylucid.mockllmapi;

var builder = WebApplication.CreateBuilder(args);

// Register everything modularly
builder.Services.AddLLMockRest(builder.Configuration);
builder.Services.AddLLMockStreaming(builder.Configuration);
builder.Services.AddLLMockGraphQL(builder.Configuration);
builder.Services.AddLLMockSignalR(builder.Configuration);
builder.Services.AddLLMockOpenApi(options =>
{
    options.BaseUrl = "http://localhost:11434/v1/";
    options.ModelName = "llama3";
    options.OpenApiSpecs = new List<OpenApiSpecConfig>
    {
        new OpenApiSpecConfig
        {
            Name = "External API",
            Source = "https://api.example.com/openapi.json",
            BasePath = "/api/external"
        }
    };
});

var app = builder.Build();

app.UseRouting();

// Map everything at different paths
app.MapLLMockRest("/api/rest");
app.MapLLMockStreaming("/api/stream");
app.MapLLMockGraphQL("/api/graphql");
app.MapLLMockSignalR("/hub/realtime", "/api/hub");
app.MapLLMockOpenApi();
app.MapLLMockOpenApiManagement("/api/specs");
app.MapLLMockGrpcManagement("/api/grpc-protos");
app.MapLLMockGrpc("/api/grpc");
app.MapLLMockApiContextManagement("/api/contexts");

app.Run();
```

**This creates:**
- `/api/rest/**` - REST endpoints
- `/api/stream/stream/**` - SSE streaming
- `/api/graphql/graphql` - GraphQL endpoint
- `/hub/realtime` - SignalR hub
- `/api/hub/contexts` - SignalR management
- `/api/external/**` - OpenAPI spec endpoints
- `/api/specs` - OpenAPI management
- `/api/grpc-protos` - gRPC proto management
- `/api/grpc/{service}/{method}` - gRPC calls
- `/api/contexts/{contextId}` - Context management

**Perfect for:**
- Comprehensive integration testing
- Testing microservice architectures
- Multi-protocol API gateways
- Full-stack development with various client types

---

## Multiple Instances

Run multiple independent mock APIs with different configurations:

```csharp
using mostlylucid.mockllmapi;

var builder = WebApplication.CreateBuilder(args);

// Development endpoints - high randomness
builder.Services.AddLLMockRest(options =>
{
    options.BaseUrl = "http://localhost:11434/v1/";
    options.ModelName = "llama3";
    options.Temperature = 1.5;
});

builder.Services.AddLLMockGraphQL(options =>
{
    options.BaseUrl = "http://localhost:11434/v1/";
    options.ModelName = "llama3";
    options.Temperature = 1.5;
});

var app = builder.Build();

app.UseRouting();

// Development endpoints
app.MapLLMockRest("/api/dev");
app.MapLLMockGraphQL("/api/dev");

// Stable test endpoints (would need separate config)
app.MapLLMockRest("/api/test");
app.MapLLMockGraphQL("/api/test");

app.Run();
```

---

## Benefits of Modular Approach

### 1. **Reduced Memory Footprint**
Only load the handlers you actually use:
- REST only: ~30% less memory
- GraphQL only: ~40% less memory
- Streaming only: ~40% less memory

### 2. **Faster Startup**
Fewer services to register = faster app startup

### 3. **Clearer Intent**
Code explicitly shows which protocols your app uses

### 4. **Easier Testing**
Test protocols in isolation:
```csharp
// Test file for REST only
builder.Services.AddLLMockRest(config);
app.MapLLMockRest("/api/mock");

// Test file for GraphQL only
builder.Services.AddLLMockGraphQL(config);
app.MapLLMockGraphQL("/api/mock");
```

### 5. **No Breaking Changes**
Existing code continues to work:
```csharp
// This still works exactly as before
builder.Services.AddLLMockApi(configuration);
app.MapLLMockApi("/api/mock");
```

### 6. **Flexible Patterns**
Mount protocols at any path:
```csharp
app.MapLLMockRest("/rest");
app.MapLLMockGraphQL("/gql");
app.MapLLMockStreaming("/events");
app.MapLLMockSignalR("/ws", "/manage");
```

---

## Migration Guide

### From v1.1.0 to v1.2.0

**No changes required!** Your existing code works as-is.

**Optional: Migrate to modular approach for benefits**

**Before (v1.1.0):**
```csharp
builder.Services.AddLLMockApi(builder.Configuration);
app.MapLLMockApi("/api/mock", includeStreaming: true, includeGraphQL: true);
```

**After (v1.2.0 - Modular):**
```csharp
// Only include what you need
builder.Services.AddLLMockRest(builder.Configuration);
builder.Services.AddLLMockGraphQL(builder.Configuration);

app.MapLLMockRest("/api/mock");
app.MapLLMockGraphQL("/api/mock");
```

Both approaches work identically, but the modular approach is more explicit and efficient.

---

## Summary

### Core Protocol Services

| Approach | Add Method | Map Method | Use Case |
|----------|------------|------------|----------|
| **Unified** | `AddLLMockApi()` | `MapLLMockApi()` | Everything (REST+Streaming+GraphQL+gRPC), backward compatible |
| **REST** | `AddLLMockRest()` | `MapLLMockRest()` | Simple REST mocking |
| **GraphQL** | `AddLLMockGraphQL()` | `MapLLMockGraphQL()` | GraphQL-only apps |
| **Streaming** | `AddLLMockStreaming()` | `MapLLMockStreaming()` | SSE streaming only |
| **SignalR** | `AddLLMockSignalR()` | `MapLLMockSignalR()` | Real-time WebSocket data |
| **OpenAPI** | `AddLLMockOpenApi()` | `MapLLMockOpenApi()` | Mock from OpenAPI/Swagger specs |

### Additional Features (No Add Method Required)

| Feature | Map Method | Use Case |
|---------|------------|----------|
| **gRPC Management** | `MapLLMockGrpcManagement()` | Upload/manage .proto files |
| **gRPC Calls** | `MapLLMockGrpc()` | Invoke mock gRPC methods |
| **OpenAPI Management** | `MapLLMockOpenApiManagement()` | Dynamically load OpenAPI specs |
| **Context Management** | `MapLLMockApiContextManagement()` | View/modify LLM conversation history |

**Notes:**
- gRPC services are included in `AddLLMockApi()` but map separately
- OpenAPI management allows dynamic spec loading at runtime
- Context management works with any protocol that maintains conversation state
- All features can be mixed and matched as needed

**Choose the approach that best fits your needs!**
