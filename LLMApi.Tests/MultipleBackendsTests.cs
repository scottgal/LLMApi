using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using mostlylucid.mockllmapi;
using mostlylucid.mockllmapi.Models;
using mostlylucid.mockllmapi.Services;
using mostlylucid.mockllmapi.Services.Providers;

namespace LLMApi.Tests;

public class LlmProviderFactoryTests
{
    private readonly LlmProviderFactory _factory;
    private readonly Mock<ILogger<LlmProviderFactory>> _loggerMock;

    public LlmProviderFactoryTests()
    {
        _loggerMock = new Mock<ILogger<LlmProviderFactory>>();
        _factory = new LlmProviderFactory(_loggerMock.Object);
    }

    [Fact]
    public void GetProvider_ReturnsOllamaProvider_WhenOllamaRequested()
    {
        // Act
        var provider = _factory.GetProvider("ollama");

        // Assert
        Assert.NotNull(provider);
        Assert.IsType<OllamaProvider>(provider);
        Assert.Equal("ollama", provider.Name);
    }

    [Fact]
    public void GetProvider_ReturnsOpenAIProvider_WhenOpenAIRequested()
    {
        // Act
        var provider = _factory.GetProvider("openai");

        // Assert
        Assert.NotNull(provider);
        Assert.IsType<OpenAIProvider>(provider);
        Assert.Equal("openai", provider.Name);
    }

    [Fact]
    public void GetProvider_ReturnsLMStudioProvider_WhenLMStudioRequested()
    {
        // Act
        var provider = _factory.GetProvider("lmstudio");

        // Assert
        Assert.NotNull(provider);
        Assert.IsType<LMStudioProvider>(provider);
        Assert.Equal("lmstudio", provider.Name);
    }

    [Fact]
    public void GetProvider_IsCaseInsensitive()
    {
        // Act
        var provider1 = _factory.GetProvider("OLLAMA");
        var provider2 = _factory.GetProvider("OpenAI");
        var provider3 = _factory.GetProvider("LmStudio");

        // Assert
        Assert.IsType<OllamaProvider>(provider1);
        Assert.IsType<OpenAIProvider>(provider2);
        Assert.IsType<LMStudioProvider>(provider3);
    }

    [Fact]
    public void GetProvider_ReturnsOllama_WhenProviderNameIsNull()
    {
        // Act
        var provider = _factory.GetProvider(null!);

        // Assert
        Assert.NotNull(provider);
        Assert.IsType<OllamaProvider>(provider);
    }

    [Fact]
    public void GetProvider_ReturnsOllama_WhenProviderNameIsEmpty()
    {
        // Act
        var provider = _factory.GetProvider(string.Empty);

        // Assert
        Assert.NotNull(provider);
        Assert.IsType<OllamaProvider>(provider);
    }

    [Fact]
    public void GetProvider_ReturnsOllama_WhenProviderNameIsUnknown()
    {
        // Act
        var provider = _factory.GetProvider("unknown-provider");

        // Assert
        Assert.NotNull(provider);
        Assert.IsType<OllamaProvider>(provider);
    }

    [Fact]
    public void GetAvailableProviders_ReturnsAllProviders()
    {
        // Act
        var providers = _factory.GetAvailableProviders().ToList();

        // Assert
        Assert.Equal(3, providers.Count);
        Assert.Contains("ollama", providers, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("openai", providers, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("lmstudio", providers, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void RegisterProvider_AddsCustomProvider()
    {
        // Arrange
        var customProvider = new OllamaProvider(); // Using OllamaProvider as a stand-in

        // Act
        _factory.RegisterProvider("custom", customProvider);
        var provider = _factory.GetProvider("custom");

        // Assert
        Assert.Same(customProvider, provider);
    }
}

public class LlmBackendSelectorTests
{
    private readonly Mock<ILogger<LlmBackendSelector>> _loggerMock;

    public LlmBackendSelectorTests()
    {
        _loggerMock = new Mock<ILogger<LlmBackendSelector>>();
    }

    [Fact]
    public void SelectBackend_ReturnsLegacyBackend_WhenNoBackendsConfigured()
    {
        // Arrange
        var options = new LLMockApiOptions
        {
            BaseUrl = "http://localhost:11434/v1/",
            ModelName = "llama3",
            LlmBackends = new List<LlmBackendConfig>()
        };
        var selector = new LlmBackendSelector(Options.Create(options), _loggerMock.Object);

        // Act
        var backend = selector.SelectBackend();

        // Assert
        Assert.NotNull(backend);
        Assert.Equal("default", backend.Name);
        Assert.Equal("ollama", backend.Provider);
        Assert.Equal("http://localhost:11434/v1/", backend.BaseUrl);
        Assert.Equal("llama3", backend.ModelName);
    }

    [Fact]
    public void SelectBackend_ReturnsFirstEnabledBackend_WhenOneBackendConfigured()
    {
        // Arrange
        var options = new LLMockApiOptions
        {
            LlmBackends = new List<LlmBackendConfig>
            {
                new()
                {
                    Name = "ollama-llama3",
                    Provider = "ollama",
                    BaseUrl = "http://localhost:11434/v1/",
                    ModelName = "llama3",
                    Enabled = true
                }
            }
        };
        var selector = new LlmBackendSelector(Options.Create(options), _loggerMock.Object);

        // Act
        var backend = selector.SelectBackend();

        // Assert
        Assert.NotNull(backend);
        Assert.Equal("ollama-llama3", backend.Name);
        Assert.Equal("llama3", backend.ModelName);
    }

    [Fact]
    public void SelectBackend_RoundRobins_WhenMultipleBackendsConfigured()
    {
        // Arrange
        var options = new LLMockApiOptions
        {
            LlmBackends = new List<LlmBackendConfig>
            {
                new()
                {
                    Name = "backend1", Provider = "ollama", BaseUrl = "http://localhost:11434/v1/",
                    ModelName = "llama3", Enabled = true
                },
                new()
                {
                    Name = "backend2", Provider = "ollama", BaseUrl = "http://localhost:11435/v1/",
                    ModelName = "mistral", Enabled = true
                },
                new()
                {
                    Name = "backend3", Provider = "ollama", BaseUrl = "http://localhost:11436/v1/",
                    ModelName = "gemma3:4b", Enabled = true
                }
            }
        };
        var selector = new LlmBackendSelector(Options.Create(options), _loggerMock.Object);

        // Act
        var backend1 = selector.SelectBackend();
        var backend2 = selector.SelectBackend();
        var backend3 = selector.SelectBackend();
        var backend4 = selector.SelectBackend();

        // Assert
        Assert.NotNull(backend1);
        Assert.NotNull(backend2);
        Assert.NotNull(backend3);
        Assert.NotNull(backend4);
        Assert.Equal("backend1", backend1.Name);
        Assert.Equal("backend2", backend2.Name);
        Assert.Equal("backend3", backend3.Name);
        Assert.Equal("backend1", backend4.Name); // Should wrap around
    }

    [Fact]
    public void SelectBackend_SkipsDisabledBackends()
    {
        // Arrange
        var options = new LLMockApiOptions
        {
            LlmBackends = new List<LlmBackendConfig>
            {
                new()
                {
                    Name = "backend1", Provider = "ollama", BaseUrl = "http://localhost:11434/v1/",
                    ModelName = "llama3", Enabled = true
                },
                new()
                {
                    Name = "backend2", Provider = "ollama", BaseUrl = "http://localhost:11435/v1/",
                    ModelName = "mistral", Enabled = false
                },
                new()
                {
                    Name = "backend3", Provider = "ollama", BaseUrl = "http://localhost:11436/v1/",
                    ModelName = "gemma3:4b", Enabled = true
                }
            }
        };
        var selector = new LlmBackendSelector(Options.Create(options), _loggerMock.Object);

        // Act
        var backend1 = selector.SelectBackend();
        var backend2 = selector.SelectBackend();
        var backend3 = selector.SelectBackend();

        // Assert
        Assert.NotNull(backend1);
        Assert.NotNull(backend2);
        Assert.NotNull(backend3);
        Assert.Equal("backend1", backend1.Name);
        Assert.Equal("backend3", backend2.Name); // Skips backend2
        Assert.Equal("backend1", backend3.Name); // Wraps around
    }

    [Fact]
    public void SelectBackend_UsesRequestedBackend_WhenHeaderProvided()
    {
        // Arrange
        var options = new LLMockApiOptions
        {
            LlmBackends = new List<LlmBackendConfig>
            {
                new()
                {
                    Name = "backend1", Provider = "ollama", BaseUrl = "http://localhost:11434/v1/",
                    ModelName = "llama3", Enabled = true
                },
                new()
                {
                    Name = "backend2", Provider = "ollama", BaseUrl = "http://localhost:11435/v1/",
                    ModelName = "mistral", Enabled = true
                }
            }
        };
        var selector = new LlmBackendSelector(Options.Create(options), _loggerMock.Object);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-LLM-Backend"] = "backend2";

        // Act
        var backend = selector.SelectBackend(httpContext.Request);

        // Assert
        Assert.NotNull(backend);
        Assert.Equal("backend2", backend.Name);
    }

    [Fact]
    public void SelectBackend_UsesRequestedBackend_WhenQueryParamProvided()
    {
        // Arrange
        var options = new LLMockApiOptions
        {
            LlmBackends = new List<LlmBackendConfig>
            {
                new()
                {
                    Name = "backend1", Provider = "ollama", BaseUrl = "http://localhost:11434/v1/",
                    ModelName = "llama3", Enabled = true
                },
                new()
                {
                    Name = "backend2", Provider = "ollama", BaseUrl = "http://localhost:11435/v1/",
                    ModelName = "mistral", Enabled = true
                }
            }
        };
        var selector = new LlmBackendSelector(Options.Create(options), _loggerMock.Object);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = new QueryString("?backend=backend2");

        // Act
        var backend = selector.SelectBackend(httpContext.Request);

        // Assert
        Assert.NotNull(backend);
        Assert.Equal("backend2", backend.Name);
    }

    [Fact]
    public void SelectBackend_HeaderTakesPrecedence_OverQueryParam()
    {
        // Arrange
        var options = new LLMockApiOptions
        {
            LlmBackends = new List<LlmBackendConfig>
            {
                new()
                {
                    Name = "backend1", Provider = "ollama", BaseUrl = "http://localhost:11434/v1/",
                    ModelName = "llama3", Enabled = true
                },
                new()
                {
                    Name = "backend2", Provider = "ollama", BaseUrl = "http://localhost:11435/v1/",
                    ModelName = "mistral", Enabled = true
                }
            }
        };
        var selector = new LlmBackendSelector(Options.Create(options), _loggerMock.Object);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-LLM-Backend"] = "backend1";
        httpContext.Request.QueryString = new QueryString("?backend=backend2");

        // Act
        var backend = selector.SelectBackend(httpContext.Request);

        // Assert
        Assert.NotNull(backend);
        Assert.Equal("backend1", backend.Name); // Header wins
    }

    [Fact]
    public void GetBackendByName_ReturnsBackend_WhenNameMatches()
    {
        // Arrange
        var options = new LLMockApiOptions
        {
            LlmBackends = new List<LlmBackendConfig>
            {
                new()
                {
                    Name = "test-backend", Provider = "ollama", BaseUrl = "http://localhost:11434/v1/",
                    ModelName = "llama3", Enabled = true
                }
            }
        };
        var selector = new LlmBackendSelector(Options.Create(options), _loggerMock.Object);

        // Act
        var backend = selector.GetBackendByName("test-backend");

        // Assert
        Assert.NotNull(backend);
        Assert.Equal("test-backend", backend.Name);
    }

    [Fact]
    public void GetBackendByName_IsCaseInsensitive()
    {
        // Arrange
        var options = new LLMockApiOptions
        {
            LlmBackends = new List<LlmBackendConfig>
            {
                new()
                {
                    Name = "Test-Backend", Provider = "ollama", BaseUrl = "http://localhost:11434/v1/",
                    ModelName = "llama3", Enabled = true
                }
            }
        };
        var selector = new LlmBackendSelector(Options.Create(options), _loggerMock.Object);

        // Act
        var backend = selector.GetBackendByName("test-backend");

        // Assert
        Assert.NotNull(backend);
        Assert.Equal("Test-Backend", backend.Name);
    }

    [Fact]
    public void GetBackendByName_ReturnsNull_WhenNameNotFound()
    {
        // Arrange
        var options = new LLMockApiOptions
        {
            LlmBackends = new List<LlmBackendConfig>
            {
                new()
                {
                    Name = "backend1", Provider = "ollama", BaseUrl = "http://localhost:11434/v1/",
                    ModelName = "llama3", Enabled = true
                }
            }
        };
        var selector = new LlmBackendSelector(Options.Create(options), _loggerMock.Object);

        // Act
        var backend = selector.GetBackendByName("non-existent");

        // Assert
        Assert.Null(backend);
    }

    [Fact]
    public void HasMultipleBackends_ReturnsTrue_WhenMultipleEnabledBackends()
    {
        // Arrange
        var options = new LLMockApiOptions
        {
            LlmBackends = new List<LlmBackendConfig>
            {
                new()
                {
                    Name = "backend1", Provider = "ollama", BaseUrl = "http://localhost:11434/v1/",
                    ModelName = "llama3", Enabled = true
                },
                new()
                {
                    Name = "backend2", Provider = "ollama", BaseUrl = "http://localhost:11435/v1/",
                    ModelName = "mistral", Enabled = true
                }
            }
        };
        var selector = new LlmBackendSelector(Options.Create(options), _loggerMock.Object);

        // Act
        var result = selector.HasMultipleBackends();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasMultipleBackends_ReturnsFalse_WhenOnlyOneEnabledBackend()
    {
        // Arrange
        var options = new LLMockApiOptions
        {
            LlmBackends = new List<LlmBackendConfig>
            {
                new()
                {
                    Name = "backend1", Provider = "ollama", BaseUrl = "http://localhost:11434/v1/",
                    ModelName = "llama3", Enabled = true
                },
                new()
                {
                    Name = "backend2", Provider = "ollama", BaseUrl = "http://localhost:11435/v1/",
                    ModelName = "mistral", Enabled = false
                }
            }
        };
        var selector = new LlmBackendSelector(Options.Create(options), _loggerMock.Object);

        // Act
        var result = selector.HasMultipleBackends();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void SelectFromBackends_SelectsFromNamedBackends()
    {
        // Arrange
        var options = new LLMockApiOptions
        {
            LlmBackends = new List<LlmBackendConfig>
            {
                new()
                {
                    Name = "backend1", Provider = "ollama", BaseUrl = "http://localhost:11434/v1/",
                    ModelName = "llama3", Enabled = true
                },
                new()
                {
                    Name = "backend2", Provider = "ollama", BaseUrl = "http://localhost:11435/v1/",
                    ModelName = "mistral", Enabled = true
                },
                new()
                {
                    Name = "backend3", Provider = "ollama", BaseUrl = "http://localhost:11436/v1/",
                    ModelName = "gemma3:4b", Enabled = true
                }
            }
        };
        var selector = new LlmBackendSelector(Options.Create(options), _loggerMock.Object);
        var backendNames = new[] { "backend1", "backend3" };

        // Act
        var backend1 = selector.SelectFromBackends(backendNames);
        var backend2 = selector.SelectFromBackends(backendNames);
        var backend3 = selector.SelectFromBackends(backendNames);

        // Assert
        Assert.NotNull(backend1);
        Assert.NotNull(backend2);
        Assert.NotNull(backend3);
        Assert.Contains(backend1.Name, backendNames);
        Assert.Contains(backend2.Name, backendNames);
        Assert.Contains(backend3.Name, backendNames);
        Assert.DoesNotContain("backend2", new[] { backend1.Name, backend2.Name, backend3.Name });
    }

    [Fact]
    public void SelectFromBackends_FallsBackToDefault_WhenNoMatchingBackends()
    {
        // Arrange
        var options = new LLMockApiOptions
        {
            LlmBackends = new List<LlmBackendConfig>
            {
                new()
                {
                    Name = "backend1", Provider = "ollama", BaseUrl = "http://localhost:11434/v1/",
                    ModelName = "llama3", Enabled = true
                }
            }
        };
        var selector = new LlmBackendSelector(Options.Create(options), _loggerMock.Object);
        var backendNames = new[] { "non-existent1", "non-existent2" };

        // Act
        var backend = selector.SelectFromBackends(backendNames);

        // Assert
        Assert.NotNull(backend);
        Assert.Equal("backend1", backend.Name); // Falls back to default selection
    }

    [Fact]
    public void SelectFromBackends_FallsBackToDefault_WhenEmptyArray()
    {
        // Arrange
        var options = new LLMockApiOptions
        {
            LlmBackends = new List<LlmBackendConfig>
            {
                new()
                {
                    Name = "backend1", Provider = "ollama", BaseUrl = "http://localhost:11434/v1/",
                    ModelName = "llama3", Enabled = true
                }
            }
        };
        var selector = new LlmBackendSelector(Options.Create(options), _loggerMock.Object);

        // Act
        var backend = selector.SelectFromBackends(Array.Empty<string>());

        // Assert
        Assert.NotNull(backend);
        Assert.Equal("backend1", backend.Name);
    }
}

public class ProviderTests
{
    [Fact]
    public void OllamaProvider_HasCorrectName()
    {
        // Arrange
        var provider = new OllamaProvider();

        // Assert
        Assert.Equal("ollama", provider.Name);
    }

    [Fact]
    public void OpenAIProvider_HasCorrectName()
    {
        // Arrange
        var provider = new OpenAIProvider();

        // Assert
        Assert.Equal("openai", provider.Name);
    }

    [Fact]
    public void LMStudioProvider_HasCorrectName()
    {
        // Arrange
        var provider = new LMStudioProvider();

        // Assert
        Assert.Equal("lmstudio", provider.Name);
    }

    [Fact]
    public void OllamaProvider_ConfiguresClient_WithOptionalApiKey()
    {
        // Arrange
        var provider = new OllamaProvider();
        var client = new HttpClient();
        var backend = new LlmBackendConfig
        {
            BaseUrl = "http://localhost:11434/v1/",
            ApiKey = "test-api-key"
        };

        // Act
        client.BaseAddress = new Uri(backend.BaseUrl);
        provider.ConfigureClient(client, backend.ApiKey);

        // Assert
        Assert.NotNull(client.BaseAddress);
        Assert.Equal("http://localhost:11434/v1/", client.BaseAddress.ToString());
        // Ollama doesn't require API key, but should accept it
    }

    [Fact]
    public void OpenAIProvider_ConfiguresClient_WithApiKey()
    {
        // Arrange
        var provider = new OpenAIProvider();
        var client = new HttpClient();
        var backend = new LlmBackendConfig
        {
            BaseUrl = "https://api.openai.com/v1/",
            ApiKey = "sk-test-key"
        };

        // Act
        client.BaseAddress = new Uri(backend.BaseUrl);
        provider.ConfigureClient(client, backend.ApiKey);

        // Assert
        Assert.NotNull(client.BaseAddress);
        Assert.Equal("https://api.openai.com/v1/", client.BaseAddress.ToString());
        Assert.True(client.DefaultRequestHeaders.Contains("Authorization"));
    }

    [Fact]
    public void LMStudioProvider_ConfiguresClient_WithBaseUrl()
    {
        // Arrange
        var provider = new LMStudioProvider();
        var client = new HttpClient();
        var backend = new LlmBackendConfig
        {
            BaseUrl = "http://localhost:1234/v1/"
        };

        // Act
        client.BaseAddress = new Uri(backend.BaseUrl);
        provider.ConfigureClient(client, backend.ApiKey);

        // Assert
        Assert.NotNull(client.BaseAddress);
        Assert.Equal("http://localhost:1234/v1/", client.BaseAddress.ToString());
    }
}