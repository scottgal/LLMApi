using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using mostlylucid.mockllmapi.Models;
using mostlylucid.mockllmapi.Services.Tools;

namespace mostlylucid.mockllmapi;

/// <summary>
///     API endpoints for tool fitness testing and evolution
/// </summary>
internal static class ToolFitnessEndpoints
{
    /// <summary>
    ///     Runs comprehensive fitness tests on all configured tools
    ///     POST /api/tools/fitness/test
    /// </summary>
    internal static async Task<IResult> HandleRunFitnessTest(
        ToolFitnessTester tester,
        ToolFitnessRagStore ragStore,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Starting tool fitness testing...");

            // Run comprehensive tests
            var report = await tester.TestAllToolsAsync(cancellationToken);

            // Store in RAG database
            await ragStore.StoreReportAsync(report);

            logger.LogInformation("Tool fitness testing complete. Passed: {Passed}/{Total}",
                report.PassedTests, report.TotalTools);

            return Results.Ok(report);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Tool fitness testing failed");
            return Results.Problem(ex.Message);
        }
    }

    /// <summary>
    ///     Gets fitness history for a specific tool
    ///     GET /api/tools/fitness/{toolName}
    /// </summary>
    internal static IResult HandleGetToolFitnessHistory(
        string toolName,
        ToolFitnessRagStore ragStore,
        int maxResults = 10)
    {
        var history = ragStore.GetToolHistory(toolName, maxResults);

        if (!history.Any()) return Results.NotFound(new { error = $"No fitness history found for tool: {toolName}" });

        return Results.Ok(new
        {
            toolName,
            historyCount = history.Count,
            latestFitness = history.Last().FitnessScore,
            history
        });
    }

    /// <summary>
    ///     Gets all low-fitness tools below threshold
    ///     GET /api/tools/fitness/low?threshold=60
    /// </summary>
    internal static IResult HandleGetLowFitnessTools(
        ToolFitnessRagStore ragStore,
        double threshold = 60.0)
    {
        var lowFitnessTools = ragStore.GetLowFitnessTools(threshold);

        return Results.Ok(new
        {
            threshold,
            count = lowFitnessTools.Count,
            tools = lowFitnessTools.Select(kvp => new
            {
                toolName = kvp.Key,
                latestSnapshot = kvp.Value
            })
        });
    }

    /// <summary>
    ///     Gets fitness trends for all tools
    ///     GET /api/tools/fitness/trends
    /// </summary>
    internal static IResult HandleGetFitnessTrends(
        ToolFitnessRagStore ragStore,
        int minSnapshots = 3)
    {
        var trends = ragStore.GetFitnessTrends(minSnapshots);

        return Results.Ok(new
        {
            totalTools = trends.Count,
            trends = trends.Select(kvp => new
            {
                toolName = kvp.Key,
                trend = kvp.Value
            }).OrderBy(t => t.trend.Direction == "Declining" ? 0 : t.trend.Direction == "Stable" ? 1 : 2)
        });
    }

    /// <summary>
    ///     Triggers evolution for low-fitness tools using god-level LLM
    ///     POST /api/tools/fitness/evolve
    ///     Body: { "threshold": 60.0 }
    /// </summary>
    internal static async Task<IResult> HandleEvolveTools(
        HttpContext ctx,
        ToolFitnessTester tester,
        ToolFitnessRagStore ragStore,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            // Parse request body
            using var reader = new StreamReader(ctx.Request.Body);
            var json = await reader.ReadToEndAsync(cancellationToken);
            var request = JsonSerializer.Deserialize(json, LLMockSerializerContext.CaseInsensitiveInstance.EvolveToolsRequest);

            var threshold = request?.Threshold ?? 60.0;

            logger.LogInformation("Starting tool evolution for tools below fitness threshold: {Threshold}", threshold);

            // Get low-fitness tools
            var lowFitnessTools = ragStore.GetLowFitnessTools(threshold);

            if (!lowFitnessTools.Any())
                return Results.Ok(new
                {
                    message = "No low-fitness tools found",
                    threshold,
                    evolutionResults = new List<object>()
                });

            // Get full test results (need to re-run test or load from last report)
            // For now, trigger a new test to get fresh data
            var report = await tester.TestAllToolsAsync(cancellationToken);
            var lowFitnessResults = tester.GetLowFitnessTools(report, threshold);

            // Evolve tools
            var evolutionResults = await tester.EvolveToolsAsync(lowFitnessResults, cancellationToken);

            logger.LogInformation("Tool evolution complete. Evolved {Count} tools", evolutionResults.Count);

            return Results.Ok(new
            {
                threshold,
                toolsEvolved = evolutionResults.Count,
                evolutionResults
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Tool evolution failed");
            return Results.Problem(ex.Message);
        }
    }

    /// <summary>
    ///     Exports complete fitness history to JSON
    ///     GET /api/tools/fitness/export
    /// </summary>
    internal static async Task<IResult> HandleExportFitnessHistory(
        ToolFitnessRagStore ragStore,
        ILogger logger)
    {
        try
        {
            var filePath = await ragStore.ExportFullHistoryAsync();

            return Results.Ok(new
            {
                message = "Fitness history exported successfully",
                filePath,
                storageDirectory = ragStore.GetStorageDirectory()
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to export fitness history");
            return Results.Problem(ex.Message);
        }
    }

    /// <summary>
    ///     Gets storage directory information
    ///     GET /api/tools/fitness/storage
    /// </summary>
    internal static IResult HandleGetStorageInfo(ToolFitnessRagStore ragStore)
    {
        var directory = ragStore.GetStorageDirectory();
        var exists = Directory.Exists(directory);

        var files = exists
            ? Directory.GetFiles(directory).Select(f => Path.GetFileName(f)!).Where(f => f != null).ToList()
            : new List<string>();

        return Results.Ok(new
        {
            storageDirectory = directory,
            exists,
            fileCount = files.Count,
            files = files.Take(20) // Limit to prevent huge responses
        });
    }
}