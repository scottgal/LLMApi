using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using mostlylucid.mockllmapi.Services;
using System.Text.Json;

namespace mostlylucid.mockllmapi;

/// <summary>
/// Management endpoints for gRPC proto definitions
/// </summary>
internal static class GrpcManagementEndpoints
{
    /// <summary>
    /// Upload a .proto file
    /// </summary>
    internal static async Task<IResult> HandleProtoUpload(
        HttpContext context,
        ProtoDefinitionManager manager,
        ILogger<ProtoDefinitionManager> logger)
    {
        try
        {
            string protoContent;
            string fileName = "uploaded.proto";

            // Check if it's a multipart/form-data request
            if (context.Request.HasFormContentType)
            {
                var form = await context.Request.ReadFormAsync();
                var file = form.Files.FirstOrDefault();

                if (file == null || file.Length == 0)
                {
                    return Results.BadRequest(new { error = "No proto file provided in form" });
                }

                // Read uploaded file
                using var stream = file.OpenReadStream();
                using var streamReader = new StreamReader(stream);
                protoContent = await streamReader.ReadToEndAsync();
                fileName = file.FileName;
            }
            else
            {
                // Read from body as plain text
                using var reader = new StreamReader(context.Request.Body);
                protoContent = await reader.ReadToEndAsync();

                if (string.IsNullOrWhiteSpace(protoContent))
                {
                    return Results.BadRequest(new { error = "No proto content provided" });
                }

                // Try to get filename from query string
                if (context.Request.Query.TryGetValue("name", out var nameValue))
                {
                    var name = nameValue.ToString().Trim();
                    if (!string.IsNullOrEmpty(name))
                    {
                        fileName = name.EndsWith(".proto") ? name : $"{name}.proto";
                    }
                }
            }

            var definition = manager.AddProtoDefinition(protoContent, fileName);
            logger.LogInformation("Proto definition uploaded: {Name}", definition.Name);

            return Results.Ok(new
            {
                message = "Proto definition uploaded successfully",
                name = definition.Name,
                package = definition.Package,
                services = definition.Services.Select(s => new
                {
                    name = s.Name,
                    methods = s.Methods.Select(m => new
                    {
                        name = m.Name,
                        type = m.GetMethodType().ToString(),
                        input = m.InputType,
                        output = m.OutputType
                    })
                })
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error uploading proto definition");
            return Results.Problem(detail: ex.Message, title: "Error uploading proto");
        }
    }

    /// <summary>
    /// List all uploaded proto definitions
    /// </summary>
    internal static IResult HandleListProtos(ProtoDefinitionManager manager)
    {
        var definitions = manager.GetAllDefinitions();

        return Results.Ok(new
        {
            protos = definitions.Select(d => new
            {
                name = d.Name,
                package = d.Package,
                syntax = d.Syntax,
                uploadedAt = d.UploadedAt,
                services = d.Services.Select(s => new
                {
                    name = s.Name,
                    methods = s.Methods.Select(m => new
                    {
                        name = m.Name,
                        inputType = m.InputType,
                        outputType = m.OutputType,
                        type = m.GetMethodType().ToString()
                    }).ToList()
                }).ToList()
            }),
            count = definitions.Count
        });
    }

    /// <summary>
    /// Get details of a specific proto definition
    /// </summary>
    internal static IResult HandleGetProto(string protoName, ProtoDefinitionManager manager)
    {
        var definition = manager.GetDefinition(protoName);

        if (definition == null)
        {
            return Results.NotFound(new
            {
                error = $"Proto definition '{protoName}' not found",
                available = manager.GetAllDefinitions().Select(d => d.Name).ToList()
            });
        }

        return Results.Ok(new
        {
            name = definition.Name,
            package = definition.Package,
            syntax = definition.Syntax,
            uploadedAt = definition.UploadedAt,
            services = definition.Services.Select(s => new
            {
                name = s.Name,
                methods = s.Methods.Select(m => new
                {
                    name = m.Name,
                    type = m.GetMethodType().ToString(),
                    inputType = m.InputType,
                    outputType = m.OutputType,
                    clientStreaming = m.ClientStreaming,
                    serverStreaming = m.ServerStreaming
                })
            }),
            messages = definition.Messages.Select(m => new
            {
                name = m.Name,
                fields = m.Fields.Select(f => new
                {
                    name = f.Name,
                    type = f.Type,
                    number = f.Number,
                    repeated = f.Repeated,
                    optional = f.Optional
                })
            }),
            rawContent = definition.RawContent
        });
    }

    /// <summary>
    /// Delete a proto definition
    /// </summary>
    internal static IResult HandleDeleteProto(string protoName, ProtoDefinitionManager manager, ILogger<ProtoDefinitionManager> logger)
    {
        var removed = manager.RemoveDefinition(protoName);

        if (removed)
        {
            logger.LogInformation("Proto definition deleted: {Name}", protoName);
            return Results.Ok(new { message = $"Proto definition '{protoName}' deleted successfully" });
        }

        return Results.NotFound(new { error = $"Proto definition '{protoName}' not found" });
    }

    /// <summary>
    /// Clear all proto definitions
    /// </summary>
    internal static IResult HandleClearAllProtos(ProtoDefinitionManager manager, ILogger<ProtoDefinitionManager> logger)
    {
        var count = manager.GetAllDefinitions().Count;
        manager.ClearAll();

        logger.LogInformation("Cleared all {Count} proto definitions", count);

        return Results.Ok(new
        {
            message = "All proto definitions cleared",
            clearedCount = count
        });
    }
}
