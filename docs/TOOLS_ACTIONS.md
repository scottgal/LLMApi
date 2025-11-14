# Pluggable Tools & Actions

**Added in:** v2.1.0
**Use case:** Call external APIs, create decision trees, compose complex workflows
**MCP-compatible:** Ready for future LLM-driven tool selection

## Overview

MockLLMApi now supports **pluggable tools** - configurable actions that can be executed during mock API requests. This enables:

- 📡 **External API calls** - Fetch real data from external services
- 🌳 **Decision trees** - Chain multiple mock endpoints together
- 🔄 **Workflow composition** - Build complex multi-step scenarios
- 🤖 **MCP compatibility** - Architecture ready for LLM-driven tool selection

### Key Features

- ✅ **HTTP tools** - Call external REST APIs with authentication
- ✅ **Mock tools** - Call other mock endpoints for decision trees
- ✅ **Template substitution** - Use `{paramName}` placeholders in URLs, bodies, headers
- ✅ **Environment variables** - Reference env vars with `${ENV_VAR_NAME}`
- ✅ **JSONPath extraction** - Extract specific fields from responses
- ✅ **Result caching** - Cache tool results per request
- ✅ **Infinite loop prevention** - Configurable max chain depth
- ✅ **Concurrency control** - Limit parallel tool executions
- ✅ **MCP-compatible** - Ready for Phase 2 LLM-driven selection

## Table of Contents

- [Quick Start](#quick-start)
- [Tool Types](#tool-types)
- [Configuration](#configuration)
- [Usage Examples](#usage-examples)
- [Execution Modes](#execution-modes)
- [Authentication](#authentication)
- [Response Handling](#response-handling)
- [Decision Trees](#decision-trees)
- [Advanced Scenarios](#advanced-scenarios)
- [Roadmap](#roadmap)
- [API Reference](#api-reference)

---

## Quick Start

### 1. Enable Tools in Configuration

```json
{
  "MockLlmApi": {
    "ToolExecutionMode": "Explicit",
    "Tools": [
      {
        "Name": "getUserData",
        "Type": "http",
        "Description": "Fetch user data from JSONPlaceholder",
        "Enabled": true,
        "HttpConfig": {
          "Endpoint": "https://jsonplaceholder.typicode.com/users/{userId}",
          "Method": "GET"
        }
      }
    ]
  }
}
```

### 2. Call Tool via Query Parameter

```http
GET /api/mock/orders?useTool=getUserData&userId=1
```

### 3. Tool Result Merged into LLM Context

The tool result is automatically added to the LLM's context, so the generated mock data can reference real user information!

---

## Tool Types

### HTTP Tools

Call external REST APIs with full authentication and templating support.

**Use cases:**
- Fetch real user data from your production API
- Call third-party services (weather, stock prices, etc.)
- Integrate with internal microservices
- Validate API responses against real data

**Example:**
```json
{
  "Name": "getWeather",
  "Type": "http",
  "Description": "Fetch current weather data",
  "HttpConfig": {
    "Endpoint": "https://api.openweathermap.org/data/2.5/weather",
    "Method": "GET",
    "Headers": {
      "Authorization": "Bearer ${WEATHER_API_KEY}"
    },
    "ResponsePath": "$.main.temp"
  }
}
```

### Mock Tools

Call other mock endpoints to create decision trees and workflow compositions.

**Use cases:**
- Chain multiple mock endpoints together
- Build complex multi-step workflows
- Create conditional logic flows
- Simulate distributed systems

**Example:**
```json
{
  "Name": "getUserOrders",
  "Type": "mock",
  "Description": "Get order history from another mock endpoint",
  "MockConfig": {
    "Endpoint": "/api/mock/users/{userId}/orders",
    "Method": "GET",
    "QueryParams": {
      "limit": "10",
      "status": "completed"
    },
    "Shape": "{\"orders\":[{\"id\":\"string\",\"total\":0.0}]}"
  }
}
```

---

## Configuration

Add tools to your `appsettings.json`:

```json
{
  "MockLlmApi": {
    // Execution mode
    "ToolExecutionMode": "Explicit",  // Disabled|Explicit|LlmDriven

    // Safety limits
    "MaxConcurrentTools": 5,
    "MaxToolChainDepth": 3,

    // Response options
    "IncludeToolResultsInResponse": false,

    // Tool definitions
    "Tools": [
      {
        "Name": "toolName",
        "Type": "http",  // or "mock"
        "Description": "Human-readable description",
        "Enabled": true,
        "TimeoutMs": 10000,
        "EnableCaching": false,
        "CacheDurationMinutes": 5,

        // HTTP-specific config
        "HttpConfig": {
          "Endpoint": "https://api.example.com/resource/{id}",
          "Method": "GET",
          "Headers": {
            "Authorization": "Bearer ${API_TOKEN}",
            "X-Custom-Header": "value"
          },
          "BodyTemplate": "{\"key\":\"{value}\"}",
          "ResponsePath": "$.data",
          "AuthType": "bearer",  // none|bearer|basic|apikey
          "AuthToken": "${API_TOKEN}"
        },

        // Mock-specific config
        "MockConfig": {
          "Endpoint": "/api/mock/path/{param}",
          "Method": "GET",
          "QueryParams": {
            "key": "value"
          },
          "Shape": "{\"field\":\"value\"}",
          "ContextName": "shared-context"
        },

        // Parameter schema (for future LLM-driven mode)
        "Parameters": {
          "userId": {
            "Type": "string",
            "Description": "User ID to fetch",
            "Required": true
          }
        }
      }
    ]
  }
}
```

### Configuration Options

#### Global Settings

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `ToolExecutionMode` | enum | `Disabled` | Tool execution mode (see [Execution Modes](#execution-modes)) |
| `MaxConcurrentTools` | int | `5` | Maximum tools to execute in parallel per request |
| `MaxToolChainDepth` | int | `3` | Maximum recursion depth to prevent infinite loops |
| `IncludeToolResultsInResponse` | bool | `false` | Include tool execution results in response JSON |

#### Tool-Specific Settings

| Option | Type | Required | Description |
|--------|------|----------|-------------|
| `Name` | string | ✅ | Unique tool identifier |
| `Type` | string | ✅ | Tool type: `http` or `mock` |
| `Description` | string | ❌ | Human-readable description (used in LLM prompts) |
| `Enabled` | bool | ❌ | Enable/disable tool (default: `true`) |
| `TimeoutMs` | int | ❌ | Execution timeout in milliseconds (default: `10000`) |
| `EnableCaching` | bool | ❌ | Cache tool results (default: `false`) |
| `CacheDurationMinutes` | int | ❌ | Cache duration (default: `5`) |

---

## Usage Examples

### Basic HTTP Tool

**Configuration:**
```json
{
  "Name": "getUserById",
  "Type": "http",
  "HttpConfig": {
    "Endpoint": "https://jsonplaceholder.typicode.com/users/{userId}",
    "Method": "GET"
  }
}
```

**Request:**
```http
GET /api/mock/dashboard?useTool=getUserById&userId=1
```

**What Happens:**
1. Tool calls `https://jsonplaceholder.typicode.com/users/1`
2. Response is merged into LLM context
3. LLM generates dashboard data that references real user info

### Multiple Tools

Call multiple tools in parallel:

```http
GET /api/mock/profile?useTool=getUserById,getWeather&userId=1&city=London
```

Or via header:
```http
GET /api/mock/profile?userId=1&city=London
X-Use-Tool: getUserById,getWeather
```

### Tool Parameters

Parameters are extracted from:
1. **Query parameters** - `?userId=123&city=London`
2. **Request body** - JSON fields become parameters
3. **Mixed** - Query params override body fields

**Example with POST body:**
```http
POST /api/mock/analysis?useTool=analyzeData
Content-Type: application/json

{
  "datasetId": "abc123",
  "startDate": "2024-01-01",
  "endDate": "2024-01-31"
}
```

Tool receives parameters: `datasetId`, `startDate`, `endDate`

### Tool Result Caching

Enable caching for expensive operations:

```json
{
  "Name": "getStockPrice",
  "Type": "http",
  "EnableCaching": true,
  "CacheDurationMinutes": 5,
  "HttpConfig": {
    "Endpoint": "https://api.stocks.com/quote/{symbol}"
  }
}
```

Cache key is computed from tool name + parameters.

---

## Execution Modes

### Disabled (Default)

Tools are completely disabled. No tool calls are possible.

```json
{
  "ToolExecutionMode": "Disabled"
}
```

### Explicit (Phase 1 - Current)

Tools are only called when explicitly requested via query parameter or header.

```json
{
  "ToolExecutionMode": "Explicit"
}
```

**Request:**
```http
GET /api/mock/data?useTool=myTool&param=value
# OR
GET /api/mock/data?param=value
X-Use-Tool: myTool
```

**Precedence:**
1. Query parameter (`?useTool=`)
2. HTTP header (`X-Use-Tool:`)

### LlmDriven (Phase 2 - Future)

LLM decides which tools to call based on the request and available tool descriptions.

```json
{
  "ToolExecutionMode": "LlmDriven"
}
```

**How it works (Phase 2):**
1. LLM receives request + list of available tools
2. LLM generates response with tool calls: `TOOL_CALL: toolName(param=value)`
3. System executes tools and feeds results back to LLM
4. LLM generates final response using tool results

---

## Authentication

### Bearer Token

```json
{
  "HttpConfig": {
    "AuthType": "bearer",
    "AuthToken": "${API_TOKEN}"  // References environment variable
  }
}
```

Environment variable resolution:
- `${VAR_NAME}` → Reads from `Environment.GetEnvironmentVariable("VAR_NAME")`
- Works in `AuthToken`, `Username`, `Password`, and anywhere in templates

### Basic Auth

```json
{
  "HttpConfig": {
    "AuthType": "basic",
    "Username": "myuser",
    "Password": "${API_PASSWORD}"
  }
}
```

### API Key Header

```json
{
  "HttpConfig": {
    "AuthType": "apikey",
    "AuthToken": "${API_KEY}"  // Adds X-API-Key header
  }
}
```

### Custom Headers

```json
{
  "HttpConfig": {
    "AuthType": "none",
    "Headers": {
      "Authorization": "Custom ${MY_TOKEN}",
      "X-Custom-Auth": "${CUSTOM_KEY}"
    }
  }
}
```

---

## Response Handling

### JSONPath Extraction

Extract specific fields from JSON responses:

```json
{
  "HttpConfig": {
    "Endpoint": "https://api.example.com/data",
    "ResponsePath": "$.data.users[0].email"
  }
}
```

**Supported paths:**
- `$.data` - Top-level field
- `$.user.profile.name` - Nested field
- `null` - Return entire response (default)

**Note:** Current implementation supports simple dot notation. For complex paths, entire response is returned.

### Tool Results in Response

Enable `IncludeToolResultsInResponse` to see tool execution details:

```json
{
  "IncludeToolResultsInResponse": true
}
```

**Response format:**
```json
{
  "data": {
    // Your mock API response
  },
  "toolResults": [
    {
      "toolName": "getUserById",
      "success": true,
      "data": {
        "id": 1,
        "name": "John Doe"
      },
      "executionTimeMs": 234,
      "metadata": {
        "StatusCode": 200
      }
    }
  ]
}
```

### Tool Results in LLM Context

By default, tool results are merged into the LLM's context:

```
Tool Results:
Tool 'getUserById' result:
{"id":1,"name":"John Doe","email":"john@example.com"}
```

The LLM can then reference this data in its generated response.

---

## Decision Trees

Create complex workflows by chaining mock endpoints together.

### Example: Multi-Step Checkout Flow

**Step 1: Main endpoint with tool call**
```http
GET /api/mock/checkout/summary?useTool=getUserCart,calculateShipping&userId=123
```

**Tools configured:**
```json
{
  "Tools": [
    {
      "Name": "getUserCart",
      "Type": "mock",
      "MockConfig": {
        "Endpoint": "/api/mock/cart/{userId}",
        "Method": "GET",
        "Shape": "{\"items\":[{\"productId\":\"string\",\"qty\":0}]}"
      }
    },
    {
      "Name": "calculateShipping",
      "Type": "mock",
      "MockConfig": {
        "Endpoint": "/api/mock/shipping/calculate",
        "Method": "POST",
        "Body": "{\"items\":[],\"address\":{\"zip\":\"{zipCode}\"}}"
      }
    }
  ]
}
```

**Execution flow:**
1. `getUserCart` calls `/api/mock/cart/123` (another mock endpoint)
2. `calculateShipping` calls `/api/mock/shipping/calculate` (another mock endpoint)
3. Both results merged into context
4. Main endpoint generates checkout summary using cart + shipping data

### Nested Tool Calls

Tools can call tools (up to `MaxToolChainDepth`):

```
Request → Tool A (mock) → Tool B (http) → Tool C (mock)
          ↓
          LLM Context ← Results from A, B, C
```

**Safety limits prevent infinite loops:**
- `MaxToolChainDepth: 3` - Maximum recursion depth
- `MaxConcurrentTools: 5` - Maximum parallel executions

---

## Advanced Scenarios

### Conditional Logic with Context

Use contexts to maintain state across tool chains:

```json
{
  "Name": "getUserOrders",
  "Type": "mock",
  "MockConfig": {
    "Endpoint": "/api/mock/orders",
    "ContextName": "user-session"  // Share context across tool calls
  }
}
```

All tools using the same `ContextName` share conversation history.

### Dynamic API Endpoints

Build URLs dynamically from request parameters:

```json
{
  "HttpConfig": {
    "Endpoint": "https://api.example.com/{apiVersion}/users/{userId}",
    "Method": "GET"
  }
}
```

Request: `?useTool=myTool&apiVersion=v2&userId=123`
Calls: `https://api.example.com/v2/users/123`

### POST with Template Body

Send dynamic JSON bodies:

```json
{
  "HttpConfig": {
    "Endpoint": "https://api.example.com/search",
    "Method": "POST",
    "BodyTemplate": "{\"query\":\"{searchTerm}\",\"filters\":{\"category\":\"{category}\"}}"
  }
}
```

Request: `?useTool=search&searchTerm=laptop&category=electronics`
Body: `{"query":"laptop","filters":{"category":"electronics"}}`

### External API + Mock Combination

Fetch real data, then use it in mock responses:

**Tools:**
```json
[
  {
    "Name": "getRealUserProfile",
    "Type": "http",
    "HttpConfig": {
      "Endpoint": "https://prod-api.example.com/users/{userId}"
    }
  },
  {
    "Name": "getRecommendations",
    "Type": "mock",
    "MockConfig": {
      "Endpoint": "/api/mock/recommendations/{userId}"
    }
  }
]
```

**Request:**
```http
GET /api/mock/dashboard?useTool=getRealUserProfile,getRecommendations&userId=123
```

**Result:** Dashboard with real user data + mock recommendations tailored to that user.

---

## Roadmap

### ✅ Phase 1: Explicit Tool Execution (Current - v2.1.0)

- HTTP and Mock tool executors
- Template substitution and authentication
- Tool registry and orchestration
- Safety limits (recursion, concurrency)
- Result caching

### 🔄 Phase 2: LLM-Driven Tool Selection (Planned - v2.2.0)

**Features:**
- LLM sees available tools in system prompt
- Function calling API for structured tool invocation
- Automatic tool selection based on request
- Multi-turn tool execution (tool → LLM → tool → LLM)
- Tool result interpretation by LLM

**Example:**
```
User: "Show me the weather for the user's location"
↓
LLM: TOOL_CALL: getUserLocation(userId=123)
↓
System: Executes tool, returns {"city": "London"}
↓
LLM: TOOL_CALL: getWeather(city=London)
↓
System: Executes tool, returns weather data
↓
LLM: Generates response: "The weather in London is..."
```

### 🚀 Phase 3: Advanced Decision Trees (Future - v2.3.0)

**Features:**
- Conditional tool execution based on results
- Parallel tool execution with dependencies
- Tool composition and workflows
- Error handling and retries
- Tool versioning and rollback

---

## API Reference

### Query Parameters

| Parameter | Type | Description | Example |
|-----------|------|-------------|---------|
| `useTool` | string | Comma-separated tool names | `?useTool=tool1,tool2` |
| `{paramName}` | any | Tool parameter values | `?userId=123&city=London` |

### Request Headers

| Header | Type | Description | Example |
|--------|------|-------------|---------|
| `X-Use-Tool` | string | Comma-separated tool names | `X-Use-Tool: tool1,tool2` |

### Response Headers

None added by tool system (uses standard mock API headers).

### Configuration Schema

```typescript
interface ToolConfig {
  Name: string;                    // Unique identifier
  Type: "http" | "mock";          // Tool type
  Description?: string;            // Human-readable description
  Enabled?: boolean;               // Default: true
  TimeoutMs?: number;              // Default: 10000
  EnableCaching?: boolean;         // Default: false
  CacheDurationMinutes?: number;   // Default: 5
  HttpConfig?: HttpToolConfig;     // For Type="http"
  MockConfig?: MockToolConfig;     // For Type="mock"
  Parameters?: Record<string, ParameterSchema>;  // For LLM-driven mode
}

interface HttpToolConfig {
  Endpoint: string;                // URL with {param} placeholders
  Method: string;                  // GET, POST, PUT, DELETE, PATCH
  Headers?: Record<string, string>;// Custom headers
  BodyTemplate?: string;           // JSON body with {param} placeholders
  ResponsePath?: string;           // JSONPath to extract (e.g., "$.data")
  AuthType?: "none" | "bearer" | "basic" | "apikey";
  AuthToken?: string;              // For bearer/apikey
  Username?: string;               // For basic auth
  Password?: string;               // For basic auth
}

interface MockToolConfig {
  Endpoint: string;                // Mock endpoint path
  Method: string;                  // HTTP method
  QueryParams?: Record<string, string>;  // Query parameters
  Body?: string;                   // Request body
  Shape?: string;                  // Response shape
  ContextName?: string;            // Shared context name
}

interface ParameterSchema {
  Type: "string" | "number" | "boolean" | "object" | "array";
  Description: string;
  Required?: boolean;
  Default?: any;
  Enum?: any[];
}
```

---

## Troubleshooting

### Tool Not Found

**Error:** `Tool 'myTool' not found`

**Solutions:**
1. ✅ Check tool name spelling
2. ✅ Verify `"Enabled": true` in config
3. ✅ Confirm `ToolExecutionMode` is not `Disabled`
4. ✅ Check logs for tool registration errors

### Tool Execution Failed

**Error:** `Tool 'myTool' threw exception: ...`

**Solutions:**
1. ✅ Check endpoint URL is valid
2. ✅ Verify authentication credentials
3. ✅ Increase `TimeoutMs` if slow API
4. ✅ Check network connectivity
5. ✅ Review tool execution logs

### Template Substitution Not Working

**Problem:** URL contains literal `{userId}` instead of value

**Solutions:**
1. ✅ Pass parameter in query string: `?userId=123`
2. ✅ Or in request body: `{"userId": 123}`
3. ✅ Check parameter name matches exactly (case-sensitive)

### Environment Variable Not Resolved

**Problem:** `${API_KEY}` appears literally in requests

**Solutions:**
1. ✅ Set environment variable: `export API_KEY=your-key`
2. ✅ Restart application after setting env var
3. ✅ Check env var name matches exactly (case-sensitive)

### Max Chain Depth Reached

**Error:** `Tool chain depth limit reached (3)`

**Solutions:**
1. ✅ Increase `MaxToolChainDepth` in config
2. ✅ Review tool chain for unnecessary nesting
3. ✅ Check for accidental infinite loops

### Tool Results Not in Context

**Problem:** LLM doesn't reference tool results

**Solutions:**
1. ✅ Tool results ARE added to context automatically
2. ✅ LLM may not use them if unrelated to request
3. ✅ Enable `IncludeToolResultsInResponse: true` to verify execution
4. ✅ Check tool execution succeeded (no errors)

---

## Performance Considerations

### Latency

- **HTTP tools** add network latency (external API response time)
- **Mock tools** are faster but still add overhead
- **Parallel execution** helps when calling multiple tools
- **Caching** significantly improves repeat requests

**Recommendations:**
- Enable caching for stable data (weather, stock prices, etc.)
- Use parallel execution when tools are independent
- Set appropriate timeouts to fail fast

### Memory

- Tool results stored in request scope (per-request memory)
- Caching increases memory usage
- Large tool responses can impact LLM context size

**Recommendations:**
- Use `ResponsePath` to extract only needed fields
- Limit `MaxConcurrentTools` to control memory
- Monitor cache size if caching is heavily used

### Security

- **Environment variables** - Never commit API keys to config
- **Authentication** - Always use secure methods (Bearer tokens)
- **Validation** - Tool parameters come from user input (validate!)
- **Rate limiting** - External APIs may have rate limits

**Recommendations:**
- Store secrets in environment variables or Key Vault
- Implement rate limiting on your mock API endpoints
- Validate and sanitize all tool parameters
- Use HTTPS for all external API calls

---

## Examples Repository

See `LLMApi/LLMApi.http` for complete working examples of all tool scenarios.

## Related Documentation

- [Main README](../README.md) - Project overview
- [Rate Limiting & Batching](./RATE_LIMITING_BATCHING.md) - Rate limiting features
- [Multiple LLM Backends](./MULTIPLE_LLM_BACKENDS.md) - Backend configuration
- [API Contexts](./API-CONTEXTS.md) - Context management

---

## Feedback & Support

Found a bug or have a feature request?
Open an issue: https://github.com/scottgal/mostlylucid.mockllmapi/issues

Want to implement Phase 2 (LLM-driven) yourself?
The architecture is ready! See `ToolOrchestrator.ParseToolCallsFromLlmResponse()` for the entry point.

## License

This feature is part of MockLLMApi and is released under the Unlicense.
