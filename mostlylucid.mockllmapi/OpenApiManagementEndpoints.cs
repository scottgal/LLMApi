using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using mostlylucid.mockllmapi.RequestHandlers;
using mostlylucid.mockllmapi.Services;

namespace mostlylucid.mockllmapi;

internal static class OpenApiManagementEndpoints
{
    internal static IResult HandleListSpecs(DynamicOpenApiManager manager)
    {
        var specs = manager.GetAllSpecs();
        return Results.Ok(new { specs, count = specs.Count });
    }

    internal static async Task<IResult> HandleLoadSpec(
        HttpContext ctx,
        DynamicOpenApiManager manager)
    {
        try
        {
            using var reader = new StreamReader(ctx.Request.Body);
            var json = await reader.ReadToEndAsync();
            var request = JsonSerializer.Deserialize<LoadSpecRequest>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (request == null || string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest(new { error = "Name is required" });
            }

            if (string.IsNullOrWhiteSpace(request.Source))
            {
                return Results.BadRequest(new { error = "Source is required" });
            }

            // Normalize empty strings to null for optional parameters
            var basePath = string.IsNullOrWhiteSpace(request.BasePath) ? null : request.BasePath;
            var contextName = string.IsNullOrWhiteSpace(request.ContextName) ? null : request.ContextName;

            var result = await manager.LoadSpecAsync(
                request.Name,
                request.Source,
                basePath,
                contextName,
                ctx.RequestAborted);

            if (result.Success)
            {
                return Results.Ok(result);
            }
            else
            {
                return Results.BadRequest(result);
            }
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message);
        }
    }

    internal static IResult HandleGetSpec(string specName, DynamicOpenApiManager manager)
    {
        var spec = manager.GetSpec(specName);
        if (spec == null)
        {
            return Results.NotFound(new { error = $"Spec '{specName}' not found" });
        }

        return Results.Ok(new
        {
            name = spec.Name,
            source = spec.Source,
            basePath = spec.BasePath,
            loadedAt = spec.LoadedAt,
            endpointCount = spec.Endpoints.Count,
            endpoints = spec.Endpoints,
            info = new
            {
                title = spec.Document.Info?.Title,
                version = spec.Document.Info?.Version,
                description = spec.Document.Info?.Description
            }
        });
    }

    internal static IResult HandleDeleteSpec(string specName, DynamicOpenApiManager manager)
    {
        var removed = manager.RemoveSpec(specName);
        if (removed)
        {
            return Results.Ok(new { message = $"Spec '{specName}' deleted successfully" });
        }
        else
        {
            return Results.NotFound(new { error = $"Spec '{specName}' not found" });
        }
    }

    internal static async Task<IResult> HandleReloadSpec(
        string specName,
        DynamicOpenApiManager manager,
        CancellationToken cancellationToken)
    {
        var result = await manager.ReloadSpecAsync(specName, cancellationToken);
        if (result.Success)
        {
            return Results.Ok(result);
        }
        else
        {
            return Results.BadRequest(result);
        }
    }

    internal static async Task<IResult> HandleTestEndpoint(
        HttpContext ctx,
        DynamicOpenApiManager manager,
        OpenApiRequestHandler handler)
    {
        try
        {
            using var reader = new StreamReader(ctx.Request.Body);
            var json = await reader.ReadToEndAsync();
            var request = JsonSerializer.Deserialize<TestEndpointRequest>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (request == null)
            {
                return Results.BadRequest(new { error = "Invalid request" });
            }

            // Find the spec and operation
            var spec = manager.GetSpec(request.SpecName);
            if (spec == null)
            {
                return Results.NotFound(new { error = $"Spec '{request.SpecName}' not found" });
            }

            // Strip the basePath from the request path to match against OpenAPI document paths
            var pathWithoutBase = request.Path;
            if (!string.IsNullOrEmpty(spec.BasePath) && request.Path.StartsWith(spec.BasePath))
            {
                pathWithoutBase = request.Path.Substring(spec.BasePath.Length);
                if (!pathWithoutBase.StartsWith("/"))
                {
                    pathWithoutBase = "/" + pathWithoutBase;
                }
            }

            // Find the matching operation
            var (operation, method) = handler.FindMatchingOperation(
                spec.Document,
                pathWithoutBase,
                request.Method);

            if (operation == null || method == null)
            {
                return Results.NotFound(new {
                    error = $"Operation not found: {request.Method} {pathWithoutBase}",
                    requestedPath = request.Path,
                    lookupPath = pathWithoutBase,
                    basePath = spec.BasePath
                });
            }

            // Generate mock response with spec's context (use path without basePath for schema lookup)
            var response = await handler.HandleRequestAsync(
                ctx,
                spec.Document,
                pathWithoutBase,
                method.Value,
                operation,
                spec.ContextName,
                ctx.RequestAborted);

            return Results.Ok(new { response });
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message);
        }
    }

    internal static IResult HandleListApiContexts(OpenApiContextManager contextManager)
    {
        var contexts = contextManager.GetAllContexts();
        return Results.Ok(new { contexts, count = contexts.Count });
    }

    internal static IResult HandleGetApiContext(string contextName, OpenApiContextManager contextManager)
    {
        var context = contextManager.GetContext(contextName);
        if (context == null)
        {
            return Results.NotFound(new { error = $"Context '{contextName}' not found" });
        }

        return Results.Ok(new
        {
            name = context.Name,
            totalCalls = context.TotalCalls,
            recentCalls = context.RecentCalls.TakeLast(10).Select(c => new
            {
                timestamp = c.Timestamp,
                method = c.Method,
                path = c.Path,
                requestBody = c.RequestBody,
                responseBody = c.ResponseBody?.Length > 500 ? c.ResponseBody.Substring(0, 500) + "..." : c.ResponseBody
            }),
            sharedData = context.SharedData.Take(50), // Limit to prevent large responses
            summary = context.ContextSummary,
            createdAt = context.CreatedAt,
            lastUsedAt = context.LastUsedAt
        });
    }

    internal static IResult HandleClearApiContext(string contextName, OpenApiContextManager contextManager)
    {
        var removed = contextManager.ClearContext(contextName);
        if (removed)
        {
            return Results.Ok(new { message = $"Context '{contextName}' cleared successfully" });
        }
        return Results.NotFound(new { error = $"Context '{contextName}' not found" });
    }

    internal static IResult HandleClearAllApiContexts(OpenApiContextManager contextManager)
    {
        contextManager.ClearAllContexts();
        return Results.Ok(new { message = "All contexts cleared successfully" });
    }
}
