using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using mostlylucid.mockllmapi.Models;
using mostlylucid.mockllmapi.Services.Tools;

namespace mostlylucid.mockllmapi;

/// <summary>
///     API endpoints for unit test generation with Pyguin + LLM fallback
/// </summary>
internal static class UnitTestEndpoints
{
    /// <summary>
    ///     Generates unit tests for a tool with Pyguin (primary) and LLM (fallback)
    ///     POST /api/tools/generate-tests
    /// </summary>
    internal static async Task<IResult> HandleGenerateTests(
        HttpContext ctx,
        UnitTestGenerator generator,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            // Parse request
            using var reader = new StreamReader(ctx.Request.Body);
            var json = await reader.ReadToEndAsync(cancellationToken);
            var request = JsonSerializer.Deserialize(json, LLMockSerializerContext.CaseInsensitiveInstance.GenerateTestsRequest);

            if (request == null || string.IsNullOrWhiteSpace(request.ToolName))
                return Results.BadRequest(new { error = "ToolName is required" });

            if (string.IsNullOrWhiteSpace(request.SourceCode))
                return Results.BadRequest(new { error = "SourceCode is required" });

            logger.LogInformation("Generating unit tests for tool: {ToolName}", request.ToolName);

            // Generate tests
            var result = await generator.GenerateTestsAsync(
                request.ToolName,
                request.SourceCode,
                request.ExistingTests,
                cancellationToken);

            if (result.Success)
            {
                logger.LogInformation("Successfully generated tests for {ToolName} using {Method}",
                    request.ToolName, result.GenerationMethod);

                return Results.Ok(new
                {
                    success = true,
                    toolName = result.ToolName,
                    generationMethod = result.GenerationMethod,
                    duration = result.Duration.TotalSeconds,
                    generatedTests = result.GeneratedTests,
                    codeReview = result.CodeReview,
                    outputFilePath = result.OutputFilePath,
                    pyguinAttempted = result.PyguinAttempted,
                    pyguinError = result.PyguinError
                });
            }

            logger.LogWarning("Failed to generate tests for {ToolName}: {Error}",
                request.ToolName, result.Error);

            return Results.Json(new
            {
                success = false,
                toolName = result.ToolName,
                error = result.Error,
                pyguinAttempted = result.PyguinAttempted,
                pyguinError = result.PyguinError
            }, statusCode: 500);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception during test generation");
            return Results.Problem(ex.Message);
        }
    }

    /// <summary>
    ///     Reviews code for correctness using LLM
    ///     POST /api/tools/review-code
    /// </summary>
    internal static async Task<IResult> HandleReviewCode(
        HttpContext ctx,
        UnitTestGenerator generator,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            // Parse request
            using var reader = new StreamReader(ctx.Request.Body);
            var json = await reader.ReadToEndAsync(cancellationToken);
            var request = JsonSerializer.Deserialize(json, LLMockSerializerContext.CaseInsensitiveInstance.ReviewCodeRequest);

            if (request == null || string.IsNullOrWhiteSpace(request.SourceCode))
                return Results.BadRequest(new { error = "SourceCode is required" });

            logger.LogInformation("Reviewing code for: {Name}", request.Name ?? "unnamed");

            // Use the test generator to get code review
            // (It will skip Pyguin and go straight to LLM review)
            var result = await generator.GenerateTestsAsync(
                request.Name ?? "CodeReview",
                request.SourceCode,
                null,
                cancellationToken);

            if (result.Success && !string.IsNullOrEmpty(result.CodeReview))
                return Results.Ok(new
                {
                    success = true,
                    codeReview = result.CodeReview,
                    generatedTests = result.GeneratedTests
                });

            return Results.Json(new
            {
                success = false,
                error = result.Error ?? "Code review failed"
            }, statusCode: 500);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception during code review");
            return Results.Problem(ex.Message);
        }
    }

    /// <summary>
    ///     Gets the test output directory info
    ///     GET /api/tools/test-output
    /// </summary>
    internal static IResult HandleGetTestOutput(UnitTestGenerator generator)
    {
        var directory = generator.GetTestOutputDirectory();
        var exists = Directory.Exists(directory);

        List<object> files;
        if (exists)
            files = Directory.GetFiles(directory, "*.cs")
                .Select(f => new
                {
                    fileName = Path.GetFileName(f),
                    created = File.GetCreationTimeUtc(f),
                    size = new FileInfo(f).Length
                })
                .OrderByDescending(f => f.created)
                .Take(50)
                .Cast<object>()
                .ToList();
        else
            files = new List<object>();

        return Results.Ok(new
        {
            outputDirectory = directory,
            exists,
            fileCount = files.Count,
            files
        });
    }

    /// <summary>
    ///     Downloads a specific generated test file
    ///     GET /api/tools/test-output/{fileName}
    /// </summary>
    internal static IResult HandleDownloadTestFile(
        string fileName,
        UnitTestGenerator generator)
    {
        // Sanitize filename to prevent directory traversal
        fileName = Path.GetFileName(fileName);

        var filePath = Path.Combine(generator.GetTestOutputDirectory(), fileName);

        if (!File.Exists(filePath)) return Results.NotFound(new { error = $"Test file not found: {fileName}" });

        var fileBytes = File.ReadAllBytes(filePath);
        return Results.File(fileBytes, "text/plain", fileName);
    }
}