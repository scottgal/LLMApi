using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using mostlylucid.mockllmapi;
using mostlylucid.mockllmapi.RequestHandlers;
using mostlylucid.mockllmapi.Services;
using mostlylucid.mockllmapi.Services.Providers;
using mostlylucid.mockllmapi.Services.Tools;

namespace LLMApi.Tests;

/// <summary>
///     Integration tests for service registration to ensure all DI dependencies resolve correctly.
///     These tests verify that AddLLMockApi and related methods properly register all required services.
/// </summary>
public class ServiceRegistrationTests
{
    #region Tool System Registration Tests

    [Fact]
    public void AddLLMockApi_RegistersToolSystem()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MockLlmApi:BaseUrl"] = "http://localhost:11434/v1/"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddLLMockApi(configuration);

        // Assert
        var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<ToolRegistry>());
        Assert.NotNull(provider.GetService<ToolOrchestrator>());
        Assert.NotNull(provider.GetService<HttpToolExecutor>());
        Assert.NotNull(provider.GetService<MockToolExecutor>());
        Assert.NotNull(provider.GetService<ToolFitnessTester>());
        Assert.NotNull(provider.GetService<ToolFitnessRagStore>());
    }

    #endregion

    #region SignalR Registration Tests

    [Fact]
    public void AddLLMockSignalR_RegistersSignalRServices()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MockLlmApi:BaseUrl"] = "http://localhost:11434/v1/"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        // Act - First add core, then SignalR
        services.AddLLMockRest(configuration);
        services.AddLLMockSignalR(configuration);

        // Assert
        var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<DynamicHubContextManager>());
    }

    #endregion

    #region Context Store Tests

    [Fact]
    public void AddLLMockApi_RegistersContextStoreWithExpiration()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MockLlmApi:BaseUrl"] = "http://localhost:11434/v1/",
                ["MockLlmApi:ContextExpirationMinutes"] = "30"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddLLMockApi(configuration);

        // Assert
        var provider = services.BuildServiceProvider();
        var contextStore = provider.GetRequiredService<IContextStore>();
        Assert.NotNull(contextStore);
        Assert.IsType<MemoryCacheContextStore>(contextStore);
    }

    #endregion

    #region RestApiRegistry Tests

    [Fact]
    public void AddLLMockApi_RegistersRestApiRegistry()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MockLlmApi:BaseUrl"] = "http://localhost:11434/v1/"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddLLMockApi(configuration);

        // Assert
        var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<RestApiRegistry>());
    }

    #endregion

    #region Minimal Configuration Tests

    [Fact]
    public void AddLLMockApi_MinimalConfiguration_RegistersAllRequiredServices()
    {
        // Arrange - Minimal configuration (just BaseUrl and ModelName)
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MockLlmApi:BaseUrl"] = "http://localhost:11434/v1/",
                ["MockLlmApi:ModelName"] = "llama3"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddLLMockApi(configuration);

        // Assert - All services should be resolvable
        var provider = services.BuildServiceProvider();

        // Core services
        Assert.NotNull(provider.GetService<LLMockApiService>());
        Assert.NotNull(provider.GetService<ShapeExtractor>());
        Assert.NotNull(provider.GetService<ContextExtractor>());
        Assert.NotNull(provider.GetService<PromptBuilder>());
        Assert.NotNull(provider.GetService<LlmClient>());
        Assert.NotNull(provider.GetService<LlmBackendSelector>());
        Assert.NotNull(provider.GetService<LlmProviderFactory>());
        Assert.NotNull(provider.GetService<CacheManager>());
        Assert.NotNull(provider.GetService<DelayHelper>());
        Assert.NotNull(provider.GetService<RateLimitService>());
        Assert.NotNull(provider.GetService<BatchingCoordinator>());
        Assert.NotNull(provider.GetService<ChunkingCoordinator>());
        Assert.NotNull(provider.GetService<IContextStore>());
        Assert.NotNull(provider.GetService<OpenApiContextManager>()); // Critical dependency for handlers

        // Request handlers
        Assert.NotNull(provider.GetService<RegularRequestHandler>());
        Assert.NotNull(provider.GetService<StreamingRequestHandler>());
        Assert.NotNull(provider.GetService<GraphQLRequestHandler>());
        Assert.NotNull(provider.GetService<GrpcRequestHandler>());
    }

    [Fact]
    public void AddLLMockApi_EmptyConfiguration_StillRegistersServices()
    {
        // Arrange - Empty configuration (uses defaults)
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddLLMockApi(configuration);

        // Assert - Services should still be registered with defaults
        var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<LLMockApiService>());
        Assert.NotNull(provider.GetService<RegularRequestHandler>());
        Assert.NotNull(provider.GetService<OpenApiContextManager>());
    }

    [Fact]
    public void AddLLMockApi_InlineConfiguration_RegistersAllServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act - Using inline configuration
        services.AddLLMockApi(options =>
        {
            options.BaseUrl = "http://localhost:11434/v1/";
            options.ModelName = "llama3";
        });

        // Assert
        var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<LLMockApiService>());
        Assert.NotNull(provider.GetService<RegularRequestHandler>());
        Assert.NotNull(provider.GetService<StreamingRequestHandler>());
        Assert.NotNull(provider.GetService<GraphQLRequestHandler>());
        Assert.NotNull(provider.GetService<OpenApiContextManager>());
    }

    #endregion

    #region Modular Registration Tests

    [Fact]
    public void AddLLMockRest_OnlyRegistersRestHandler()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MockLlmApi:BaseUrl"] = "http://localhost:11434/v1/"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddLLMockRest(configuration);

        // Assert
        var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<RegularRequestHandler>());
        Assert.NotNull(provider.GetService<OpenApiContextManager>()); // Should be available
        Assert.Null(provider.GetService<StreamingRequestHandler>()); // Not registered
        Assert.Null(provider.GetService<GraphQLRequestHandler>()); // Not registered
    }

    [Fact]
    public void AddLLMockStreaming_OnlyRegistersStreamingHandler()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MockLlmApi:BaseUrl"] = "http://localhost:11434/v1/"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddLLMockStreaming(configuration);

        // Assert
        var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<StreamingRequestHandler>());
        Assert.NotNull(provider.GetService<OpenApiContextManager>()); // Should be available
        Assert.Null(provider.GetService<RegularRequestHandler>()); // Not registered
        Assert.Null(provider.GetService<GraphQLRequestHandler>()); // Not registered
    }

    [Fact]
    public void AddLLMockGraphQL_OnlyRegistersGraphQLHandler()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MockLlmApi:BaseUrl"] = "http://localhost:11434/v1/"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddLLMockGraphQL(configuration);

        // Assert
        var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<GraphQLRequestHandler>());
        Assert.NotNull(provider.GetService<OpenApiContextManager>()); // Should be available
        Assert.Null(provider.GetService<RegularRequestHandler>()); // Not registered
        Assert.Null(provider.GetService<StreamingRequestHandler>()); // Not registered
    }

    [Fact]
    public void AddLLMockOpenApi_RegistersOpenApiServices()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MockLlmApi:BaseUrl"] = "http://localhost:11434/v1/"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddLLMockOpenApi(configuration);

        // Assert
        var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<OpenApiSpecLoader>());
        Assert.NotNull(provider.GetService<OpenApiSchemaConverter>());
        Assert.NotNull(provider.GetService<OpenApiRequestHandler>());
        Assert.NotNull(provider.GetService<DynamicOpenApiManager>());
        Assert.NotNull(provider.GetService<OpenApiContextManager>());
    }

    #endregion

    #region Multiple Modular Registration Tests

    [Fact]
    public void AddLLMockRest_AndAddLLMockStreaming_BothWork()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MockLlmApi:BaseUrl"] = "http://localhost:11434/v1/"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        // Act - Register both modularly
        services.AddLLMockRest(configuration);
        services.AddLLMockStreaming(configuration);

        // Assert
        var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<RegularRequestHandler>());
        Assert.NotNull(provider.GetService<StreamingRequestHandler>());
        Assert.NotNull(provider.GetService<OpenApiContextManager>());
    }

    [Fact]
    public void CombiningModularAndFullApi_DoesNotDuplicateServices()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MockLlmApi:BaseUrl"] = "http://localhost:11434/v1/"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        // Act - Mix modular and full registration
        services.AddLLMockRest(configuration);
        services.AddLLMockApi(configuration); // Should not duplicate

        // Assert - Should only have one of each service
        var provider = services.BuildServiceProvider();
        var regularHandlers = services.Where(s => s.ServiceType == typeof(RegularRequestHandler)).ToList();
        Assert.Single(regularHandlers);
    }

    #endregion

    #region Handler Dependency Resolution Tests

    [Fact]
    public void RegularRequestHandler_AllDependenciesResolve()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MockLlmApi:BaseUrl"] = "http://localhost:11434/v1/"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLLMockApi(configuration);

        var provider = services.BuildServiceProvider();

        // Act & Assert - Should not throw
        using var scope = provider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<RegularRequestHandler>();
        Assert.NotNull(handler);
    }

    [Fact]
    public void StreamingRequestHandler_AllDependenciesResolve()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MockLlmApi:BaseUrl"] = "http://localhost:11434/v1/"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLLMockApi(configuration);

        var provider = services.BuildServiceProvider();

        // Act & Assert
        using var scope = provider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<StreamingRequestHandler>();
        Assert.NotNull(handler);
    }

    [Fact]
    public void GraphQLRequestHandler_AllDependenciesResolve()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MockLlmApi:BaseUrl"] = "http://localhost:11434/v1/"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLLMockApi(configuration);

        var provider = services.BuildServiceProvider();

        // Act & Assert
        using var scope = provider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<GraphQLRequestHandler>();
        Assert.NotNull(handler);
    }

    [Fact]
    public void GrpcRequestHandler_AllDependenciesResolve()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MockLlmApi:BaseUrl"] = "http://localhost:11434/v1/"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLLMockApi(configuration);

        var provider = services.BuildServiceProvider();

        // Act & Assert
        using var scope = provider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<GrpcRequestHandler>();
        Assert.NotNull(handler);
    }

    #endregion

    #region TestHost Integration Tests

    [Fact]
    public async Task AddLLMockApi_WithTestHost_ServicesResolveCorrectly()
    {
        // Arrange & Act - Test that services resolve in a hosted environment
        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureAppConfiguration(config =>
                    {
                        config.AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            ["MockLlmApi:BaseUrl"] = "http://localhost:11434/v1/",
                            ["MockLlmApi:ModelName"] = "llama3"
                        });
                    })
                    .ConfigureServices((context, services) =>
                    {
                        services.AddRouting();
                        services.AddLLMockApi(context.Configuration);
                    })
                    .Configure(app =>
                    {
                        // Note: MapLLMockApi requires IEndpointRouteBuilder which WebApplication provides
                        // For TestHost, we just verify services resolve correctly
                        app.UseRouting();
                    });
            });

        using var host = await hostBuilder.StartAsync();

        // Assert - Host started and services resolve
        Assert.NotNull(host);
        var testServer = host.GetTestServer();
        Assert.NotNull(testServer);

        // Verify services resolve in hosted scope
        using var scope = host.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<LLMockApiService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<RegularRequestHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<StreamingRequestHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<GraphQLRequestHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<OpenApiContextManager>());
    }

    [Fact]
    public async Task AddLLMockRest_WithTestHost_ServicesResolveCorrectly()
    {
        // Arrange
        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureAppConfiguration(config =>
                    {
                        config.AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            ["MockLlmApi:BaseUrl"] = "http://localhost:11434/v1/"
                        });
                    })
                    .ConfigureServices((context, services) =>
                    {
                        services.AddRouting();
                        services.AddLLMockRest(context.Configuration);
                    })
                    .Configure(app => { app.UseRouting(); });
            });

        // Act
        using var host = await hostBuilder.StartAsync();

        // Assert
        Assert.NotNull(host);

        // Verify REST handler and its dependencies resolve
        using var scope = host.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<RegularRequestHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<OpenApiContextManager>());
    }

    #endregion

    #region Backend Configuration Tests

    [Fact]
    public void AddLLMockApi_WithMultipleBackends_RegistersCorrectly()
    {
        // Arrange - Multiple backend configuration
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MockLlmApi:Backends:0:Name"] = "primary",
                ["MockLlmApi:Backends:0:Provider"] = "ollama",
                ["MockLlmApi:Backends:0:BaseUrl"] = "http://localhost:11434/v1/",
                ["MockLlmApi:Backends:0:ModelName"] = "llama3",
                ["MockLlmApi:Backends:0:Enabled"] = "true",
                ["MockLlmApi:Backends:1:Name"] = "secondary",
                ["MockLlmApi:Backends:1:Provider"] = "openai",
                ["MockLlmApi:Backends:1:BaseUrl"] = "https://api.openai.com/v1/",
                ["MockLlmApi:Backends:1:ModelName"] = "gpt-4",
                ["MockLlmApi:Backends:1:ApiKey"] = "test-key",
                ["MockLlmApi:Backends:1:Enabled"] = "true"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddLLMockApi(configuration);

        // Assert
        var provider = services.BuildServiceProvider();
        var backendSelector = provider.GetRequiredService<LlmBackendSelector>();
        Assert.NotNull(backendSelector);
    }

    [Fact]
    public void AddLLMockApi_WithLegacyConfig_BackwardCompatible()
    {
        // Arrange - Legacy single-backend config (pre-v1.8.0 style)
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MockLlmApi:BaseUrl"] = "http://localhost:11434/v1/",
                ["MockLlmApi:ModelName"] = "llama3",
                ["MockLlmApi:Temperature"] = "1.2",
                ["MockLlmApi:TimeoutSeconds"] = "30"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddLLMockApi(configuration);

        // Assert - Should still work with legacy config
        var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<LlmClient>());
        Assert.NotNull(provider.GetService<LlmBackendSelector>());
    }

    #endregion
}