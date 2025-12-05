# mostlylucid.mockllmapi.Testing

Testing utilities for **mostlylucid.mockllmapi**. Provides a fluent API for configuring mock LLM API responses and seamless HttpClient integration for easy testing.

## Installation

```bash
dotnet add package mostlylucid.mockllmapi.Testing
```

## Quick Start

### Basic Usage

```csharp
using mostlylucid.mockllmapi.Testing;

// Create a client with a single endpoint configuration
var client = HttpClientExtensions.CreateMockLlmClient(
    baseAddress: "http://localhost:5116",
    pathPattern: "/users",
    configure: endpoint => endpoint
        .WithShape(new { id = 0, name = "", email = "" })
        .WithCache(5)
);

// Make requests - configuration is automatically applied
var response = await client.GetAsync("/users");
var users = await response.Content.ReadFromJsonAsync<User[]>();
```

### Multiple Endpoints

```csharp
var client = HttpClientExtensions.CreateMockLlmClient(
    "http://localhost:5116",
    configure: handler => handler
        .ForEndpoint("/users", config => config
            .WithShape(new { id = 0, name = "", email = "" })
            .WithCache(10))
        .ForEndpoint("/posts", config => config
            .WithShape(new { id = 0, title = "", content = "", authorId = 0 })
            .WithCache(20))
        .ForEndpoint("/error", config => config
            .WithError(404, "Resource not found"))
);

// Each endpoint automatically uses its configuration
var usersResponse = await client.GetAsync("/users");
var postsResponse = await client.GetAsync("/posts");
var errorResponse = await client.GetAsync("/error"); // Returns 404
```

### Using the Handler Directly

```csharp
var handler = new MockLlmHttpHandler("/api/mock")
    .ForEndpoint("/products", config => config
        .WithShape(new { id = 0, name = "", price = 0.0 })
        .WithStreaming(true)
        .WithSseMode("CompleteObjects"))
    .ForEndpoint("/categories", config => config
        .WithShape(new { id = 0, name = "" }));

var client = new HttpClient(handler)
{
    BaseAddress = new Uri("http://localhost:5116")
};
```

## Configuration Options

### Shape Configuration

Define the JSON structure for mock responses:

```csharp
// Using anonymous objects
.WithShape(new { id = 0, name = "", active = true })

// Using JSON strings
.WithShape("{ \"id\": 0, \"name\": \"\", \"tags\": [] }")

// Complex nested structures
.WithShape(new
{
    user = new { id = 0, name = "" },
    posts = new[] { new { id = 0, title = "" } }
})
```

### Error Simulation

Configure error responses for testing error handling:

```csharp
// Simple error
.WithError(404)

// With custom message
.WithError(404, "User not found")

// With details
.WithError(422, "Validation failed", "Email address is invalid")
```

### Caching

Control response variation:

```csharp
// Generate 10 different variants
.WithCache(10)

// Always return the same response
.WithCache(1)
```

### Backend Selection

Choose which LLM backend to use:

```csharp
.WithBackend("openai")
.WithBackend("ollama")
.WithBackend("lmstudio")
```

### Streaming

Enable SSE streaming responses:

```csharp
// Enable streaming with token-by-token output
.WithStreaming()
.WithSseMode("LlmTokens")

// Stream complete objects
.WithStreaming()
.WithSseMode("CompleteObjects")

// Stream array items individually
.WithStreaming()
.WithSseMode("ArrayItems")

// Continuous streaming (like SignalR)
.WithContinuousStreaming(enabled: true, intervalMs: 2000)
```

### Custom Headers and Query Parameters

Add custom configuration:

```csharp
.WithHeader("X-Custom-Header", "value")
.WithQueryParameter("customParam", "value")
```

### Auto-Chunking

Control automatic chunking for large responses:

```csharp
.WithAutoChunking(true)
.WithMaxItems(100)
```

## Dependency Injection

### Typed Client

```csharp
services.AddMockLlmHttpClient<IUserApiClient>(
    baseApiPath: "/api/mock",
    configure: handler => handler
        .ForEndpoint("/users", config => config
            .WithShape(new { id = 0, name = "", email = "" }))
);
```

### Named Client

```csharp
services.AddMockLlmHttpClient(
    name: "MockApi",
    baseApiPath: "/api/mock",
    configure: handler => handler
        .ForEndpoint("/data", config => config
            .WithShape(new { value = 0 }))
);

// Usage
var client = httpClientFactory.CreateClient("MockApi");
```

## Testing Scenarios

### Integration Tests

```csharp
[Fact]
public async Task Should_Handle_User_Creation()
{
    // Arrange
    var client = HttpClientExtensions.CreateMockLlmClient(
        "http://localhost:5116",
        "/users",
        config => config
            .WithMethod("POST")
            .WithShape(new { id = 0, name = "", email = "", createdAt = "" })
    );

    // Act
    var newUser = new { name = "John Doe", email = "john@example.com" };
    var response = await client.PostAsJsonAsync("/users", newUser);

    // Assert
    response.EnsureSuccessStatusCode();
    var created = await response.Content.ReadFromJsonAsync<User>();
    Assert.NotNull(created);
    Assert.NotEqual(0, created.Id);
}

[Fact]
public async Task Should_Handle_Not_Found_Error()
{
    // Arrange
    var client = HttpClientExtensions.CreateMockLlmClient(
        "http://localhost:5116",
        "/users/999",
        config => config.WithError(404, "User not found")
    );

    // Act
    var response = await client.GetAsync("/users/999");

    // Assert
    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
}
```

### Testing Error Handling

```csharp
var client = HttpClientExtensions.CreateMockLlmClient(
    "http://localhost:5116",
    configure: handler => handler
        .ForEndpoint("/timeout", config => config
            .WithError(408, "Request timeout"))
        .ForEndpoint("/server-error", config => config
            .WithError(500, "Internal server error"))
        .ForEndpoint("/rate-limit", config => config
            .WithError(429, "Too many requests"))
);
```

### Testing Streaming

```csharp
var client = HttpClientExtensions.CreateMockLlmClient(
    "http://localhost:5116",
    "/stream/data",
    config => config
        .WithStreaming()
        .WithSseMode("CompleteObjects")
        .WithShape(new { id = 0, timestamp = "" })
);

var response = await client.GetAsync("/stream/data");
await using var stream = await response.Content.ReadAsStreamAsync();
using var reader = new StreamReader(stream);

while (!reader.EndOfStream)
{
    var line = await reader.ReadLineAsync();
    if (line?.StartsWith("data:") == true)
    {
        var json = line.Substring(5).Trim();
        var obj = JsonSerializer.Deserialize<DataObject>(json);
        // Process streamed object
    }
}
```

## Path Pattern Matching

The handler supports simple wildcard matching:

```csharp
// Exact match
.ForEndpoint("/users", ...)         // Matches: /users

// Wildcard suffix
.ForEndpoint("/api/users/*", ...)   // Matches: /api/users/123, /api/users/abc

// Wildcard prefix (matches by suffix)
.ForEndpoint("/users", ...)         // Also matches: /api/mock/users
```

## Advanced Usage

### Dynamic Configuration

```csharp
var handler = new MockLlmHttpHandler("/api/mock");

// Add endpoints dynamically
foreach (var endpoint in testEndpoints)
{
    handler.ForEndpoint(endpoint.Path, config =>
    {
        config.WithShape(endpoint.Shape);
        if (endpoint.ShouldFail)
        {
            config.WithError(endpoint.ErrorCode);
        }
    });
}
```

### Configuration Reset

```csharp
var handler = new MockLlmHttpHandler("/api/mock");

// Initial configuration
handler.ForEndpoint("/users", config => config.WithCache(5));

// Clear and reconfigure
handler.ClearEndpoints();
handler.ForEndpoint("/users", config => config.WithCache(10));
```

## How It Works

The `MockLlmHttpHandler` is a `DelegatingHandler` that:

1. Intercepts outgoing HTTP requests
2. Matches requests against configured endpoint patterns
3. Injects mock configuration via:
   - Query parameters (for shape, cache, backend, etc.)
   - HTTP headers (for errors, backend selection)
   - Path modification (for streaming)
4. Forwards the modified request to the actual mock LLM API

This approach allows you to use a real `HttpClient` in your tests while easily controlling the mock API behavior without modifying your application code.

## License

This project is released into the public domain under the [Unlicense](https://unlicense.org).
