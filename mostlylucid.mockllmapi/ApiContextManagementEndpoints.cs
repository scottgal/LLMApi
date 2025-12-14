using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using mostlylucid.mockllmapi.Models;
using mostlylucid.mockllmapi.Services;

namespace mostlylucid.mockllmapi;

/// <summary>
///     Management endpoints for API Contexts - view and modify context history
/// </summary>
internal static class ApiContextManagementEndpoints
{
    /// <summary>
    ///     Gets a list of all API context names and their summary information
    /// </summary>
    internal static IResult HandleListAllContexts(OpenApiContextManager contextManager)
    {
        var contexts = contextManager.GetAllContexts();
        return Results.Ok(new
        {
            contexts = contexts.Select(c => new
            {
                name = c.Name,
                totalCalls = c.TotalCalls,
                lastUsedAt = c.LastUsedAt,
                createdAt = c.CreatedAt,
                recentCallCount = c.RecentCallCount,
                sharedDataCount = c.SharedDataCount,
                hasSummary = c.HasSummary
            }).ToList(),
            count = contexts.Count
        });
    }

    /// <summary>
    ///     Gets the complete details of a specific API context including all calls and shared data
    /// </summary>
    internal static IResult HandleGetContext(
        string contextName,
        OpenApiContextManager contextManager,
        ILogger<OpenApiContextManager> logger,
        [FromQuery] bool includeCallDetails = true,
        [FromQuery] int? maxCalls = null)
    {
        var context = contextManager.GetContext(contextName);

        if (context == null)
        {
            var allContexts = contextManager.GetAllContexts();
            return Results.NotFound(new
            {
                error = $"Context '{contextName}' not found",
                availableContexts = allContexts.Select(c => c.Name).ToList()
            });
        }

        var response = new Dictionary<string, object>
        {
            ["name"] = context.Name,
            ["totalCalls"] = context.TotalCalls,
            ["lastUsedAt"] = context.LastUsedAt,
            ["createdAt"] = context.CreatedAt,
            ["sharedData"] = context.SharedData,
            ["contextSummary"] = context.ContextSummary
        };

        if (includeCallDetails)
        {
            var calls = context.RecentCalls.AsEnumerable();
            if (maxCalls.HasValue) calls = calls.Take(maxCalls.Value);

            response["recentCalls"] = calls.Select(call => new
            {
                method = call.Method,
                path = call.Path,
                requestBody = call.RequestBody,
                responseBody = call.ResponseBody,
                timestamp = call.Timestamp
            }).ToList();

            response["recentCallCount"] = context.RecentCalls.Count;
        }

        return Results.Ok(response);
    }

    /// <summary>
    ///     Gets the formatted prompt text for a specific context (what gets sent to LLM)
    /// </summary>
    internal static IResult HandleGetContextPrompt(
        string contextName,
        OpenApiContextManager contextManager,
        ILogger<OpenApiContextManager> logger)
    {
        var promptText = contextManager.GetContextForPrompt(contextName);

        if (string.IsNullOrEmpty(promptText))
        {
            var contexts = contextManager.GetAllContexts();
            return Results.NotFound(new
            {
                error = $"Context '{contextName}' not found or is empty",
                availableContexts = contexts.Select(c => c.Name).ToList()
            });
        }

        return Results.Ok(new
        {
            contextName,
            promptText,
            length = promptText.Length
        });
    }

    /// <summary>
    ///     Adds a call to an existing context or creates a new context
    /// </summary>
    internal static IResult HandleAddToContext(
        string contextName,
        HttpContext httpContext,
        OpenApiContextManager contextManager,
        ILogger<OpenApiContextManager> logger)
    {
        try
        {
            var body = new StreamReader(httpContext.Request.Body).ReadToEndAsync().Result;

            if (string.IsNullOrWhiteSpace(body)) return Results.BadRequest(new { error = "Request body is required" });

            var request = JsonSerializer.Deserialize(body, LLMockSerializerContext.CaseInsensitiveInstance.AddToContextRequest);

            if (request == null) return Results.BadRequest(new { error = "Invalid request format" });

            // Validate required fields
            if (string.IsNullOrWhiteSpace(request.Method))
                return Results.BadRequest(new { error = "Method is required" });

            if (string.IsNullOrWhiteSpace(request.Path)) return Results.BadRequest(new { error = "Path is required" });

            if (string.IsNullOrWhiteSpace(request.ResponseBody))
                return Results.BadRequest(new { error = "ResponseBody is required" });

            // Add to context
            contextManager.AddToContext(
                contextName,
                request.Method,
                request.Path,
                request.RequestBody,
                request.ResponseBody
            );

            logger.LogInformation("Added call to context '{Context}': {Method} {Path}", contextName, request.Method,
                request.Path);

            return Results.Ok(new
            {
                message = $"Call added to context '{contextName}'",
                contextName,
                method = request.Method,
                path = request.Path
            });
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse request body for context '{Context}'", contextName);
            return Results.BadRequest(new { error = "Invalid JSON in request body", details = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error adding to context '{Context}'", contextName);
            return Results.Problem(ex.Message, title: "Error adding to context");
        }
    }

    /// <summary>
    ///     Updates shared data for a context (merges with existing data)
    /// </summary>
    internal static IResult HandleUpdateSharedData(
        string contextName,
        HttpContext httpContext,
        OpenApiContextManager contextManager,
        ILogger<OpenApiContextManager> logger)
    {
        try
        {
            var body = new StreamReader(httpContext.Request.Body).ReadToEndAsync().Result;

            if (string.IsNullOrWhiteSpace(body)) return Results.BadRequest(new { error = "Request body is required" });

            var sharedData = JsonSerializer.Deserialize(body, LLMockSerializerContext.CaseInsensitiveInstance.DictionaryStringObject);

            if (sharedData == null || sharedData.Count == 0)
                return Results.BadRequest(new { error = "Shared data must be a non-empty object" });

            // Check if context exists
            var context = contextManager.GetContext(contextName);

            if (context == null)
            {
                var allContexts = contextManager.GetAllContexts();
                return Results.NotFound(new
                {
                    error = $"Context '{contextName}' not found. Add a call first to create the context.",
                    availableContexts = allContexts.Select(c => c.Name).ToList()
                });
            }

            // Merge shared data by adding a synthetic response that updates the shared data
            // The context manager will extract and update the shared data automatically
            var syntheticResponse = JsonSerializer.Serialize(sharedData, LLMockSerializerContext.Default.DictionaryStringObject);
            contextManager.AddToContext(contextName, "PATCH", "/_shared-data", null, syntheticResponse);

            logger.LogInformation("Updated shared data for context '{Context}' with {Count} keys", contextName,
                sharedData.Count);

            // Get updated context
            var updatedContext = contextManager.GetContext(contextName);

            return Results.Ok(new
            {
                message = $"Shared data updated for context '{contextName}'",
                contextName,
                updatedKeys = sharedData.Keys.ToList(),
                currentSharedData = updatedContext?.SharedData ?? new Dictionary<string, string>()
            });
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse shared data for context '{Context}'", contextName);
            return Results.BadRequest(new { error = "Invalid JSON in request body", details = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating shared data for context '{Context}'", contextName);
            return Results.Problem(ex.Message, title: "Error updating shared data");
        }
    }

    /// <summary>
    ///     Clears all calls from a specific context (but keeps the context name registered)
    /// </summary>
    internal static IResult HandleClearContext(
        string contextName,
        OpenApiContextManager contextManager,
        ILogger<OpenApiContextManager> logger)
    {
        var success = contextManager.ClearContext(contextName);

        if (success)
        {
            logger.LogInformation("Cleared context: {Context}", contextName);
            return Results.Ok(new
            {
                message = $"Context '{contextName}' cleared successfully",
                contextName
            });
        }

        var contexts = contextManager.GetAllContexts();
        return Results.NotFound(new
        {
            error = $"Context '{contextName}' not found",
            availableContexts = contexts.Select(c => c.Name).ToList()
        });
    }

    /// <summary>
    ///     Clears all API contexts
    /// </summary>
    internal static IResult HandleClearAllContexts(
        OpenApiContextManager contextManager,
        ILogger<OpenApiContextManager> logger)
    {
        var count = contextManager.GetAllContexts().Count;
        contextManager.ClearAllContexts();

        logger.LogInformation("Cleared all {Count} API contexts", count);

        return Results.Ok(new
        {
            message = "All API contexts cleared successfully",
            clearedCount = count
        });
    }

    /// <summary>
    ///     Deletes a specific context completely (removes it from the manager)
    /// </summary>
    internal static IResult HandleDeleteContext(
        string contextName,
        OpenApiContextManager contextManager,
        ILogger<OpenApiContextManager> logger)
    {
        // Clear first to remove the data
        var success = contextManager.ClearContext(contextName);

        if (success)
        {
            logger.LogInformation("Deleted context: {Context}", contextName);
            return Results.Ok(new
            {
                message = $"Context '{contextName}' deleted successfully",
                contextName
            });
        }

        var contexts = contextManager.GetAllContexts();
        return Results.NotFound(new
        {
            error = $"Context '{contextName}' not found",
            availableContexts = contexts.Select(c => c.Name).ToList()
        });
    }

}