using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using mostlylucid.mockllmapi;
using mostlylucid.mockllmapi.Services;
using Xunit;

namespace LLMApi.Tests;

public class OpenApiManagementTests
{
    private DynamicOpenApiManager CreateManager()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient();
        services.AddScoped<OpenApiSpecLoader>();
        services.AddSingleton<DynamicOpenApiManager>();

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<DynamicOpenApiManager>();
    }

    [Fact]
    public async Task LoadSpec_FromValidUrl_ReturnsSuccess()
    {
        // Arrange
        var manager = CreateManager();
        var specUrl = "https://petstore3.swagger.io/api/v3/openapi.json";

        // Act
        var result = await manager.LoadSpecAsync("test-petstore", specUrl, "/test");

        // Assert
        Assert.True(result.Success);
        Assert.Equal("test-petstore", result.SpecName);
        Assert.True(result.EndpointCount > 0);
        Assert.NotNull(result.Endpoints);
        Assert.NotNull(result.Info);
    }

    [Fact]
    public async Task LoadSpec_InvalidUrl_ReturnsFailure()
    {
        // Arrange
        var manager = CreateManager();
        var invalidUrl = "https://invalid-url-that-does-not-exist-12345.com/openapi.json";

        // Act
        var result = await manager.LoadSpecAsync("invalid-spec", invalidUrl);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("invalid-spec", result.SpecName);
    }

    [Fact]
    public async Task GetAllSpecs_AfterLoadingMultiple_ReturnsAll()
    {
        // Arrange
        var manager = CreateManager();
        var specUrl = "https://petstore3.swagger.io/api/v3/openapi.json";

        // Act - Load two specs
        await manager.LoadSpecAsync("spec1", specUrl, "/v1");
        await manager.LoadSpecAsync("spec2", specUrl, "/v2");

        var allSpecs = manager.GetAllSpecs();

        // Assert
        Assert.Equal(2, allSpecs.Count);
        Assert.Contains(allSpecs, s => s.Name == "spec1" && s.BasePath == "/v1");
        Assert.Contains(allSpecs, s => s.Name == "spec2" && s.BasePath == "/v2");
    }

    [Fact]
    public async Task GetSpec_ExistingSpec_ReturnsSpec()
    {
        // Arrange
        var manager = CreateManager();
        var specUrl = "https://petstore3.swagger.io/api/v3/openapi.json";
        await manager.LoadSpecAsync("test-spec", specUrl);

        // Act
        var spec = manager.GetSpec("test-spec");

        // Assert
        Assert.NotNull(spec);
        Assert.Equal("test-spec", spec.Name);
        Assert.NotEmpty(spec.Endpoints);
    }

    [Fact]
    public void GetSpec_NonExistentSpec_ReturnsNull()
    {
        // Arrange
        var manager = CreateManager();

        // Act
        var spec = manager.GetSpec("does-not-exist");

        // Assert
        Assert.Null(spec);
    }

    [Fact]
    public async Task RemoveSpec_ExistingSpec_ReturnsTrue()
    {
        // Arrange
        var manager = CreateManager();
        var specUrl = "https://petstore3.swagger.io/api/v3/openapi.json";
        await manager.LoadSpecAsync("to-delete", specUrl);

        // Act
        var removed = manager.RemoveSpec("to-delete");

        // Assert
        Assert.True(removed);
        Assert.Null(manager.GetSpec("to-delete"));
    }

    [Fact]
    public void RemoveSpec_NonExistentSpec_ReturnsFalse()
    {
        // Arrange
        var manager = CreateManager();

        // Act
        var removed = manager.RemoveSpec("does-not-exist");

        // Assert
        Assert.False(removed);
    }

    [Fact]
    public async Task ReloadSpec_ExistingSpec_ReturnsSuccess()
    {
        // Arrange
        var manager = CreateManager();
        var specUrl = "https://petstore3.swagger.io/api/v3/openapi.json";
        await manager.LoadSpecAsync("to-reload", specUrl);

        // Act
        var result = await manager.ReloadSpecAsync("to-reload");

        // Assert
        Assert.True(result.Success);
        Assert.Equal("to-reload", result.SpecName);
        Assert.True(result.EndpointCount > 0);
    }

    [Fact]
    public async Task ReloadSpec_NonExistentSpec_ReturnsFailure()
    {
        // Arrange
        var manager = CreateManager();

        // Act
        var result = await manager.ReloadSpecAsync("does-not-exist");

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task LoadSpec_ParsesEndpointsCorrectly()
    {
        // Arrange
        var manager = CreateManager();
        var specUrl = "https://petstore3.swagger.io/api/v3/openapi.json";

        // Act
        var result = await manager.LoadSpecAsync("endpoint-test", specUrl, "/api");

        // Assert
        Assert.True(result.Success);
        Assert.NotEmpty(result.Endpoints);

        // Check that endpoints have expected properties
        var endpoint = result.Endpoints.First();
        Assert.NotEmpty(endpoint.Path);
        Assert.NotEmpty(endpoint.Method);
        Assert.StartsWith("/api", endpoint.Path);
    }

    [Fact]
    public async Task LoadSpec_ExtractsSpecInfo()
    {
        // Arrange
        var manager = CreateManager();
        var specUrl = "https://petstore3.swagger.io/api/v3/openapi.json";

        // Act
        var result = await manager.LoadSpecAsync("info-test", specUrl);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Info);
        Assert.NotNull(result.Info.Title);
        Assert.NotNull(result.Info.Version);
    }

    [Fact]
    public async Task LoadSpec_DuplicateName_OverwritesExisting()
    {
        // Arrange
        var manager = CreateManager();
        var specUrl = "https://petstore3.swagger.io/api/v3/openapi.json";

        // Act - Load same name twice
        var result1 = await manager.LoadSpecAsync("duplicate", specUrl, "/v1");
        var result2 = await manager.LoadSpecAsync("duplicate", specUrl, "/v2");

        var allSpecs = manager.GetAllSpecs();

        // Assert
        Assert.Single(allSpecs); // Only one spec should exist
        var spec = manager.GetSpec("duplicate");
        Assert.NotNull(spec);
        Assert.Equal("/v2", spec.BasePath); // Should have latest base path
    }

    [Fact]
    public async Task LoadSpec_CustomBasePath_OverridesSpecDefault()
    {
        // Arrange
        var manager = CreateManager();
        var specUrl = "https://petstore3.swagger.io/api/v3/openapi.json";

        // Act
        var result = await manager.LoadSpecAsync("custom-path", specUrl, "/custom");

        // Assert
        Assert.True(result.Success);
        var spec = manager.GetSpec("custom-path");
        Assert.NotNull(spec);
        Assert.Equal("/custom", spec.BasePath);

        // All endpoints should start with custom base path
        Assert.All(spec.Endpoints, ep => Assert.StartsWith("/custom", ep.Path));
    }

    [Fact]
    public async Task LoadSpec_NoBasePath_UsesSpecDefault()
    {
        // Arrange
        var manager = CreateManager();
        var specUrl = "https://petstore3.swagger.io/api/v3/openapi.json";

        // Act
        var result = await manager.LoadSpecAsync("default-path", specUrl, basePath: null);

        // Assert
        Assert.True(result.Success);
        var spec = manager.GetSpec("default-path");
        Assert.NotNull(spec);
        Assert.NotEmpty(spec.BasePath);
        // Should use spec's server URL or default to /api
    }

    [Fact]
    public async Task GetAllSpecs_ReturnsCorrectEndpointCounts()
    {
        // Arrange
        var manager = CreateManager();
        var specUrl = "https://petstore3.swagger.io/api/v3/openapi.json";

        // Act
        var loadResult = await manager.LoadSpecAsync("count-test", specUrl);
        var allSpecs = manager.GetAllSpecs();

        // Assert
        var summary = allSpecs.First(s => s.Name == "count-test");
        Assert.Equal(loadResult.EndpointCount, summary.EndpointCount);
    }

    [Fact]
    public async Task LoadSpec_TracksLoadTime()
    {
        // Arrange
        var manager = CreateManager();
        var specUrl = "https://petstore3.swagger.io/api/v3/openapi.json";
        var beforeLoad = DateTimeOffset.UtcNow;

        // Act
        await manager.LoadSpecAsync("time-test", specUrl);
        var afterLoad = DateTimeOffset.UtcNow;

        var spec = manager.GetSpec("time-test");

        // Assert
        Assert.NotNull(spec);
        Assert.True(spec.LoadedAt >= beforeLoad);
        Assert.True(spec.LoadedAt <= afterLoad);
    }

    [Fact]
    public async Task LoadSpec_PreservesSource()
    {
        // Arrange
        var manager = CreateManager();
        var specUrl = "https://petstore3.swagger.io/api/v3/openapi.json";

        // Act
        await manager.LoadSpecAsync("source-test", specUrl);
        var spec = manager.GetSpec("source-test");

        // Assert
        Assert.NotNull(spec);
        Assert.Equal(specUrl, spec.Source);
    }

    [Fact]
    public async Task LoadSpec_ExtractsOperationMetadata()
    {
        // Arrange
        var manager = CreateManager();
        var specUrl = "https://petstore3.swagger.io/api/v3/openapi.json";

        // Act
        var result = await manager.LoadSpecAsync("metadata-test", specUrl);

        // Assert
        Assert.True(result.Success);
        Assert.NotEmpty(result.Endpoints);

        // Check that at least some endpoints have metadata
        var endpointsWithSummary = result.Endpoints.Where(e => !string.IsNullOrEmpty(e.Summary)).ToList();
        Assert.NotEmpty(endpointsWithSummary);

        var endpointsWithTags = result.Endpoints.Where(e => e.Tags.Count > 0).ToList();
        Assert.NotEmpty(endpointsWithTags);
    }

    [Fact]
    public async Task RemoveSpec_ClearsEndpoints()
    {
        // Arrange
        var manager = CreateManager();
        var specUrl = "https://petstore3.swagger.io/api/v3/openapi.json";
        await manager.LoadSpecAsync("clear-test", specUrl);

        // Act
        manager.RemoveSpec("clear-test");
        var specs = manager.GetAllSpecs();

        // Assert
        Assert.DoesNotContain(specs, s => s.Name == "clear-test");
    }

    [Fact]
    public async Task MultipleSpecs_DoNotInterfere()
    {
        // Arrange
        var manager = CreateManager();
        var specUrl = "https://petstore3.swagger.io/api/v3/openapi.json";

        // Act - Load multiple specs with different configurations
        await manager.LoadSpecAsync("multi1", specUrl, "/api1");
        await manager.LoadSpecAsync("multi2", specUrl, "/api2");
        await manager.LoadSpecAsync("multi3", specUrl, "/api3");

        var spec1 = manager.GetSpec("multi1");
        var spec2 = manager.GetSpec("multi2");
        var spec3 = manager.GetSpec("multi3");

        // Assert - Each spec maintains its own configuration
        Assert.NotNull(spec1);
        Assert.NotNull(spec2);
        Assert.NotNull(spec3);

        Assert.Equal("/api1", spec1.BasePath);
        Assert.Equal("/api2", spec2.BasePath);
        Assert.Equal("/api3", spec3.BasePath);

        // All should have endpoints starting with their base path
        Assert.All(spec1.Endpoints, ep => Assert.StartsWith("/api1", ep.Path));
        Assert.All(spec2.Endpoints, ep => Assert.StartsWith("/api2", ep.Path));
        Assert.All(spec3.Endpoints, ep => Assert.StartsWith("/api3", ep.Path));
    }

    [Fact]
    public async Task ReloadSpec_UpdatesEndpoints()
    {
        // Arrange
        var manager = CreateManager();
        var specUrl = "https://petstore3.swagger.io/api/v3/openapi.json";
        await manager.LoadSpecAsync("reload-endpoints", specUrl);

        var originalSpec = manager.GetSpec("reload-endpoints");
        var originalEndpointCount = originalSpec!.Endpoints.Count;

        // Act
        await manager.ReloadSpecAsync("reload-endpoints");
        var reloadedSpec = manager.GetSpec("reload-endpoints");

        // Assert
        Assert.NotNull(reloadedSpec);
        // Endpoint count should be the same after reload (same spec)
        Assert.Equal(originalEndpointCount, reloadedSpec.Endpoints.Count);
        // LoadedAt should be updated
        Assert.True(reloadedSpec.LoadedAt > originalSpec.LoadedAt);
    }
}
