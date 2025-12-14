using System.Text.Json;
using mostlylucid.mockllmapi.Testing;

Console.WriteLine("=== mostlylucid.mockllmapi.Testing Examples ===\n");

// Example 1: Basic usage with a single endpoint
Console.WriteLine("Example 1: Basic Usage");
await Example1_BasicUsage();

// Example 2: Multiple endpoints
Console.WriteLine("\nExample 2: Multiple Endpoints");
await Example2_MultipleEndpoints();

// Example 3: Error simulation
Console.WriteLine("\nExample 3: Error Simulation");
await Example3_ErrorSimulation();

// Example 4: Streaming
Console.WriteLine("\nExample 4: Streaming");
await Example4_Streaming();

// Example 5: Using the handler directly
Console.WriteLine("\nExample 5: Direct Handler Usage");
await Example5_DirectHandler();

Console.WriteLine("\n=== All examples completed! ===");

static async Task Example1_BasicUsage()
{
    // Create a client configured for a single endpoint
    var client = HttpClientExtensions.CreateMockLlmClient(
        "http://localhost:5116",
        "/users",
        endpoint => endpoint
            .WithShape(new { id = 0, name = "", email = "", active = true })
            .WithCache(3) // Generate 3 different variants
    );

    // Make multiple requests - each will return a different variant
    for (var i = 0; i < 3; i++)
        try
        {
            var response = await client.GetAsync("/users");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var users = JsonSerializer.Deserialize<JsonElement>(json);

            Console.WriteLine($"  Request {i + 1}: Got {users.GetArrayLength()} users");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Error: {ex.Message}");
            Console.WriteLine("  Note: Make sure the mock API is running at http://localhost:5116");
        }
}

static async Task Example2_MultipleEndpoints()
{
    // Configure multiple endpoints with different shapes
    var client = HttpClientExtensions.CreateMockLlmClient(
        "http://localhost:5116",
        configure: handler => handler
            .ForEndpoint("/users", config => config
                .WithShape(new { id = 0, name = "", email = "" })
                .WithCache(5))
            .ForEndpoint("/posts", config => config
                .WithShape(new { id = 0, title = "", content = "", authorId = 0 })
                .WithCache(5))
            .ForEndpoint("/comments", config => config
                .WithShape(new { id = 0, text = "", postId = 0, userId = 0 })
                .WithCache(5))
    );

    try
    {
        // Each endpoint automatically uses its configuration
        var usersResponse = await client.GetAsync("/users");
        var postsResponse = await client.GetAsync("/posts");
        var commentsResponse = await client.GetAsync("/comments");

        Console.WriteLine($"  Users: {usersResponse.StatusCode}");
        Console.WriteLine($"  Posts: {postsResponse.StatusCode}");
        Console.WriteLine($"  Comments: {commentsResponse.StatusCode}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  Error: {ex.Message}");
    }
}

static async Task Example3_ErrorSimulation()
{
    // Configure different error scenarios
    var client = HttpClientExtensions.CreateMockLlmClient(
        "http://localhost:5116",
        configure: handler => handler
            .ForEndpoint("/not-found", config => config
                .WithError(404, "Resource not found"))
            .ForEndpoint("/unauthorized", config => config
                .WithError(401, "Unauthorized", "Invalid API key"))
            .ForEndpoint("/server-error", config => config
                .WithError(500, "Internal server error"))
    );

    try
    {
        var notFoundResponse = await client.GetAsync("/not-found");
        Console.WriteLine(
            $"  Not Found: {notFoundResponse.StatusCode} - {await notFoundResponse.Content.ReadAsStringAsync()}");

        var unauthorizedResponse = await client.GetAsync("/unauthorized");
        Console.WriteLine(
            $"  Unauthorized: {unauthorizedResponse.StatusCode} - {await unauthorizedResponse.Content.ReadAsStringAsync()}");

        var serverErrorResponse = await client.GetAsync("/server-error");
        Console.WriteLine(
            $"  Server Error: {serverErrorResponse.StatusCode} - {await serverErrorResponse.Content.ReadAsStringAsync()}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  Error: {ex.Message}");
    }
}

static async Task Example4_Streaming()
{
    // Configure streaming endpoint
    var client = HttpClientExtensions.CreateMockLlmClient(
        "http://localhost:5116",
        "/data",
        config => config
            .WithShape(new { id = 0, value = "", timestamp = "" })
            .WithStreaming()
            .WithSseMode("CompleteObjects")
    );

    try
    {
        var response = await client.GetAsync("/data");
        response.EnsureSuccessStatusCode();

        Console.WriteLine($"  Streaming response received with content type: {response.Content.Headers.ContentType}");

        // Read first few lines of the stream
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        var eventCount = 0;
        while (!reader.EndOfStream && eventCount < 5)
        {
            var line = await reader.ReadLineAsync();
            if (line?.StartsWith("data:") == true)
            {
                eventCount++;
                Console.WriteLine($"  Event {eventCount}: {line.Substring(0, Math.Min(50, line.Length))}...");
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  Error: {ex.Message}");
    }
}

static async Task Example5_DirectHandler()
{
    // Use the handler directly for more control
    var handler = new MockLlmHttpHandler("/api/mock")
        .ForEndpoint("/products", config => config
            .WithShape(new { id = 0, name = "", price = 0.0, category = "" })
            .WithCache(10)
            .WithBackend("ollama"))
        .ForEndpoint("/categories", config => config
            .WithShape(new { id = 0, name = "", description = "" })
            .WithCache(5));

    var client = new HttpClient(handler)
    {
        BaseAddress = new Uri("http://localhost:5116")
    };

    try
    {
        var productsResponse = await client.GetAsync("/products");
        var categoriesResponse = await client.GetAsync("/categories");

        Console.WriteLine($"  Products: {productsResponse.StatusCode}");
        Console.WriteLine($"  Categories: {categoriesResponse.StatusCode}");

        if (productsResponse.IsSuccessStatusCode)
        {
            var json = await productsResponse.Content.ReadAsStringAsync();
            var products = JsonSerializer.Deserialize<JsonElement>(json);
            Console.WriteLine($"  Got {products.GetArrayLength()} products");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  Error: {ex.Message}");
    }
}