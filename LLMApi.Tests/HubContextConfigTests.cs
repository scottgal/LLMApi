using mostlylucid.mockllmapi.Models;

namespace LLMApi.Tests;

public class HubContextConfigTests
{
    [Fact]
    public void Constructor_SetsDefaultValues()
    {
        // Act
        var config = new HubContextConfig();

        // Assert
        Assert.Equal("default", config.Name);
        Assert.Equal("GET", config.Method);
        Assert.Equal("/data", config.Path);
        Assert.Null(config.Body);
        Assert.Null(config.Shape);
        Assert.Null(config.IsJsonSchema);
        Assert.Null(config.Description);
    }

    [Fact]
    public void SetName_UpdatesValue()
    {
        // Arrange
        var config = new HubContextConfig();

        // Act
        config.Name = "test";

        // Assert
        Assert.Equal("test", config.Name);
    }

    [Fact]
    public void SetDescription_UpdatesValue()
    {
        // Arrange
        var config = new HubContextConfig();

        // Act
        config.Description = "Test description";

        // Assert
        Assert.Equal("Test description", config.Description);
    }

    [Fact]
    public void SetMethod_UpdatesValue()
    {
        // Arrange
        var config = new HubContextConfig();

        // Act
        config.Method = "POST";

        // Assert
        Assert.Equal("POST", config.Method);
    }

    [Fact]
    public void SetPath_UpdatesValue()
    {
        // Arrange
        var config = new HubContextConfig();

        // Act
        config.Path = "/test/path";

        // Assert
        Assert.Equal("/test/path", config.Path);
    }

    [Fact]
    public void SetBody_UpdatesValue()
    {
        // Arrange
        var config = new HubContextConfig();

        // Act
        config.Body = "{\"test\":\"value\"}";

        // Assert
        Assert.Equal("{\"test\":\"value\"}", config.Body);
    }

    [Fact]
    public void SetShape_UpdatesValue()
    {
        // Arrange
        var config = new HubContextConfig();

        // Act
        config.Shape = "{\"id\":0,\"name\":\"string\"}";

        // Assert
        Assert.Equal("{\"id\":0,\"name\":\"string\"}", config.Shape);
    }

    [Fact]
    public void SetIsJsonSchema_UpdatesValue()
    {
        // Arrange
        var config = new HubContextConfig();

        // Act
        config.IsJsonSchema = true;

        // Assert
        Assert.True(config.IsJsonSchema);
    }

    [Fact]
    public void InitializerSyntax_SetsAllProperties()
    {
        // Act
        var config = new HubContextConfig
        {
            Name = "weather",
            Description = "Weather data",
            Method = "GET",
            Path = "/weather/current",
            Body = "{\"city\":\"NYC\"}",
            Shape = "{\"temp\":0,\"condition\":\"string\"}",
            IsJsonSchema = false
        };

        // Assert
        Assert.Equal("weather", config.Name);
        Assert.Equal("Weather data", config.Description);
        Assert.Equal("GET", config.Method);
        Assert.Equal("/weather/current", config.Path);
        Assert.Equal("{\"city\":\"NYC\"}", config.Body);
        Assert.Equal("{\"temp\":0,\"condition\":\"string\"}", config.Shape);
        Assert.False(config.IsJsonSchema);
    }

    [Fact]
    public void NullValues_AreAllowed()
    {
        // Act
        var config = new HubContextConfig
        {
            Name = "test",
            Description = null,
            Body = null,
            Shape = null,
            IsJsonSchema = null
        };

        // Assert
        Assert.Equal("test", config.Name);
        Assert.Null(config.Description);
        Assert.Null(config.Body);
        Assert.Null(config.Shape);
        Assert.Null(config.IsJsonSchema);
    }
}