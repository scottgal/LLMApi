using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using mostlylucid.mockllmapi.Models;
using mostlylucid.mockllmapi.Services;

namespace mostlylucid.mockllmapi;

/// <summary>
///     Management endpoints for the Journeys system.
///     Provides APIs to manage journey templates and active sessions.
/// </summary>
public static class JourneyManagementEndpoints
{
    /// <summary>
    ///     Maps all journey management endpoints under the specified pattern.
    ///     Default pattern: /api/journeys
    /// </summary>
    public static IEndpointRouteBuilder MapJourneyManagement(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/api/journeys")
    {
        var group = endpoints.MapGroup(pattern);

        // Template management endpoints
        group.MapGet("/templates", GetAllTemplates)
            .WithName("GetJourneyTemplates")
            .WithOpenApi();

        group.MapGet("/templates/{name}", GetTemplate)
            .WithName("GetJourneyTemplate")
            .WithOpenApi();

        group.MapPost("/templates", (Delegate)CreateTemplate)
            .WithName("CreateJourneyTemplate")
            .WithOpenApi();

        group.MapDelete("/templates/{name}", DeleteTemplate)
            .WithName("DeleteJourneyTemplate")
            .WithOpenApi();

        group.MapGet("/templates/by-modality/{modality}", GetTemplatesByModality)
            .WithName("GetJourneyTemplatesByModality")
            .WithOpenApi();

        // Session management endpoints
        group.MapPost("/sessions/{sessionId}/start", (Delegate)StartJourney)
            .WithName("StartJourney")
            .WithOpenApi();

        group.MapPost("/sessions/{sessionId}/start-random", (Delegate)StartRandomJourney)
            .WithName("StartRandomJourney")
            .WithOpenApi();

        group.MapGet("/sessions/{sessionId}", GetSessionStatus)
            .WithName("GetJourneySessionStatus")
            .WithOpenApi();

        group.MapPost("/sessions/{sessionId}/advance", AdvanceJourney)
            .WithName("AdvanceJourney")
            .WithOpenApi();

        group.MapDelete("/sessions/{sessionId}", EndJourney)
            .WithName("EndJourney")
            .WithOpenApi();

        // Utility endpoints
        group.MapGet("/status", GetJourneySystemStatus)
            .WithName("GetJourneySystemStatus")
            .WithOpenApi();

        return endpoints;
    }

    /// <summary>
    ///     GET /api/journeys/templates - List all journey templates
    /// </summary>
    private static IResult GetAllTemplates(HttpContext context)
    {
        var registry = context.RequestServices.GetRequiredService<JourneyRegistry>();
        var summaries = registry.GetJourneySummaries();

        return Results.Ok(new
        {
            enabled = registry.IsEnabled,
            count = summaries.Count,
            templates = summaries.Select(s => new
            {
                name = s.Name,
                modality = s.Modality.ToString(),
                stepCount = s.StepCount,
                weight = s.Weight
            })
        });
    }

    /// <summary>
    ///     GET /api/journeys/templates/{name} - Get a specific journey template
    /// </summary>
    private static IResult GetTemplate(string name, HttpContext context)
    {
        var registry = context.RequestServices.GetRequiredService<JourneyRegistry>();
        var template = registry.GetJourney(name);

        if (template == null)
            return Results.NotFound(new { error = $"Journey template '{name}' not found" });

        return Results.Ok(new
        {
            name = template.Name,
            modality = template.Modality.ToString(),
            weight = template.Weight,
            promptHints = template.PromptHints,
            steps = template.Steps.Select((s, i) => new
            {
                index = i,
                method = s.Method,
                path = s.Path,
                description = s.Description,
                hasShape = !string.IsNullOrEmpty(s.ShapeJson),
                hasBody = !string.IsNullOrEmpty(s.BodyTemplateJson),
                promptHints = s.PromptHints
            })
        });
    }

    /// <summary>
    ///     POST /api/journeys/templates - Create a new journey template
    /// </summary>
    private static async Task<IResult> CreateTemplate(HttpContext context)
    {
        var registry = context.RequestServices.GetRequiredService<JourneyRegistry>();

        try
        {
            var config = await context.Request.ReadFromJsonAsync<JourneyTemplateConfig>();
            if (config == null)
                return Results.BadRequest(new { error = "Invalid journey template configuration" });

            if (string.IsNullOrWhiteSpace(config.Name))
                return Results.BadRequest(new { error = "Journey name is required" });

            if (config.Steps.Count == 0)
                return Results.BadRequest(new { error = "Journey must have at least one step" });

            var template = config.ToRecord();
            registry.RegisterJourney(template);

            return Results.Created($"/api/journeys/templates/{template.Name}", new
            {
                name = template.Name,
                modality = template.Modality.ToString(),
                stepCount = template.Steps.Count,
                message = "Journey template created successfully"
            });
        }
        catch (JsonException ex)
        {
            return Results.BadRequest(new { error = "Invalid JSON", details = ex.Message });
        }
    }

    /// <summary>
    ///     DELETE /api/journeys/templates/{name} - Delete a journey template
    /// </summary>
    private static IResult DeleteTemplate(string name, HttpContext context)
    {
        var registry = context.RequestServices.GetRequiredService<JourneyRegistry>();

        if (registry.RemoveJourney(name))
            return Results.Ok(new { message = $"Journey template '{name}' deleted" });

        return Results.NotFound(new { error = $"Journey template '{name}' not found" });
    }

    /// <summary>
    ///     GET /api/journeys/templates/by-modality/{modality} - Get templates by modality
    /// </summary>
    private static IResult GetTemplatesByModality(string modality, HttpContext context)
    {
        var registry = context.RequestServices.GetRequiredService<JourneyRegistry>();

        if (!Enum.TryParse<JourneyModality>(modality, true, out var mod))
            return Results.BadRequest(new
            {
                error = $"Invalid modality '{modality}'",
                validValues = Enum.GetNames<JourneyModality>()
            });

        var templates = registry.GetJourneysByModality(mod);

        return Results.Ok(new
        {
            modality = mod.ToString(),
            count = templates.Count,
            templates = templates.Select(t => new
            {
                name = t.Name,
                stepCount = t.Steps.Count,
                weight = t.Weight
            })
        });
    }

    /// <summary>
    ///     POST /api/journeys/sessions/{sessionId}/start - Start a specific journey for a session
    ///     Body: { "journeyName": "...", "variables": { ... } }
    /// </summary>
    private static async Task<IResult> StartJourney(string sessionId, HttpContext context)
    {
        var sessionManager = context.RequestServices.GetRequiredService<JourneySessionManager>();

        try
        {
            var request = await context.Request.ReadFromJsonAsync<StartJourneyRequest>();
            if (request == null || string.IsNullOrWhiteSpace(request.JourneyName))
                return Results.BadRequest(new { error = "journeyName is required" });

            var instance = sessionManager.CreateJourneyInstance(
                sessionId,
                request.JourneyName,
                request.Variables);

            return Results.Ok(new
            {
                sessionId = instance.SessionId,
                journeyName = instance.Template.Name,
                modality = instance.Template.Modality.ToString(),
                currentStep = instance.CurrentStepIndex,
                totalSteps = instance.ResolvedSteps.Count,
                isComplete = instance.IsComplete,
                currentStepDetails = instance.CurrentStep != null
                    ? new
                    {
                        method = instance.CurrentStep.Method,
                        path = instance.CurrentStep.Path,
                        description = instance.CurrentStep.Description
                    }
                    : null,
                variables = instance.Variables
            });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (JsonException ex)
        {
            return Results.BadRequest(new { error = "Invalid JSON", details = ex.Message });
        }
    }

    /// <summary>
    ///     POST /api/journeys/sessions/{sessionId}/start-random - Start a random journey
    ///     Body: { "modality": "Rest", "variables": { ... } } (modality optional)
    /// </summary>
    private static async Task<IResult> StartRandomJourney(string sessionId, HttpContext context)
    {
        var sessionManager = context.RequestServices.GetRequiredService<JourneySessionManager>();

        try
        {
            var request = await context.Request.ReadFromJsonAsync<StartRandomJourneyRequest>();

            JourneyModality? modality = null;
            if (!string.IsNullOrWhiteSpace(request?.Modality))
            {
                if (!Enum.TryParse<JourneyModality>(request.Modality, true, out var mod))
                    return Results.BadRequest(new
                    {
                        error = $"Invalid modality '{request.Modality}'",
                        validValues = Enum.GetNames<JourneyModality>()
                    });
                modality = mod;
            }

            var instance = sessionManager.CreateRandomJourneyInstance(
                sessionId,
                modality,
                request?.Variables);

            if (instance == null)
                return Results.NotFound(new
                {
                    error = "No journey templates available",
                    modality = modality?.ToString()
                });

            return Results.Ok(new
            {
                sessionId = instance.SessionId,
                journeyName = instance.Template.Name,
                modality = instance.Template.Modality.ToString(),
                currentStep = instance.CurrentStepIndex,
                totalSteps = instance.ResolvedSteps.Count,
                isComplete = instance.IsComplete,
                currentStepDetails = instance.CurrentStep != null
                    ? new
                    {
                        method = instance.CurrentStep.Method,
                        path = instance.CurrentStep.Path,
                        description = instance.CurrentStep.Description
                    }
                    : null,
                variables = instance.Variables
            });
        }
        catch (JsonException ex)
        {
            return Results.BadRequest(new { error = "Invalid JSON", details = ex.Message });
        }
    }

    /// <summary>
    ///     GET /api/journeys/sessions/{sessionId} - Get session status
    /// </summary>
    private static IResult GetSessionStatus(string sessionId, HttpContext context)
    {
        var sessionManager = context.RequestServices.GetRequiredService<JourneySessionManager>();
        var instance = sessionManager.GetJourneyForSession(sessionId);

        if (instance == null)
            return Results.NotFound(new { error = $"No active journey for session '{sessionId}'" });

        return Results.Ok(new
        {
            sessionId = instance.SessionId,
            journeyName = instance.Template.Name,
            modality = instance.Template.Modality.ToString(),
            currentStep = instance.CurrentStepIndex,
            totalSteps = instance.ResolvedSteps.Count,
            isComplete = instance.IsComplete,
            currentStepDetails = instance.CurrentStep != null
                ? new
                {
                    method = instance.CurrentStep.Method,
                    path = instance.CurrentStep.Path,
                    description = instance.CurrentStep.Description
                }
                : null,
            allSteps = instance.ResolvedSteps.Select((s, i) => new
            {
                index = i,
                method = s.Method,
                path = s.Path,
                description = s.Description,
                isCurrent = i == instance.CurrentStepIndex,
                isCompleted = i < instance.CurrentStepIndex
            }),
            variables = instance.Variables
        });
    }

    /// <summary>
    ///     POST /api/journeys/sessions/{sessionId}/advance - Advance to next step
    /// </summary>
    private static IResult AdvanceJourney(string sessionId, HttpContext context)
    {
        var sessionManager = context.RequestServices.GetRequiredService<JourneySessionManager>();
        var instance = sessionManager.AdvanceJourney(sessionId);

        if (instance == null)
            return Results.NotFound(new { error = $"No active journey for session '{sessionId}'" });

        return Results.Ok(new
        {
            sessionId = instance.SessionId,
            journeyName = instance.Template.Name,
            currentStep = instance.CurrentStepIndex,
            totalSteps = instance.ResolvedSteps.Count,
            isComplete = instance.IsComplete,
            currentStepDetails = instance.CurrentStep != null
                ? new
                {
                    method = instance.CurrentStep.Method,
                    path = instance.CurrentStep.Path,
                    description = instance.CurrentStep.Description
                }
                : null,
            message = instance.IsComplete ? "Journey completed!" : "Advanced to next step"
        });
    }

    /// <summary>
    ///     DELETE /api/journeys/sessions/{sessionId} - End a journey
    /// </summary>
    private static IResult EndJourney(string sessionId, HttpContext context)
    {
        var sessionManager = context.RequestServices.GetRequiredService<JourneySessionManager>();

        if (sessionManager.EndJourney(sessionId))
            return Results.Ok(new { message = $"Journey ended for session '{sessionId}'" });

        return Results.NotFound(new { error = $"No active journey for session '{sessionId}'" });
    }

    /// <summary>
    ///     GET /api/journeys/status - Get journey system status
    /// </summary>
    private static IResult GetJourneySystemStatus(HttpContext context)
    {
        var registry = context.RequestServices.GetRequiredService<JourneyRegistry>();
        var summaries = registry.GetJourneySummaries();

        var byModality = summaries
            .GroupBy(s => s.Modality)
            .ToDictionary(g => g.Key.ToString(), g => g.Count());

        return Results.Ok(new
        {
            enabled = registry.IsEnabled,
            templateCount = summaries.Count,
            templatesByModality = byModality,
            modalities = Enum.GetNames<JourneyModality>()
        });
    }

    // Request DTOs
    private record StartJourneyRequest(
        string JourneyName,
        Dictionary<string, string>? Variables = null);

    private record StartRandomJourneyRequest(
        string? Modality = null,
        Dictionary<string, string>? Variables = null);
}