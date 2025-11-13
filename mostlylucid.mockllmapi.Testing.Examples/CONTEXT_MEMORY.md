# Context Memory Guide

## Overview

**Context Memory** is a powerful feature that maintains consistency across related API requests by storing previous interactions and reusing IDs, names, relationships, and other data. This enables realistic mock scenarios where data flows naturally between requests, just like in a real application.

## How It Works

When you specify a context name in your requests, the mock API:

1. **Extracts** the context name from your request
2. **Retrieves** the history of previous API interactions in that context
3. **Injects** the context history into the LLM prompt with instructions to maintain consistency
4. **Stores** the new response in the context for future requests

This creates a "memory" that spans multiple requests, making your mocks behave like a stateful backend.

## Specifying Context Names

Context names can be specified in **three ways** (in order of precedence):

### 1. Query Parameter (Highest Precedence)

```http
GET /api/mock/users?context=test-session
GET /api/mock/users?api-context=test-session
```

**Best for:** Quick testing, browser requests, explicit visibility

### 2. HTTP Header

```http
GET /api/mock/users
X-Api-Context: test-session
```

**Best for:** Programmatic clients, when URL should stay clean

### 3. Request Body Property

```json
{
  "context": "test-session",
  "userId": "123"
}
```

or:

```json
{
  "apiContext": "test-session",
  "userId": "123"
}
```

**Best for:** POST requests where shape is also in body

## Supported Mock Types

Context memory works across **all mock types**:

| Mock Type | Context Support | Notes |
|-----------|----------------|-------|
| **REST API** | ✅ Full | Query param, header, or body |
| **GraphQL** | ✅ Full | Maintains consistency in nested queries |
| **OpenAPI** | ✅ Full | Works with dynamically generated endpoints |
| **gRPC** | ✅ Full | Supports both JSON and binary Protobuf |
| **SignalR** | ✅ Full | Configured via `HubContextConfig.ApiContextName` |
| **Streaming** | ✅ Full | Consistent data across SSE streams |

## Examples by Mock Type

### REST API Context

```http
# Request 1: Create a user
POST /api/mock/users?context=shopping-flow
Content-Type: application/json

{
  "shape": {"id": "string", "name": "string", "email": "string"}
}

# Response 1:
{
  "id": "user-8472",
  "name": "Alice Johnson",
  "email": "alice.johnson@example.com"
}

# Request 2: Create order (automatically references user from context)
POST /api/mock/orders?context=shopping-flow
Content-Type: application/json

{
  "shape": {"orderId": "string", "userId": "string", "total": "number"}
}

# Response 2: (Notice userId matches from context!)
{
  "orderId": "order-2847",
  "userId": "user-8472",  # ← Same as previous request
  "total": 149.99
}
```

### GraphQL Context with Nested Queries

```http
# Request 1: Create post with author
POST /graphql?context=blog-context
Content-Type: application/json

{
  "query": "mutation { createPost(input: {title: \"Hello\"}) { id title author { id name } } }"
}

# Response 1:
{
  "data": {
    "createPost": {
      "id": "post-123",
      "title": "Hello World",
      "author": {
        "id": "author-456",
        "name": "John Doe"
      }
    }
  }
}

# Request 2: Query author with nested posts (maintains consistency)
POST /graphql?context=blog-context
Content-Type: application/json

{
  "query": "{ author(id: \"1\") { id name posts { id title } } }"
}

# Response 2: (Author data matches!)
{
  "data": {
    "author": {
      "id": "author-456",  # ← Same author ID
      "name": "John Doe",   # ← Same name
      "posts": [
        {
          "id": "post-123",  # ← Includes previous post
          "title": "Hello World"
        }
      ]
    }
  }
}
```

### OpenAPI Context

```http
# Request 1: Create a pet
POST /petstore/pets
Content-Type: application/json
X-Api-Context: petstore-test

{
  "name": "Fluffy",
  "status": "available"
}

# Response 1:
{
  "id": 42,
  "name": "Fluffy",
  "status": "available",
  "tag": "cat"
}

# Request 2: Get pet (references created pet)
GET /petstore/pets/42?context=petstore-test

# Response 2: (Consistent data!)
{
  "id": 42,
  "name": "Fluffy",
  "status": "available",
  "tag": "cat"
}
```

### gRPC Context

```http
# Request 1: Create user
POST /api/grpc/json/UserService/CreateUser?context=grpc-test
Content-Type: application/json

{
  "name": "Bob Smith",
  "email": "bob@example.com"
}

# Response 1:
{
  "userId": "usr-789",
  "name": "Bob Smith",
  "email": "bob@example.com"
}

# Request 2: Get user (maintains consistency)
POST /api/grpc/json/UserService/GetUser?context=grpc-test
Content-Type: application/json

{
  "userId": "usr-789"
}

# Response 2: (Same user data!)
{
  "userId": "usr-789",
  "name": "Bob Smith",
  "email": "bob@example.com",
  "createdAt": "2024-01-15T10:30:00Z"
}
```

### SignalR Context

SignalR contexts are configured at the hub level, not per-request:

```csharp
builder.Services.AddMockLlmApi(options =>
{
    options.SignalRHubs = new List<HubContextConfig>
    {
        new HubContextConfig
        {
            Name = "chatHub",
            Path = "/hubs/chat",
            ApiContextName = "chat-session",  // All hub methods share this context
            Methods = new List<HubMethodConfig>
            {
                new HubMethodConfig
                {
                    Name = "SendMessage",
                    ResponseShape = "{\"messageId\": \"string\", \"userId\": \"string\"}"
                },
                new HubMethodConfig
                {
                    Name = "GetHistory",
                    ResponseShape = "{\"messages\": [{\"messageId\": \"string\", \"userId\": \"string\", \"text\": \"string\"}]}"
                }
            }
        }
    };
});
```

All methods on the hub will maintain consistency (e.g., `SendMessage` creates a message with `userId: "usr-123"`, then `GetHistory` includes that same user ID).

## Cross-API Context

Context memory works **across different mock types** using the same context name:

```http
# 1. Create entity via REST
POST /api/mock/entities?context=cross-api
{"shape": {"id": "string", "name": "string"}}

# Response: {"id": "ent-456", "name": "Widget"}

# 2. Query via GraphQL (maintains consistency)
POST /graphql?context=cross-api
{"query": "{ entity(id: \"1\") { id name } }"}

# Response: {"data": {"entity": {"id": "ent-456", "name": "Widget"}}}

# 3. Call gRPC (full consistency maintained)
POST /api/grpc/json/EntityService/GetEntity?context=cross-api
{"entityId": "ent-456"}

# Response: {"entityId": "ent-456", "name": "Widget", ...}
```

## Configuration

### Context Expiration

Contexts automatically expire after a period of inactivity to prevent memory leaks. Configure via `appsettings.json`:

```json
{
  "MockLlmApi": {
    "ContextExpirationMinutes": 15
  }
}
```

**Recommended values:**
- **15 minutes** (default): Good balance for most scenarios
- **60+ minutes**: Long test sessions, integration tests
- **5 minutes**: Memory-constrained environments, quick unit tests

Expired contexts are automatically cleaned up by a background service.

### Disabling Context Memory

To disable context memory for specific requests, simply don't provide a context name. The request will be processed without any context history.

## Best Practices

### ✅ DO

1. **Use descriptive context names** that indicate the test scenario:
   ```
   user-registration-flow
   checkout-with-payment
   blog-post-creation
   multi-tenant-test-org-123
   ```

2. **Create separate contexts for unrelated scenarios**:
   ```http
   # Scenario A
   POST /api/mock/users?context=scenario-a

   # Scenario B (completely independent)
   POST /api/mock/users?context=scenario-b
   ```

3. **Use consistent context names across related requests**:
   ```http
   POST /api/mock/users?context=test-1
   GET /api/mock/orders?context=test-1
   POST /graphql?context=test-1
   ```

4. **Leverage query parameters for visibility**:
   ```http
   GET /api/mock/data?context=my-test&other=params
   # Clearly visible in logs, network tab, etc.
   ```

5. **Use contexts for end-to-end test scenarios**:
   ```csharp
   [Fact]
   public async Task CheckoutFlow_ShouldMaintainConsistency()
   {
       var context = "checkout-test-" + Guid.NewGuid();

       // All requests use same context
       var user = await CreateUser(context);
       var cart = await AddToCart(context, user.Id);
       var order = await Checkout(context, cart.Id);

       // Order should reference user and cart IDs
       Assert.Equal(user.Id, order.UserId);
   }
   ```

### ❌ DON'T

1. **Don't reuse context names across unrelated tests** - causes data pollution
2. **Don't rely on context for security testing** - it's for mock consistency only
3. **Don't use extremely long context names** - keep them concise
4. **Don't expect context to persist forever** - respect expiration settings
5. **Don't use context to "fix" bad test design** - isolate tests properly

## Troubleshooting

### Problem: Data not consistent across requests

**Symptoms:** IDs, names, or other data differ between requests that should be related.

**Solutions:**
1. Verify the same context name is used in all requests
2. Check for typos in context name
3. Ensure context hasn't expired (check `ContextExpirationMinutes`)
4. Review request logs to confirm context is being extracted

### Problem: Context expires too quickly

**Symptoms:** Consistency lost partway through test sequence.

**Solutions:**
1. Increase `ContextExpirationMinutes` in configuration
2. Reduce delays between requests
3. For long-running tests, consider renewing context with periodic requests

### Problem: Context not working for SignalR

**Symptoms:** SignalR hub methods don't maintain consistency.

**Solutions:**
1. Ensure `ApiContextName` is set in `HubContextConfig` (not per-request)
2. Verify hub is properly registered with context configuration
3. Check SignalR connection logs for context information

### Problem: Too much old data in responses

**Symptoms:** Responses include data from previous test runs or unrelated requests.

**Solutions:**
1. Use a new context name (e.g., append timestamp or GUID)
2. Wait for context to expire naturally
3. Lower `ContextExpirationMinutes` to 5 for faster cleanup
4. Ensure test cleanup is proper

### Problem: Context not extracted from request body

**Symptoms:** Body contains `"context": "name"` but context is not applied.

**Solutions:**
1. Ensure property is named `"context"` or `"apiContext"` (case-insensitive)
2. Verify JSON is valid and properly formatted
3. Try using query parameter instead: `?context=name`
4. Check that Content-Type header is `application/json`

### Problem: Context differs across backend providers

**Symptoms:** Context works with Ollama but not OpenAI (or vice versa).

**Solutions:**
1. Context storage is provider-independent - should work with all backends
2. Verify backend configuration is correct
3. Check provider-specific token limits (context history counts against limits)
4. Review LLM logs to see if context is included in prompts

## Implementation Details

### How Context is Stored

Context data is stored in-memory using `OpenApiContextManager`:

```csharp
public class OpenApiContextManager
{
    // Stores: contextName -> List of (timestamp, method, path, request, response)
    private readonly ConcurrentDictionary<string, List<ApiInteraction>> _contexts;

    public void AddToContext(string contextName, string method, string path,
                            string request, string response)
    {
        // Stores interaction with timestamp for expiration
    }

    public string GetContextForPrompt(string contextName)
    {
        // Returns formatted history for LLM prompt
    }
}
```

### How Context is Injected into LLM Prompts

When generating a response, the system:

1. Retrieves context history via `_contextManager.GetContextForPrompt(contextName)`
2. Formats history as:
   ```
   Previous API interactions in this context:

   [2024-01-15 10:30:00] POST /api/mock/users
   Request: {"name": "Alice"}
   Response: {"id": "user-123", "name": "Alice"}

   [2024-01-15 10:31:00] GET /api/mock/orders
   Response: {"orderId": "ord-456", "userId": "user-123"}
   ```
3. Adds instruction: "IMPORTANT: Maintain consistency with previous data. Reuse IDs, names, and relationships from the context above."
4. Sends combined prompt to LLM

### Expiration and Cleanup

The `ContextCleanupService` runs every 5 minutes:

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    while (!stoppingToken.IsCancellationRequested)
    {
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        _contextManager.CleanupExpiredContexts();
    }
}
```

Contexts are removed if their last interaction timestamp exceeds `ContextExpirationMinutes`.

## Advanced Scenarios

### Multi-Tenant Testing

Use context names to isolate tenant data:

```http
POST /api/mock/users?context=tenant-123
POST /api/mock/users?context=tenant-456
```

### Parallel Test Execution

Each test gets a unique context:

```csharp
var context = $"test-{Guid.NewGuid()}";
```

### Integration Test Workflows

Chain multiple operations:

```http
# 1. Register
POST /api/mock/register?context=integration-1
# 2. Login
POST /api/mock/login?context=integration-1
# 3. Update profile
POST /api/mock/profile?context=integration-1
# 4. Verify (all data consistent)
GET /api/mock/profile?context=integration-1
```

## See Also

- [ContextExamples.http](./ContextExamples.http) - HTTP file with runnable examples
- [Testing Utilities](../mostlylucid.mockllmapi.Testing/README.md) - Programmatic testing with contexts
- [Docker Guide](../../docs/DOCKER_GUIDE.md) - Using contexts in containerized environments
- [OpenAPI Features](../../docs/OPENAPI-FEATURES.md) - Context with dynamically generated endpoints
