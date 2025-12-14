using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using mostlylucid.mockllmapi.Models;
using mostlylucid.mockllmapi.Services;

namespace mostlylucid.mockllmapi;

internal static class SignalRManagementEndpoints
{
    internal static async Task<IResult> HandleCreateDynamicContext(
        HttpContext ctx,
        DynamicHubContextManager contextManager,
        LlmClient llmClient,
        ILogger<DynamicHubContextManager> logger)
    {
        try
        {
            HubContextConfig? config;

            // Check Content-Type to determine how to parse the body
            var contentType = ctx.Request.ContentType?.ToLowerInvariant() ?? "";

            if (contentType.Contains("application/x-www-form-urlencoded"))
            {
                // Parse form-encoded data
                var form = await ctx.Request.ReadFormAsync();
                config = new HubContextConfig
                {
                    Name = form.ContainsKey("name") ? form["name"].ToString() : string.Empty,
                    Description = form.ContainsKey("description") ? form["description"].ToString() : string.Empty,
                    Method = form.ContainsKey("method") && !string.IsNullOrWhiteSpace(form["method"])
                        ? form["method"].ToString()
                        : "GET",
                    Path = form.ContainsKey("path") && !string.IsNullOrWhiteSpace(form["path"])
                        ? form["path"].ToString()
                        : "/data",
                    Body = form.ContainsKey("body") && !string.IsNullOrWhiteSpace(form["body"])
                        ? form["body"].ToString()
                        : null,
                    Shape = form.ContainsKey("shape") && !string.IsNullOrWhiteSpace(form["shape"])
                        ? form["shape"].ToString()
                        : null
                };

                // Parse error configuration if present
                if (form.ContainsKey("error") && !string.IsNullOrWhiteSpace(form["error"]))
                {
                    var errorStr = form["error"].ToString();
                    if (int.TryParse(errorStr, out var errorCode) && errorCode >= 100 && errorCode < 600)
                    {
                        var errorMessage = form.ContainsKey("errorMessage") &&
                                           !string.IsNullOrWhiteSpace(form["errorMessage"])
                            ? form["errorMessage"].ToString()
                            : null;
                        var errorDetails = form.ContainsKey("errorDetails") &&
                                           !string.IsNullOrWhiteSpace(form["errorDetails"])
                            ? form["errorDetails"].ToString()
                            : null;

                        config.ErrorConfig = new ErrorConfig(errorCode, errorMessage, errorDetails);
                    }
                }
            }
            else
            {
                // Parse JSON data
                using var reader = new StreamReader(ctx.Request.Body);
                var json = await reader.ReadToEndAsync();
                config = JsonSerializer.Deserialize(json, LLMockSerializerContext.CaseInsensitiveInstance.HubContextConfig);
            }

            if (config == null || string.IsNullOrWhiteSpace(config.Name))
                return Results.BadRequest(new { error = "Invalid context configuration. Name is required." });

            // If description is provided but no shape, use LLM to generate shape
            if (!string.IsNullOrWhiteSpace(config.Description) && string.IsNullOrWhiteSpace(config.Shape))
            {
                var shapePrompt = $@"Based on this description, generate a JSON schema that defines the data structure.
Description: {config.Description}

Return ONLY valid JSON schema with no additional text. Include:
- type, properties, required fields
- Appropriate data types (string, number, boolean, object, array)
- Clear descriptions for each property

Example format:
{{
  ""type"": ""object"",
  ""properties"": {{
    ""fieldName"": {{
      ""type"": ""string"",
      ""description"": ""Description of field""
    }}
  }},
  ""required"": [""fieldName""]
}}";

                try
                {
                    var shape = await llmClient.GetCompletionAsync(shapePrompt, ctx.RequestAborted);
                    config.Shape = shape;
                    config.IsJsonSchema = true;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to generate shape from description, using description as-is");
                    config.Shape = config.Description;
                }
            }

            var success = contextManager.RegisterContext(config);

            if (success)
            {
                logger.LogInformation(
                    "Context registered successfully: {Name}, IsActive={IsActive}, ConnectionCount={Count}",
                    config.Name, config.IsActive, config.ConnectionCount);

                return Results.Ok(new
                {
                    message = $"Context '{config.Name}' registered successfully",
                    context = config
                });
            }

            logger.LogWarning("Context registration failed - already exists: {Name}", config.Name);
            return Results.Conflict(new { error = $"Context '{config.Name}' already exists" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating dynamic context");
            return Results.Problem(ex.Message);
        }
    }

    internal static IResult HandleListContexts(
        DynamicHubContextManager contextManager,
        IConfiguration configuration)
    {
        // Get dynamic contexts registered at runtime
        var dynamicContexts = contextManager.GetAllContexts();

        // Also read configured contexts directly from appsettings to ensure they are always present
        var configured = configuration
            .GetSection($"{LLMockApiOptions.SectionName}:HubContexts")
            .Get<List<HubContextConfig>>() ?? new List<HubContextConfig>();

        // Merge configured + dynamic, with dynamic taking precedence on name collisions
        var merged = new Dictionary<string, HubContextConfig>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in configured)
            if (!string.IsNullOrWhiteSpace(c.Name))
                merged[c.Name] = c;

        foreach (var c in dynamicContexts)
            if (!string.IsNullOrWhiteSpace(c.Name))
                merged[c.Name] = c; // override configured with dynamic instance

        var contexts = merged.Values.ToList();
        return Results.Ok(new { contexts, count = contexts.Count });
    }

    internal static IResult HandleGetContext(
        string contextName,
        DynamicHubContextManager contextManager,
        IConfiguration configuration)
    {
        // First try dynamic runtime contexts
        var context = contextManager.GetContext(contextName);
        if (context != null) return Results.Ok(context);

        // Fallback to configured contexts from appsettings.json to ensure visibility even if not dynamically registered
        var configured = configuration
                             .GetSection($"{LLMockApiOptions.SectionName}:HubContexts")
                             .Get<List<HubContextConfig>>()
                         ?? new List<HubContextConfig>();

        var match = configured.Find(c =>
            !string.IsNullOrWhiteSpace(c.Name) &&
            string.Equals(c.Name, contextName, StringComparison.OrdinalIgnoreCase));
        if (match != null) return Results.Ok(match);

        return Results.NotFound(new { error = $"Context '{contextName}' not found" });
    }

    internal static IResult HandleDeleteContext(
        string contextName,
        DynamicHubContextManager contextManager)
    {
        var success = contextManager.UnregisterContext(contextName);

        if (success) return Results.Ok(new { message = $"Context '{contextName}' deleted successfully" });

        return Results.NotFound(new { error = $"Context '{contextName}' not found" });
    }

    internal static IResult HandleStartContext(
        string contextName,
        DynamicHubContextManager contextManager)
    {
        var success = contextManager.StartContext(contextName);

        if (success) return Results.Ok(new { message = $"Context '{contextName}' started successfully" });

        return Results.NotFound(new { error = $"Context '{contextName}' not found" });
    }

    internal static IResult HandleStopContext(
        string contextName,
        DynamicHubContextManager contextManager)
    {
        var success = contextManager.StopContext(contextName);

        if (success) return Results.Ok(new { message = $"Context '{contextName}' stopped successfully" });

        return Results.NotFound(new { error = $"Context '{contextName}' not found" });
    }
}