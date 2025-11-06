# Backend API Reference

**Version:** 2.0.0+
**Last Updated:** 2025-01-06

This document provides a comprehensive reference for all management and service endpoints provided by the LLMock API backend.

## Table of Contents

- [OpenAPI Management](#openapi-management)
- [OpenAPI Contexts](#openapi-contexts)
- [API Contexts Management](#api-contexts-management)
- [gRPC Proto Management](#grpc-proto-management)
- [gRPC Service Calls](#grpc-service-calls)
- [SignalR Hubs](#signalr-hubs)

---

## OpenAPI Management

Endpoints for dynamically loading, managing, and testing OpenAPI specifications.

**Base Path:** `/api/openapi` (configurable)
**SignalR Hub:** `/hub/openapi` (configurable)

### List All Loaded Specifications

```http
GET /api/openapi/specs
```

**Description:** Returns a list of all currently loaded OpenAPI specifications with their basic details including name, base path, and endpoint count.

**Response:**
```json
[
  {
    "name": "petstore",
    "basePath": "/petstore",
    "endpointCount": 19,
    "loadedAt": "2025-01-06T10:30:00Z"
  }
]
```

**Tags:** `OpenAPI Management`

---

### Load a New Specification

```http
POST /api/openapi/specs
Content-Type: application/json

{
  "name": "petstore",
  "source": "https://petstore3.swagger.io/api/v3/openapi.json",
  "basePath": "/petstore",
  "contextName": "petstore-session"
}
```

**Description:** Dynamically loads an OpenAPI specification from a URL or file path and registers all endpoints.

**Request Body:**
- `name` (string, required): Unique name for the specification
- `source` (string, required): URL or file path to the OpenAPI spec (JSON or YAML)
- `basePath` (string, required): Base path where endpoints will be mounted
- `contextName` (string, optional): Context name for maintaining consistency across calls
- `enableStreaming` (boolean, optional, default: false): Enable SSE streaming for this spec

**Response:**
```json
{
  "name": "petstore",
  "basePath": "/petstore",
  "endpointCount": 19,
  "endpoints": [
    {"path": "/petstore/pet", "method": "POST"},
    {"path": "/petstore/pet/{petId}", "method": "GET"}
  ],
  "success": true
}
```

**Tags:** `OpenAPI Management`

---

### Get Specification Details

```http
GET /api/openapi/specs/{specName}
```

**Description:** Returns detailed information about a loaded OpenAPI specification including all available endpoints, schemas, and configuration.

**Path Parameters:**
- `specName` (string): Name of the specification

**Response:**
```json
{
  "name": "petstore",
  "basePath": "/petstore",
  "source": "https://petstore3.swagger.io/api/v3/openapi.json",
  "contextName": "petstore-session",
  "endpointCount": 19,
  "endpoints": [...],
  "loadedAt": "2025-01-06T10:30:00Z"
}
```

**Tags:** `OpenAPI Management`

---

### Delete a Specification

```http
DELETE /api/openapi/specs/{specName}
```

**Description:** Removes a loaded OpenAPI specification and unregisters all its endpoints.

**Path Parameters:**
- `specName` (string): Name of the specification to delete

**Response:**
```json
{
  "success": true,
  "message": "Specification 'petstore' deleted successfully"
}
```

**Tags:** `OpenAPI Management`

---

### Reload a Specification

```http
POST /api/openapi/specs/{specName}/reload
```

**Description:** Reloads an OpenAPI specification from its original source, useful when the spec has been updated.

**Path Parameters:**
- `specName` (string): Name of the specification to reload

**Response:**
```json
{
  "name": "petstore",
  "endpointCount": 21,
  "message": "Specification reloaded successfully",
  "changesSummary": "2 new endpoints added"
}
```

**Tags:** `OpenAPI Management`

---

### Test an Endpoint

```http
POST /api/openapi/test
Content-Type: application/json

{
  "specName": "petstore",
  "path": "/pet/123",
  "method": "GET"
}
```

**Description:** Tests a specific endpoint from a loaded OpenAPI specification by generating mock data based on the schema.

**Request Body:**
- `specName` (string, required): Name of the loaded specification
- `path` (string, required): Endpoint path to test
- `method` (string, required): HTTP method (GET, POST, PUT, DELETE, etc.)
- `body` (object, optional): Request body for POST/PUT/PATCH requests

**Response:**
```json
{
  "success": true,
  "response": {
    "id": 123,
    "name": "Max",
    "status": "available"
  }
}
```

**Tags:** `OpenAPI Management`

---

## OpenAPI Contexts

Endpoints for managing OpenAPI-specific contexts (different from API Contexts).

**Base Path:** `/api/openapi/contexts` (part of OpenAPI Management pattern)

### List All OpenAPI Contexts

```http
GET /api/openapi/contexts
```

**Description:** Returns a summary of all active OpenAPI contexts with their call counts and last used timestamps.

**Response:**
```json
[
  {
    "name": "petstore-session",
    "specName": "petstore",
    "totalCalls": 42,
    "createdAt": "2025-01-06T10:30:00Z",
    "lastUsedAt": "2025-01-06T14:22:00Z"
  }
]
```

**Tags:** `OpenAPI Contexts`

---

### Get OpenAPI Context Details

```http
GET /api/openapi/contexts/{contextName}
```

**Description:** Returns detailed information about an OpenAPI context including recent API calls, shared data, and context history.

**Path Parameters:**
- `contextName` (string): Name of the context

**Response:**
```json
{
  "name": "petstore-session",
  "specName": "petstore",
  "totalCalls": 42,
  "recentCalls": [
    {
      "method": "GET",
      "path": "/petstore/pet/123",
      "timestamp": "2025-01-06T14:20:00Z",
      "response": {...}
    }
  ],
  "sharedData": {
    "lastPetId": "123",
    "lastOwnerId": "456"
  }
}
```

**Tags:** `OpenAPI Contexts`

---

### Clear an OpenAPI Context

```http
DELETE /api/openapi/contexts/{contextName}
```

**Description:** Removes a specific OpenAPI context and all its associated data.

**Path Parameters:**
- `contextName` (string): Name of the context to clear

**Tags:** `OpenAPI Contexts`

---

### Clear All OpenAPI Contexts

```http
DELETE /api/openapi/contexts
```

**Description:** Removes all OpenAPI contexts and their associated data.

**Tags:** `OpenAPI Contexts`

---

## API Contexts Management

Endpoints for managing general API contexts (for `/api/mock` endpoints).

**Base Path:** `/api/contexts` (configurable)

### List All Contexts

```http
GET /api/contexts
```

**Description:** Returns a summary list of all active API contexts including their names, total calls, creation time, and last used time.

**Response:**
```json
[
  {
    "name": "user-session-123",
    "totalCalls": 15,
    "createdAt": "2025-01-06T10:30:00Z",
    "lastUsedAt": "2025-01-06T14:22:00Z"
  }
]
```

**Tags:** `API Contexts`

---

### Get Context Details

```http
GET /api/contexts/{contextName}
```

**Description:** Returns complete details for a context including all recent API calls, shared data values, and context summary.

**Path Parameters:**
- `contextName` (string): Name of the context

**Response:**
```json
{
  "name": "user-session-123",
  "totalCalls": 15,
  "createdAt": "2025-01-06T10:30:00Z",
  "lastUsedAt": "2025-01-06T14:22:00Z",
  "recentCalls": [
    {
      "method": "GET",
      "path": "/api/mock/users/42",
      "timestamp": "2025-01-06T14:20:00Z",
      "requestBody": null,
      "response": "{\"id\": 42, \"name\": \"Alice\"}"
    }
  ],
  "sharedData": {
    "lastUserId": "42",
    "lastProductId": "789"
  },
  "contextSummary": "Session with 15 API calls..."
}
```

**Tags:** `API Contexts`

---

### Get Context Prompt

```http
GET /api/contexts/{contextName}/prompt
```

**Description:** Returns the complete formatted prompt that would be sent to the LLM for this context, including shared data and recent call history.

**Path Parameters:**
- `contextName` (string): Name of the context

**Response:**
```text
API Context: user-session-123
Total calls in session: 15

Shared data to maintain consistency:
- lastUserId: 42
- lastProductId: 789

Recent API calls:
1. GET /api/mock/users/42 → {"id": 42, "name": "Alice"}
2. POST /api/mock/orders → {"orderId": 123, "userId": 42}
...
```

**Tags:** `API Contexts`

---

### Add Call to Context

```http
POST /api/contexts/{contextName}/calls
Content-Type: application/json

{
  "method": "GET",
  "path": "/api/mock/users/42",
  "requestBody": null,
  "response": "{\"id\": 42, \"name\": \"Alice\"}"
}
```

**Description:** Manually adds an API call entry to a context for testing or simulation purposes.

**Path Parameters:**
- `contextName` (string): Name of the context

**Request Body:**
- `method` (string, required): HTTP method
- `path` (string, required): Request path
- `requestBody` (string, optional): Request body JSON
- `response` (string, required): Response JSON

**Tags:** `API Contexts`

---

### Update Shared Data

```http
PATCH /api/contexts/{contextName}/shared-data
Content-Type: application/json

{
  "lastUserId": "99",
  "lastOrderId": "456"
}
```

**Description:** Manually updates the shared data dictionary for a context. Shared data is used to maintain consistency across API calls.

**Path Parameters:**
- `contextName` (string): Name of the context

**Request Body:** Key-value pairs to update in shared data

**Tags:** `API Contexts`

---

### Clear a Context

```http
POST /api/contexts/{contextName}/clear
```

**Description:** Removes all API call history and shared data from a context but keeps the context registered. Use this to reset a context for a new session.

**Path Parameters:**
- `contextName` (string): Name of the context

**Tags:** `API Contexts`

---

### Delete a Context

```http
DELETE /api/contexts/{contextName}
```

**Description:** Completely removes a context including all its data and history. The context will need to be recreated before it can be used again.

**Path Parameters:**
- `contextName` (string): Name of the context

**Tags:** `API Contexts`

---

### Clear All Contexts

```http
DELETE /api/contexts
```

**Description:** Removes all API contexts and their associated data. Useful for resetting the entire system state.

**Tags:** `API Contexts`

---

## gRPC Proto Management

Endpoints for uploading and managing .proto files to enable mock gRPC services.

**Base Path:** `/api/grpc-protos` (configurable)

### Upload a Proto File

```http
POST /api/grpc-protos
Content-Type: multipart/form-data

--boundary
Content-Disposition: form-data; name="file"; filename="greeter.proto"
Content-Type: text/plain

syntax = "proto3";
package greet;

service Greeter {
  rpc SayHello (HelloRequest) returns (HelloReply);
}

message HelloRequest {
  string name = 1;
}

message HelloReply {
  string message = 1;
}
```

**OR**

```http
POST /api/grpc-protos
Content-Type: text/plain

syntax = "proto3";
...
```

**Description:** Uploads and parses a gRPC .proto file to enable mock gRPC services. Accepts multipart/form-data (file upload) or text/plain (raw proto content).

**Response:**
```json
{
  "success": true,
  "protoName": "greeter",
  "services": ["Greeter"],
  "methods": ["SayHello"],
  "message": "Proto file uploaded successfully"
}
```

**Tags:** `gRPC Proto Management`

---

### List All Proto Files

```http
GET /api/grpc-protos
```

**Description:** Returns a list of all uploaded gRPC .proto files with their names, services, and message definitions.

**Response:**
```json
[
  {
    "name": "greeter",
    "services": [
      {
        "name": "Greeter",
        "methods": [
          {
            "name": "SayHello",
            "inputType": "HelloRequest",
            "outputType": "HelloReply"
          }
        ]
      }
    ],
    "uploadedAt": "2025-01-06T10:30:00Z"
  }
]
```

**Tags:** `gRPC Proto Management`

---

### Get Proto Details

```http
GET /api/grpc-protos/{protoName}
```

**Description:** Returns detailed information about a specific uploaded .proto file including all service definitions, methods, and message schemas.

**Path Parameters:**
- `protoName` (string): Name of the proto file (without .proto extension)

**Response:**
```json
{
  "name": "greeter",
  "content": "syntax = \"proto3\";\n...",
  "services": [...],
  "messages": [
    {
      "name": "HelloRequest",
      "fields": [
        {"name": "name", "type": "string", "number": 1}
      ]
    }
  ]
}
```

**Tags:** `gRPC Proto Management`

---

### Delete a Proto File

```http
DELETE /api/grpc-protos/{protoName}
```

**Description:** Removes a specific uploaded .proto file and all its associated mock gRPC services.

**Path Parameters:**
- `protoName` (string): Name of the proto file to delete

**Tags:** `gRPC Proto Management`

---

### Delete All Proto Files

```http
DELETE /api/grpc-protos
```

**Description:** Removes all uploaded .proto files and clears all mock gRPC services.

**Tags:** `gRPC Proto Management`

---

## gRPC Service Calls

Endpoints for invoking mock gRPC methods.

**Base Path:** `/api/grpc` (configurable)

### Invoke gRPC Unary Call (JSON)

```http
POST /api/grpc/{serviceName}/{methodName}
Content-Type: application/json

{
  "name": "World"
}
```

**Description:** Calls a mock gRPC unary method using JSON format. Send the request message as JSON and receive the response as JSON. Requires a .proto file to be uploaded first.

**Path Parameters:**
- `serviceName` (string): Name of the gRPC service (e.g., "Greeter")
- `methodName` (string): Name of the method to invoke (e.g., "SayHello")

**Request Body:** JSON object matching the request message schema

**Response:**
```json
{
  "message": "Hello, World!"
}
```

**Tags:** `gRPC Service Calls (JSON)`

---

### Invoke gRPC Unary Call (Protobuf)

```http
POST /api/grpc/proto/{serviceName}/{methodName}
Content-Type: application/grpc+proto

<binary protobuf data>
```

**Description:** Calls a mock gRPC unary method using binary Protobuf format. Send the request message as binary Protobuf and receive the response as binary Protobuf. Requires a .proto file to be uploaded first.

**Path Parameters:**
- `serviceName` (string): Name of the gRPC service
- `methodName` (string): Name of the method to invoke

**Request Body:** Binary Protobuf encoded message

**Response:** Binary Protobuf encoded response

**Content-Type:** `application/grpc+proto`

**Tags:** `gRPC Service Calls (Protobuf)`

---

## SignalR Hubs

Real-time communication endpoints using SignalR.

### OpenAPI Hub

**Path:** `/hub/openapi` (configurable)

**Description:** SignalR hub for real-time updates about OpenAPI spec changes, endpoint activity, and context updates.

**Events:**
- `SpecLoaded`: Fired when a new spec is loaded
- `SpecDeleted`: Fired when a spec is deleted
- `EndpointCalled`: Fired when a mock endpoint is invoked
- `ContextUpdated`: Fired when context data changes

### Mock Data Hub

**Path:** `/hub/mock` (default for SignalR background generation)

**Description:** SignalR hub for continuous background data generation. Configure hub contexts in `appsettings.json` to enable automatic data push.

**Configuration:**
```json
{
  "MockLlmApi": {
    "HubContexts": [
      {
        "Name": "weather",
        "Description": "Real-time weather data",
        "IsActive": true
      }
    ],
    "SignalRPushIntervalMs": 5000
  }
}
```

---

## Query Parameters

Many endpoints support common query parameters:

### Common Parameters

- `?contextName=session-123` - Associate request with a named context
- `?cache=5` - Use response caching with 5 variants
- `?autoChunk=false` - Disable automatic chunking
- `?backend=ollama-mistral` - Select specific LLM backend
- `?shape={...}` - Provide JSON shape for response generation
- `?count=50` - Request specific number of items

### Streaming Parameters

- `?continuous=true` - Enable continuous SSE streaming
- `?interval=2000` - Interval between continuous events (ms)
- `?maxDuration=600` - Max duration for continuous streams (seconds)
- `?sseMode=CompleteObjects` - SSE streaming mode (LlmTokens, CompleteObjects, ArrayItems)

### Error Simulation

- `?error=404` - Simulate specific HTTP error code
- `?errorMessage=Not%20found` - Custom error message (URL-encoded)
- `?errorDetails=Resource%20missing` - Additional error details (URL-encoded)

---

## HTTP Headers

### Request Headers

- `X-LLM-Backend: ollama-mistral` - Select specific backend
- `X-Context-Name: session-123` - Specify context name
- `X-Cache-Count: 5` - Enable caching with variants
- `X-Shape: {...}` - Provide JSON shape
- `X-Error-Code: 404` - Simulate error
- `X-Error-Message: Not found` - Error message
- `X-Continuous-Streaming: true` - Enable continuous SSE
- `Accept: text/event-stream` - Required for SSE streaming

### Response Headers

- `X-Cache-Hit: true/false` - Indicates if response came from cache
- `X-Chunk-Info: 1/5` - Chunk number and total chunks
- `X-Context-Name: session-123` - Context used for generation
- `X-Backend-Used: ollama-mistral` - Backend that handled the request

---

## Error Responses

All endpoints return consistent error responses:

```json
{
  "error": {
    "code": 404,
    "message": "Not Found",
    "details": "Specification 'invalid-spec' not found"
  }
}
```

**Common Status Codes:**
- `200 OK` - Success
- `400 Bad Request` - Invalid request data
- `404 Not Found` - Resource not found
- `415 Unsupported Media Type` - Invalid Content-Type
- `500 Internal Server Error` - Server-side error

---

## Authentication & Authorization

Currently, LLMock API does not include authentication or authorization. All endpoints are publicly accessible. This is by design for development and testing scenarios.

For production use with external access, consider:
- Adding an API gateway with authentication
- Using ASP.NET Core authentication middleware
- Implementing IP whitelisting
- Running behind a firewall

---

## Rate Limiting

No built-in rate limiting is provided. For high-volume testing:

- Use multiple LLM backends for load distribution
- Enable caching to reduce LLM calls
- Configure appropriate `MaxItems` and chunking settings
- Monitor LLM backend performance

---

## Versioning

API versioning is managed through the NuGet package version. Breaking changes will increment the major version number.

**Current Version:** 2.0.0

**Breaking Changes in 2.0:**
- `MaxInputTokens` and `MaxOutputTokens` replaced with `MaxContextWindow`
- Automatic allocation of input (75%) and output (25%) tokens

**Backward Compatibility:**
- Legacy `BaseUrl` and `ModelName` still supported
- All v1.x endpoints remain functional
- New endpoints use separate base paths

---

## OpenAPI/Swagger Documentation

The demo application generates comprehensive OpenAPI documentation automatically at:

**Swagger UI:** `http://localhost:5116/swagger`
**OpenAPI JSON:** `http://localhost:5116/swagger/v1/swagger.json`

All backend endpoints are fully documented with:
- Summary and detailed descriptions
- Request/response schemas
- Tags for organization
- Example requests and responses

---

## Related Documentation

- **[Configuration Reference](./CONFIGURATION_REFERENCE.md)** - All configuration options
- **[Multiple LLM Backends](./MULTIPLE_LLM_BACKENDS.md)** - Backend setup and selection
- **[OpenAPI Features](./OPENAPI-FEATURES.md)** - Dynamic OpenAPI mocking
- **[SSE Streaming Modes](./SSE_STREAMING_MODES.md)** - Server-Sent Events guide
- **[Continuous Streaming](./CONTINUOUS_STREAMING.md)** - Long-lived SSE connections
- **[Chunking & Caching](../CHUNKING_AND_CACHING.md)** - Performance optimization

---

**Last Updated:** 2025-01-06
**Version:** 2.0.0
