using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using mostlylucid.mockllmapi;
using mostlylucid.mockllmapi.Services;
using Xunit;

namespace LLMApi.Tests;

public class OpenApiContextManagerTests
{
    private OpenApiContextManager CreateManager()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<OpenApiContextManager>();
        var options = Microsoft.Extensions.Options.Options.Create(new LLMockApiOptions
        {
            MaxInputTokens = 2048
        });

        // Create real IMemoryCache and IContextStore for testing
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var contextStoreLogger = loggerFactory.CreateLogger<MemoryCacheContextStore>();
        var contextStore = new MemoryCacheContextStore(memoryCache, contextStoreLogger);

        return new OpenApiContextManager(logger, options, contextStore);
    }

    [Fact]
    public void AddToContext_WithValidData_CreatesContext()
    {
        // Arrange
        var manager = CreateManager();

        // Act
        manager.AddToContext("test-context", "GET", "/users/123", null, "{\"id\": 123, \"name\": \"Alice\"}");

        // Assert
        var context = manager.GetContext("test-context");
        Assert.NotNull(context);
        Assert.Equal("test-context", context.Name);
        Assert.Equal(1, context.TotalCalls);
        Assert.Single(context.RecentCalls);
    }

    [Fact]
    public void AddToContext_WithEmptyContextName_DoesNothing()
    {
        // Arrange
        var manager = CreateManager();

        // Act
        manager.AddToContext("", "GET", "/users/123", null, "{\"id\": 123}");
        manager.AddToContext(null!, "GET", "/users/123", null, "{\"id\": 123}");

        // Assert
        var contexts = manager.GetAllContexts();
        Assert.Empty(contexts);
    }

    [Fact]
    public void AddToContext_ExtractsSharedData_FromResponse()
    {
        // Arrange
        var manager = CreateManager();
        var response = "{\"id\": 456, \"userId\": 123, \"name\": \"Bob\", \"email\": \"bob@example.com\"}";

        // Act
        manager.AddToContext("test-context", "POST", "/users", null, response);

        // Assert
        var context = manager.GetContext("test-context");
        Assert.NotNull(context);
        Assert.Contains("lastId", context!.SharedData.Keys);
        Assert.Contains("lastUserId", context.SharedData.Keys);
        Assert.Contains("lastName", context.SharedData.Keys);
        Assert.Contains("lastEmail", context.SharedData.Keys);
        Assert.Equal("456", context.SharedData["lastId"]);
        Assert.Equal("123", context.SharedData["lastUserId"]);
        Assert.Equal("Bob", context.SharedData["lastName"]);
        Assert.Equal("bob@example.com", context.SharedData["lastEmail"]);
    }

    [Fact]
    public void AddToContext_ExtractsFromArrayResponse_UsesFirstItem()
    {
        // Arrange
        var manager = CreateManager();
        var response = "[{\"id\": 789, \"name\": \"Charlie\"}, {\"id\": 790, \"name\": \"David\"}]";

        // Act
        manager.AddToContext("test-context", "GET", "/users", null, response);

        // Assert
        var context = manager.GetContext("test-context");
        Assert.NotNull(context);
        Assert.Equal("789", context!.SharedData["lastId"]);
        Assert.Equal("Charlie", context.SharedData["lastName"]);
    }

    [Fact]
    public void AddToContext_InvalidJson_DoesNotCrash()
    {
        // Arrange
        var manager = CreateManager();

        // Act & Assert - should not throw
        manager.AddToContext("test-context", "GET", "/users", null, "not valid json");

        var context = manager.GetContext("test-context");
        Assert.NotNull(context);
        Assert.Equal(1, context.TotalCalls);
        Assert.Empty(context.SharedData); // No data extracted
    }

    [Fact]
    public void AddToContext_MultipleCalls_IncrementsTotalCalls()
    {
        // Arrange
        var manager = CreateManager();

        // Act
        manager.AddToContext("test-context", "GET", "/users/1", null, "{\"id\": 1}");
        manager.AddToContext("test-context", "GET", "/users/2", null, "{\"id\": 2}");
        manager.AddToContext("test-context", "POST", "/users", null, "{\"id\": 3}");

        // Assert
        var context = manager.GetContext("test-context");
        Assert.NotNull(context);
        Assert.Equal(3, context.TotalCalls);
        Assert.Equal(3, context.RecentCalls.Count);
    }

    [Fact]
    public void AddToContext_MoreThan20Calls_SummarizesOldOnes()
    {
        // Arrange
        var manager = CreateManager();

        // Act - Add 25 calls
        for (int i = 1; i <= 25; i++)
        {
            manager.AddToContext("test-context", "GET", $"/users/{i}", null, $"{{\"id\": {i}}}");
        }

        // Assert
        var context = manager.GetContext("test-context");
        Assert.NotNull(context);
        Assert.Equal(25, context.TotalCalls);
        Assert.True(context.RecentCalls.Count <= 15); // Should keep only recent ones
        Assert.NotEmpty(context.ContextSummary); // Should have summary
    }

    [Fact]
    public void GetContextForPrompt_WithNoContext_ReturnsNull()
    {
        // Arrange
        var manager = CreateManager();

        // Act
        var prompt = manager.GetContextForPrompt("nonexistent");

        // Assert
        Assert.Null(prompt);
    }

    [Fact]
    public void GetContextForPrompt_WithContext_ReturnsFormattedPrompt()
    {
        // Arrange
        var manager = CreateManager();
        manager.AddToContext("test-context", "GET", "/users/123", null, "{\"id\": 123, \"name\": \"Alice\"}");
        manager.AddToContext("test-context", "POST", "/orders", "{\"userId\": 123}", "{\"orderId\": 456}");

        // Act
        var prompt = manager.GetContextForPrompt("test-context");

        // Assert
        Assert.NotNull(prompt);
        Assert.Contains("API Context: test-context", prompt);
        Assert.Contains("Total calls in session: 2", prompt);
        Assert.Contains("/users/123", prompt);
        Assert.Contains("/orders", prompt);
        Assert.Contains("Shared data to maintain consistency", prompt);
        Assert.Contains("Recent API calls", prompt);
    }

    [Fact]
    public void GetContextForPrompt_IncludesSharedData()
    {
        // Arrange
        var manager = CreateManager();
        manager.AddToContext("test-context", "POST", "/users", null, "{\"id\": 789, \"name\": \"Eve\"}");

        // Act
        var prompt = manager.GetContextForPrompt("test-context");

        // Assert
        Assert.NotNull(prompt);
        Assert.Contains("lastId: 789", prompt);
        Assert.Contains("lastName: Eve", prompt);
    }

    [Fact]
    public void GetAllContexts_ReturnsAllContextSummaries()
    {
        // Arrange
        var manager = CreateManager();
        manager.AddToContext("context1", "GET", "/users/1", null, "{\"id\": 1}");
        manager.AddToContext("context2", "GET", "/users/2", null, "{\"id\": 2}");
        manager.AddToContext("context1", "GET", "/users/3", null, "{\"id\": 3}");

        // Act
        var contexts = manager.GetAllContexts();

        // Assert
        Assert.Equal(2, contexts.Count);
        Assert.Contains(contexts, c => c.Name == "context1" && c.TotalCalls == 2);
        Assert.Contains(contexts, c => c.Name == "context2" && c.TotalCalls == 1);
    }

    [Fact]
    public void ClearContext_ExistingContext_RemovesIt()
    {
        // Arrange
        var manager = CreateManager();
        manager.AddToContext("test-context", "GET", "/users/1", null, "{\"id\": 1}");

        // Act
        var removed = manager.ClearContext("test-context");

        // Assert
        Assert.True(removed);
        Assert.Null(manager.GetContext("test-context"));
    }

    [Fact]
    public void ClearContext_NonExistentContext_ReturnsFalse()
    {
        // Arrange
        var manager = CreateManager();

        // Act
        var removed = manager.ClearContext("nonexistent");

        // Assert
        Assert.False(removed);
    }

    [Fact]
    public void ClearAllContexts_RemovesAllContexts()
    {
        // Arrange
        var manager = CreateManager();
        manager.AddToContext("context1", "GET", "/users/1", null, "{\"id\": 1}");
        manager.AddToContext("context2", "GET", "/users/2", null, "{\"id\": 2}");
        manager.AddToContext("context3", "GET", "/users/3", null, "{\"id\": 3}");

        // Act
        manager.ClearAllContexts();

        // Assert
        var contexts = manager.GetAllContexts();
        Assert.Empty(contexts);
    }

    [Fact]
    public void Context_IsCaseInsensitive()
    {
        // Arrange
        var manager = CreateManager();
        manager.AddToContext("Test-Context", "GET", "/users/1", null, "{\"id\": 1}");

        // Act
        var context1 = manager.GetContext("test-context");
        var context2 = manager.GetContext("TEST-CONTEXT");
        var context3 = manager.GetContext("Test-Context");

        // Assert
        Assert.NotNull(context1);
        Assert.NotNull(context2);
        Assert.NotNull(context3);
        Assert.Same(context1, context2);
        Assert.Same(context2, context3);
    }

    [Fact]
    public void Context_TracksTimestamps()
    {
        // Arrange
        var manager = CreateManager();
        var beforeAdd = DateTimeOffset.UtcNow;

        // Act
        manager.AddToContext("test-context", "GET", "/users/1", null, "{\"id\": 1}");
        var afterAdd = DateTimeOffset.UtcNow;

        // Assert
        var context = manager.GetContext("test-context");
        Assert.NotNull(context);
        Assert.True(context.CreatedAt >= beforeAdd);
        Assert.True(context.CreatedAt <= afterAdd);
        Assert.True(context.LastUsedAt >= beforeAdd);
        Assert.True(context.LastUsedAt <= afterAdd);
    }

    [Fact]
    public void Context_UpdatesLastUsedAt_OnSubsequentCalls()
    {
        // Arrange
        var manager = CreateManager();
        manager.AddToContext("test-context", "GET", "/users/1", null, "{\"id\": 1}");
        var context1 = manager.GetContext("test-context");
        var firstLastUsed = context1!.LastUsedAt;

        // Wait a bit
        Thread.Sleep(10);

        // Act
        manager.AddToContext("test-context", "GET", "/users/2", null, "{\"id\": 2}");

        // Assert
        var context2 = manager.GetContext("test-context");
        Assert.NotNull(context2);
        Assert.True(context2.LastUsedAt > firstLastUsed);
        Assert.Equal(context1.CreatedAt, context2.CreatedAt); // CreatedAt doesn't change
    }

    [Fact]
    public void SharedData_OverwritesPreviousValues()
    {
        // Arrange
        var manager = CreateManager();

        // Act
        manager.AddToContext("test-context", "GET", "/users/1", null, "{\"id\": 100, \"name\": \"First\"}");
        manager.AddToContext("test-context", "GET", "/users/2", null, "{\"id\": 200, \"name\": \"Second\"}");

        // Assert
        var context = manager.GetContext("test-context");
        Assert.NotNull(context);
        Assert.Equal("200", context!.SharedData["lastId"]); // Latest value
        Assert.Equal("Second", context.SharedData["lastName"]); // Latest value
    }

    [Fact]
    public void GetContextForPrompt_TruncatesLongJson()
    {
        // Arrange
        var manager = CreateManager();
        var longResponse = "{\"data\": \"" + new string('x', 1000) + "\"}";
        manager.AddToContext("test-context", "GET", "/users/1", null, longResponse);

        // Act
        var prompt = manager.GetContextForPrompt("test-context");

        // Assert
        Assert.NotNull(prompt);
        Assert.Contains("...", prompt); // Should be truncated
        Assert.True(prompt.Length < longResponse.Length + 500); // Significantly shorter
    }

    [Fact]
    public void GetContextForPrompt_WithRequestBody_IncludesIt()
    {
        // Arrange
        var manager = CreateManager();
        var requestBody = "{\"name\": \"New User\", \"email\": \"new@example.com\"}";
        manager.AddToContext("test-context", "POST", "/users", requestBody, "{\"id\": 999}");

        // Act
        var prompt = manager.GetContextForPrompt("test-context");

        // Assert
        Assert.NotNull(prompt);
        Assert.Contains("Request:", prompt);
        Assert.Contains("New User", prompt);
    }
}
