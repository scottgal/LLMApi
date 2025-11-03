using Microsoft.Extensions.Logging;
using Moq;
using mostlylucid.mockllmapi.Models;
using mostlylucid.mockllmapi.Services;
using Xunit;

namespace LLMApi.Tests;

public class DynamicHubContextManagerTests
{
    private readonly Mock<ILogger<DynamicHubContextManager>> _mockLogger;
    private readonly DynamicHubContextManager _manager;

    public DynamicHubContextManagerTests()
    {
        _mockLogger = new Mock<ILogger<DynamicHubContextManager>>();
        _manager = new DynamicHubContextManager(_mockLogger.Object);
    }

    [Fact]
    public void RegisterContext_WithValidConfig_ReturnsTrue()
    {
        // Arrange
        var config = new HubContextConfig
        {
            Name = "test",
            Method = "GET",
            Path = "/test"
        };

        // Act
        var result = _manager.RegisterContext(config);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void RegisterContext_WithEmptyName_ReturnsFalse()
    {
        // Arrange
        var config = new HubContextConfig
        {
            Name = "",
            Method = "GET",
            Path = "/test"
        };

        // Act
        var result = _manager.RegisterContext(config);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void RegisterContext_WithNullName_ReturnsFalse()
    {
        // Arrange
        var config = new HubContextConfig
        {
            Name = null!,
            Method = "GET",
            Path = "/test"
        };

        // Act
        var result = _manager.RegisterContext(config);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void RegisterContext_WithDuplicateName_ReturnsFalse()
    {
        // Arrange
        var config1 = new HubContextConfig { Name = "test", Method = "GET", Path = "/test1" };
        var config2 = new HubContextConfig { Name = "test", Method = "POST", Path = "/test2" };

        // Act
        var result1 = _manager.RegisterContext(config1);
        var result2 = _manager.RegisterContext(config2);

        // Assert
        Assert.True(result1);
        Assert.False(result2);
    }

    [Fact]
    public void UnregisterContext_WithExistingContext_ReturnsTrue()
    {
        // Arrange
        var config = new HubContextConfig { Name = "test", Method = "GET", Path = "/test" };
        _manager.RegisterContext(config);

        // Act
        var result = _manager.UnregisterContext("test");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void UnregisterContext_WithNonExistentContext_ReturnsFalse()
    {
        // Act
        var result = _manager.UnregisterContext("nonexistent");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void GetContext_WithExistingContext_ReturnsConfig()
    {
        // Arrange
        var config = new HubContextConfig { Name = "test", Method = "GET", Path = "/test", Body = "test body" };
        _manager.RegisterContext(config);

        // Act
        var result = _manager.GetContext("test");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("test", result.Name);
        Assert.Equal("GET", result.Method);
        Assert.Equal("/test", result.Path);
        Assert.Equal("test body", result.Body);
    }

    [Fact]
    public void GetContext_WithNonExistentContext_ReturnsNull()
    {
        // Act
        var result = _manager.GetContext("nonexistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetAllContexts_WithNoContexts_ReturnsEmptyCollection()
    {
        // Act
        var result = _manager.GetAllContexts();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void GetAllContexts_WithMultipleContexts_ReturnsAllContexts()
    {
        // Arrange
        var config1 = new HubContextConfig { Name = "test1", Method = "GET", Path = "/test1" };
        var config2 = new HubContextConfig { Name = "test2", Method = "POST", Path = "/test2" };
        var config3 = new HubContextConfig { Name = "test3", Method = "PUT", Path = "/test3" };

        _manager.RegisterContext(config1);
        _manager.RegisterContext(config2);
        _manager.RegisterContext(config3);

        // Act
        var result = _manager.GetAllContexts();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Count);
        Assert.Contains(result, c => c.Name == "test1");
        Assert.Contains(result, c => c.Name == "test2");
        Assert.Contains(result, c => c.Name == "test3");
    }

    [Fact]
    public void ContextExists_WithExistingContext_ReturnsTrue()
    {
        // Arrange
        var config = new HubContextConfig { Name = "test", Method = "GET", Path = "/test" };
        _manager.RegisterContext(config);

        // Act
        var result = _manager.ContextExists("test");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ContextExists_WithNonExistentContext_ReturnsFalse()
    {
        // Act
        var result = _manager.ContextExists("nonexistent");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void RegisterContext_ThreadSafe_HandlesMultipleRegistrations()
    {
        // Arrange
        var tasks = new List<Task<bool>>();

        // Act
        for (int i = 0; i < 100; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                var config = new HubContextConfig { Name = $"test{index}", Method = "GET", Path = $"/test{index}" };
                return _manager.RegisterContext(config);
            }));
        }

        Task.WaitAll(tasks.ToArray());

        // Assert
        Assert.All(tasks, task => Assert.True(task.Result));
        Assert.Equal(100, _manager.GetAllContexts().Count);
    }

    [Fact]
    public void RegisterContext_WithDescription_StoresDescription()
    {
        // Arrange
        var config = new HubContextConfig
        {
            Name = "test",
            Description = "Test description",
            Method = "GET",
            Path = "/test"
        };

        // Act
        _manager.RegisterContext(config);
        var result = _manager.GetContext("test");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Test description", result.Description);
    }

    [Fact]
    public void RegisterContext_WithJsonSchema_StoresJsonSchemaFlag()
    {
        // Arrange
        var config = new HubContextConfig
        {
            Name = "test",
            Method = "GET",
            Path = "/test",
            Shape = "{\"type\":\"object\",\"properties\":{\"id\":{\"type\":\"number\"}}}",
            IsJsonSchema = true
        };

        // Act
        _manager.RegisterContext(config);
        var result = _manager.GetContext("test");

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsJsonSchema);
        Assert.Contains("\"type\":\"object\"", result.Shape);
    }

    [Fact]
    public void RegisterAndUnregister_MultipleOperations_MaintainsConsistency()
    {
        // Arrange & Act
        var config1 = new HubContextConfig { Name = "test1", Method = "GET", Path = "/test1" };
        var config2 = new HubContextConfig { Name = "test2", Method = "GET", Path = "/test2" };
        var config3 = new HubContextConfig { Name = "test3", Method = "GET", Path = "/test3" };

        _manager.RegisterContext(config1);
        _manager.RegisterContext(config2);
        _manager.RegisterContext(config3);

        Assert.Equal(3, _manager.GetAllContexts().Count);

        _manager.UnregisterContext("test2");

        // Assert
        Assert.Equal(2, _manager.GetAllContexts().Count);
        Assert.True(_manager.ContextExists("test1"));
        Assert.False(_manager.ContextExists("test2"));
        Assert.True(_manager.ContextExists("test3"));
    }
}
