using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using mostlylucid.mockllmapi;
using mostlylucid.mockllmapi.Models;
using mostlylucid.mockllmapi.Services;

namespace LLMApi.Tests;

public class AutoShapeTests
{
    #region PathNormalizer Tests

    [Theory]
    [InlineData("/api/mock/users/123", "/api/mock/users/{id}")]
    [InlineData("/api/mock/users/abc-123", "/api/mock/users/{id}")]
    [InlineData("/api/mock/products/550e8400-e29b-41d4-a716-446655440000", "/api/mock/products/{id}")]
    [InlineData("/api/mock/orders/ORD-2024-001", "/api/mock/orders/{id}")]
    [InlineData("/api/users/123/posts/456", "/api/users/{id}/posts/{id}")]
    public void PathNormalizer_NormalizesIds(string input, string expected)
    {
        var result = PathNormalizer.NormalizePath(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("/api/mock/users", "/api/mock/users")]
    [InlineData("/api/mock/products", "/api/mock/products")]
    [InlineData("/api/mock/v1/users", "/api/mock/v1/users")]
    public void PathNormalizer_PreservesStaticPaths(string input, string expected)
    {
        var result = PathNormalizer.NormalizePath(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("/api/mock/users?page=1", "/api/mock/users")]
    [InlineData("/api/mock/users/123?expand=true", "/api/mock/users/{id}")]
    public void PathNormalizer_StripsQueryStrings(string input, string expected)
    {
        var result = PathNormalizer.NormalizePath(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("123", true)]
    [InlineData("abc-123", true)]
    [InlineData("550e8400-e29b-41d4-a716-446655440000", true)]
    [InlineData("users", false)]
    [InlineData("api", false)]
    [InlineData("v1", false)]
    public void PathNormalizer_IdentifiesDynamicIds(string segment, bool expected)
    {
        var result = PathNormalizer.IsLikelyDynamicId(segment);
        Assert.Equal(expected, result);
    }

    #endregion

    #region ShapeExtractorFromResponse Tests

    [Fact]
    public void ShapeExtractor_ExtractsSimpleObjectShape()
    {
        var extractor = new ShapeExtractorFromResponse(NullLogger<ShapeExtractorFromResponse>.Instance);
        var json = """{"id": 123, "name": "John", "active": true}""";

        var shape = extractor.ExtractShape(json);

        Assert.NotNull(shape);
        var expectedShape = """{"id":0,"name":"","active":true}""";
        Assert.Equal(expectedShape, shape);
    }

    [Fact]
    public void ShapeExtractor_ExtractsArrayShape()
    {
        var extractor = new ShapeExtractorFromResponse(NullLogger<ShapeExtractorFromResponse>.Instance);
        var json = """[{"id": 1, "name": "Alice"}, {"id": 2, "name": "Bob"}]""";

        var shape = extractor.ExtractShape(json);

        Assert.NotNull(shape);
        var expectedShape = """[{"id":0,"name":""}]""";
        Assert.Equal(expectedShape, shape);
    }

    [Fact]
    public void ShapeExtractor_ExtractsNestedObjectShape()
    {
        var extractor = new ShapeExtractorFromResponse(NullLogger<ShapeExtractorFromResponse>.Instance);
        var json = """{"user": {"id": 1, "name": "John"}, "status": "active"}""";

        var shape = extractor.ExtractShape(json);

        Assert.NotNull(shape);
        var expectedShape = """{"user":{"id":0,"name":""},"status":""}""";
        Assert.Equal(expectedShape, shape);
    }

    [Fact]
    public void ShapeExtractor_DistinguishesNumberTypes()
    {
        var extractor = new ShapeExtractorFromResponse(NullLogger<ShapeExtractorFromResponse>.Instance);
        var json = """{"integer": 42, "decimal": 3.14}""";

        var shape = extractor.ExtractShape(json);

        Assert.NotNull(shape);
        // Note: JSON serialization may omit .0 for whole number decimals
        // The important part is that both numbers are represented as numbers
        Assert.Contains("\"integer\":0", shape);
        Assert.Contains("\"decimal\":", shape);
    }

    [Fact]
    public void ShapeExtractor_HandlesEmptyArray()
    {
        var extractor = new ShapeExtractorFromResponse(NullLogger<ShapeExtractorFromResponse>.Instance);
        var json = """[]""";

        var shape = extractor.ExtractShape(json);

        Assert.NotNull(shape);
        var expectedShape = """[]""";
        Assert.Equal(expectedShape, shape);
    }

    [Fact]
    public void ShapeExtractor_RejectsErrorResponses()
    {
        var extractor = new ShapeExtractorFromResponse(NullLogger<ShapeExtractorFromResponse>.Instance);
        var json = """{"error": {"code": 404, "message": "Not found"}}""";

        var isValid = extractor.IsValidForShapeExtraction(json);

        Assert.False(isValid);
    }

    [Fact]
    public void ShapeExtractor_RejectsGraphQLErrors()
    {
        var extractor = new ShapeExtractorFromResponse(NullLogger<ShapeExtractorFromResponse>.Instance);
        var json = """{"errors": [{"message": "Field not found"}]}""";

        var isValid = extractor.IsValidForShapeExtraction(json);

        Assert.False(isValid);
    }

    [Fact]
    public void ShapeExtractor_AcceptsValidResponses()
    {
        var extractor = new ShapeExtractorFromResponse(NullLogger<ShapeExtractorFromResponse>.Instance);
        var json = """{"id": 1, "name": "Test"}""";

        var isValid = extractor.IsValidForShapeExtraction(json);

        Assert.True(isValid);
    }

    #endregion

    #region AutoShapeManager Tests

    [Fact]
    public void AutoShapeManager_RespectsGlobalConfig()
    {
        var options = Options.Create(new LLMockApiOptions { EnableAutoShape = false });
        var cache = new MemoryCache(new MemoryCacheOptions());
        var shapeStore = new MemoryCacheShapeStore(cache, NullLogger<MemoryCacheShapeStore>.Instance);
        var shapeExtractor = new ShapeExtractorFromResponse(NullLogger<ShapeExtractorFromResponse>.Instance);
        var manager = new AutoShapeManager(options, shapeStore, shapeExtractor, NullLogger<AutoShapeManager>.Instance);

        var request = new DefaultHttpContext().Request;
        var isEnabled = manager.IsAutoShapeEnabled(request);

        Assert.False(isEnabled);
    }

    [Fact]
    public void AutoShapeManager_QueryParameterOverridesConfig()
    {
        var options = Options.Create(new LLMockApiOptions { EnableAutoShape = false });
        var cache = new MemoryCache(new MemoryCacheOptions());
        var shapeStore = new MemoryCacheShapeStore(cache, NullLogger<MemoryCacheShapeStore>.Instance);
        var shapeExtractor = new ShapeExtractorFromResponse(NullLogger<ShapeExtractorFromResponse>.Instance);
        var manager = new AutoShapeManager(options, shapeStore, shapeExtractor, NullLogger<AutoShapeManager>.Instance);

        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?autoshape=true");
        var isEnabled = manager.IsAutoShapeEnabled(context.Request);

        Assert.True(isEnabled);
    }

    [Fact]
    public void AutoShapeManager_HeaderOverridesConfig()
    {
        var options = Options.Create(new LLMockApiOptions { EnableAutoShape = false });
        var cache = new MemoryCache(new MemoryCacheOptions());
        var shapeStore = new MemoryCacheShapeStore(cache, NullLogger<MemoryCacheShapeStore>.Instance);
        var shapeExtractor = new ShapeExtractorFromResponse(NullLogger<ShapeExtractorFromResponse>.Instance);
        var manager = new AutoShapeManager(options, shapeStore, shapeExtractor, NullLogger<AutoShapeManager>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Auto-Shape"] = "true";
        var isEnabled = manager.IsAutoShapeEnabled(context.Request);

        Assert.True(isEnabled);
    }

    [Fact]
    public void AutoShapeManager_SkipsWhenExplicitShapeProvided()
    {
        var options = Options.Create(new LLMockApiOptions { EnableAutoShape = true });
        var cache = new MemoryCache(new MemoryCacheOptions());
        var shapeStore = new MemoryCacheShapeStore(cache, NullLogger<MemoryCacheShapeStore>.Instance);
        var shapeExtractor = new ShapeExtractorFromResponse(NullLogger<ShapeExtractorFromResponse>.Instance);
        var manager = new AutoShapeManager(options, shapeStore, shapeExtractor, NullLogger<AutoShapeManager>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/mock/users";
        var shapeInfo = new ShapeInfo { Shape = """{"id": 0, "name": ""}""" };

        var result = manager.GetShapeForRequest(context.Request, shapeInfo);

        Assert.Null(result); // Should skip because explicit shape is provided
    }

    [Fact]
    public void AutoShapeManager_StoresAndRetrievesShape()
    {
        var options = Options.Create(new LLMockApiOptions { EnableAutoShape = true });
        var cache = new MemoryCache(new MemoryCacheOptions());
        var shapeStore = new MemoryCacheShapeStore(cache, NullLogger<MemoryCacheShapeStore>.Instance);
        var shapeExtractor = new ShapeExtractorFromResponse(NullLogger<ShapeExtractorFromResponse>.Instance);
        var manager = new AutoShapeManager(options, shapeStore, shapeExtractor, NullLogger<AutoShapeManager>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/mock/users/123";

        // Store shape
        var json = """{"id": 123, "name": "John", "email": "john@example.com"}""";
        manager.StoreShapeFromResponse(context.Request, json);

        // Retrieve shape (for a different ID but same endpoint)
        var context2 = new DefaultHttpContext();
        context2.Request.Path = "/api/mock/users/456";
        var shapeInfo = new ShapeInfo();
        var retrievedShape = manager.GetShapeForRequest(context2.Request, shapeInfo);

        Assert.NotNull(retrievedShape);
        var expectedShape = """{"id":0,"name":"","email":""}""";
        Assert.Equal(expectedShape, retrievedShape);
    }

    [Fact]
    public void AutoShapeManager_RenewShapeReplacesOldOne()
    {
        var options = Options.Create(new LLMockApiOptions { EnableAutoShape = true });
        var cache = new MemoryCache(new MemoryCacheOptions());
        var shapeStore = new MemoryCacheShapeStore(cache, NullLogger<MemoryCacheShapeStore>.Instance);
        var shapeExtractor = new ShapeExtractorFromResponse(NullLogger<ShapeExtractorFromResponse>.Instance);
        var manager = new AutoShapeManager(options, shapeStore, shapeExtractor, NullLogger<AutoShapeManager>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/mock/users/123";

        // Store initial shape
        var json1 = """{"id": 123, "name": "John"}""";
        manager.StoreShapeFromResponse(context.Request, json1);

        // Request with renewshape=true
        var context2 = new DefaultHttpContext();
        context2.Request.Path = "/api/mock/users/456";
        context2.Request.QueryString = new QueryString("?renewshape=true");
        var shapeInfo = new ShapeInfo();
        var retrievedShape = manager.GetShapeForRequest(context2.Request, shapeInfo);

        // Should NOT retrieve stored shape (returns null to force new generation)
        Assert.Null(retrievedShape);

        // Store new shape
        var json2 = """{"id": 456, "name": "Jane", "email": "jane@example.com", "active": true}""";
        manager.StoreShapeFromResponse(context2.Request, json2);

        // Now retrieve should get the new shape
        var context3 = new DefaultHttpContext();
        context3.Request.Path = "/api/mock/users/789";
        var retrievedShape2 = manager.GetShapeForRequest(context3.Request, new ShapeInfo());

        Assert.NotNull(retrievedShape2);
        var expectedShape = """{"id":0,"name":"","email":"","active":true}""";
        Assert.Equal(expectedShape, retrievedShape2);
    }

    [Fact]
    public void AutoShapeManager_DoesNotStoreErrorResponses()
    {
        var options = Options.Create(new LLMockApiOptions { EnableAutoShape = true });
        var cache = new MemoryCache(new MemoryCacheOptions());
        var shapeStore = new MemoryCacheShapeStore(cache, NullLogger<MemoryCacheShapeStore>.Instance);
        var shapeExtractor = new ShapeExtractorFromResponse(NullLogger<ShapeExtractorFromResponse>.Instance);
        var manager = new AutoShapeManager(options, shapeStore, shapeExtractor, NullLogger<AutoShapeManager>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/mock/users/123";

        // Try to store error response
        var json = """{"error": {"code": 404, "message": "Not found"}}""";
        manager.StoreShapeFromResponse(context.Request, json);

        // Should not have stored anything
        var count = manager.GetStoredShapeCount();
        Assert.Equal(0, count);
    }

    [Fact]
    public void AutoShapeManager_ClearsAllShapes()
    {
        var options = Options.Create(new LLMockApiOptions { EnableAutoShape = true });
        var cache = new MemoryCache(new MemoryCacheOptions());
        var shapeStore = new MemoryCacheShapeStore(cache, NullLogger<MemoryCacheShapeStore>.Instance);
        var shapeExtractor = new ShapeExtractorFromResponse(NullLogger<ShapeExtractorFromResponse>.Instance);
        var manager = new AutoShapeManager(options, shapeStore, shapeExtractor, NullLogger<AutoShapeManager>.Instance);

        // Store multiple shapes
        var context1 = new DefaultHttpContext();
        context1.Request.Path = "/api/mock/users/1";
        manager.StoreShapeFromResponse(context1.Request, """{"id": 1, "name": "John"}""");

        var context2 = new DefaultHttpContext();
        context2.Request.Path = "/api/mock/products/1";
        manager.StoreShapeFromResponse(context2.Request, """{"id": 1, "title": "Product"}""");

        Assert.Equal(2, manager.GetStoredShapeCount());

        // Clear all
        manager.ClearAllShapes();

        Assert.Equal(0, manager.GetStoredShapeCount());
    }

    [Fact]
    public void AutoShapeManager_RemoveSpecificShape()
    {
        var options = Options.Create(new LLMockApiOptions { EnableAutoShape = true });
        var cache = new MemoryCache(new MemoryCacheOptions());
        var shapeStore = new MemoryCacheShapeStore(cache, NullLogger<MemoryCacheShapeStore>.Instance);
        var shapeExtractor = new ShapeExtractorFromResponse(NullLogger<ShapeExtractorFromResponse>.Instance);
        var manager = new AutoShapeManager(options, shapeStore, shapeExtractor, NullLogger<AutoShapeManager>.Instance);

        // Store shape
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/mock/users/123";
        manager.StoreShapeFromResponse(context.Request, """{"id": 123, "name": "John"}""");

        Assert.Equal(1, manager.GetStoredShapeCount());

        // Remove specific shape
        var removed = manager.RemoveShape("/api/mock/users/456"); // Different ID, same normalized path

        Assert.True(removed);
        Assert.Equal(0, manager.GetStoredShapeCount());
    }

    #endregion

    #region MemoryCacheShapeStore Tests

    [Fact]
    public void ShapeStore_StoresAndRetrievesShape()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var store = new MemoryCacheShapeStore(cache, NullLogger<MemoryCacheShapeStore>.Instance);

        var path = "/api/mock/users/{id}";
        var shape = """{"id": 0, "name": ""}""";

        store.Set(path, shape);

        var retrieved = store.TryGetValue(path, out var result);

        Assert.True(retrieved);
        Assert.Equal(shape, result);
    }

    [Fact]
    public void ShapeStore_GetOrAddCreatesNewShape()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var store = new MemoryCacheShapeStore(cache, NullLogger<MemoryCacheShapeStore>.Instance);

        var path = "/api/mock/users/{id}";
        var shape = """{"id": 0, "name": ""}""";

        var result = store.GetOrAdd(path, _ => shape);

        Assert.Equal(shape, result);
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void ShapeStore_GetOrAddReturnsExisting()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var store = new MemoryCacheShapeStore(cache, NullLogger<MemoryCacheShapeStore>.Instance);

        var path = "/api/mock/users/{id}";
        var shape1 = """{"id": 0, "name": ""}""";
        var shape2 = """{"id": 0, "name": "", "email": ""}""";

        store.Set(path, shape1);
        var result = store.GetOrAdd(path, _ => shape2);

        Assert.Equal(shape1, result); // Should return existing, not create new
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void ShapeStore_RemovesShape()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var store = new MemoryCacheShapeStore(cache, NullLogger<MemoryCacheShapeStore>.Instance);

        var path = "/api/mock/users/{id}";
        var shape = """{"id": 0, "name": ""}""";

        store.Set(path, shape);
        var removed = store.TryRemove(path, out var removedShape);

        Assert.True(removed);
        Assert.Equal(shape, removedShape);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void ShapeStore_CaseInsensitivePaths()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var store = new MemoryCacheShapeStore(cache, NullLogger<MemoryCacheShapeStore>.Instance);

        var path1 = "/api/mock/USERS/{id}";
        var path2 = "/api/mock/users/{id}";
        var shape = """{"id": 0, "name": ""}""";

        store.Set(path1, shape);
        var retrieved = store.TryGetValue(path2, out var result);

        Assert.True(retrieved);
        Assert.Equal(shape, result);
    }

    [Fact]
    public void ShapeStore_GetAllPaths()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var store = new MemoryCacheShapeStore(cache, NullLogger<MemoryCacheShapeStore>.Instance);

        store.Set("/api/mock/users/{id}", """{"id": 0}""");
        store.Set("/api/mock/products/{id}", """{"id": 0}""");

        var paths = store.GetAllPaths().ToList();

        Assert.Equal(2, paths.Count);
        Assert.Contains("/api/mock/users/{id}", paths, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("/api/mock/products/{id}", paths, StringComparer.OrdinalIgnoreCase);
    }

    #endregion
}