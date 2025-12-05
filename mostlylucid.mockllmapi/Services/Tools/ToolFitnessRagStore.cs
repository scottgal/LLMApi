using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Text.Json;

namespace mostlylucid.mockllmapi.Services.Tools;

/// <summary>
/// Storage and retrieval system for tool fitness data in RAG-optimized format.
/// Supports querying, filtering, and exporting for vector embedding and LLM consumption.
/// </summary>
public class ToolFitnessRagStore
{
    private readonly ILogger<ToolFitnessRagStore> _logger;
    private readonly LLMockApiOptions _options;
    private readonly ConcurrentDictionary<string, List<ToolFitnessSnapshot>> _fitnessHistory;
    private readonly string _storageDirectory;

    public ToolFitnessRagStore(
        ILogger<ToolFitnessRagStore> logger,
        IOptions<LLMockApiOptions> options)
    {
        _logger = logger;
        _options = options.Value;
        _fitnessHistory = new ConcurrentDictionary<string, List<ToolFitnessSnapshot>>();

        // Default storage directory
        _storageDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LLMockApi",
            "ToolFitness");

        Directory.CreateDirectory(_storageDirectory);
    }

    /// <summary>
    /// Stores a complete fitness report
    /// </summary>
    public async Task StoreReportAsync(ToolFitnessReport report)
    {
        _logger.LogInformation("Storing fitness report: {TestRunId}", report.TestRunId);

        // Update in-memory history
        foreach (var toolResult in report.ToolResults)
        {
            var snapshot = new ToolFitnessSnapshot
            {
                ToolName = toolResult.ToolName,
                ToolType = toolResult.ToolType,
                Timestamp = report.StartTime,
                TestRunId = report.TestRunId,
                FitnessScore = toolResult.FitnessScore,
                Passed = toolResult.Passed,
                ExecutionTimeMs = toolResult.ExecutionTimeMs,
                ValidationsPassed = toolResult.ValidationResults.Count(v => v.Passed),
                ValidationsTotal = toolResult.ValidationResults.Count,
                ExecutionError = toolResult.ExecutionError,
                TestParameters = toolResult.TestParameters,
                FailedValidations = toolResult.ValidationResults
                    .Where(v => !v.Passed)
                    .Select(v => $"{v.ExpectationType}: {v.Description}")
                    .ToList()
            };

            _fitnessHistory.AddOrUpdate(
                toolResult.ToolName,
                new List<ToolFitnessSnapshot> { snapshot },
                (key, existing) =>
                {
                    existing.Add(snapshot);
                    // Keep last 100 snapshots per tool
                    return existing.TakeLast(100).ToList();
                });
        }

        // Persist to disk
        await PersistReportAsync(report);

        // Export RAG-optimized documents
        await ExportRagDocumentsAsync(report);
    }

    /// <summary>
    /// Persists the full report to disk as JSON
    /// </summary>
    private async Task PersistReportAsync(ToolFitnessReport report)
    {
        var fileName = $"fitness_report_{report.TestRunId}_{report.StartTime:yyyyMMdd_HHmmss}.json";
        var filePath = Path.Combine(_storageDirectory, fileName);

        try
        {
            var json = report.ToRagJson();
            await File.WriteAllTextAsync(filePath, json);
            _logger.LogInformation("Persisted fitness report to: {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist fitness report to disk");
        }
    }

    /// <summary>
    /// Exports RAG-optimized documents for vector embedding and semantic search
    /// </summary>
    private async Task ExportRagDocumentsAsync(ToolFitnessReport report)
    {
        var ragDocs = new List<ToolRagDocument>();

        foreach (var toolResult in report.ToolResults)
        {
            var doc = new ToolRagDocument
            {
                DocumentId = $"{toolResult.ToolName}_{report.TestRunId}",
                ToolName = toolResult.ToolName,
                ToolType = toolResult.ToolType,
                Timestamp = report.StartTime,
                FitnessScore = toolResult.FitnessScore,
                Passed = toolResult.Passed,

                // Semantic text for embedding
                SemanticText = BuildSemanticText(toolResult),

                // Structured metadata for filtering
                Metadata = new Dictionary<string, object>
                {
                    { "toolName", toolResult.ToolName },
                    { "toolType", toolResult.ToolType },
                    { "fitnessScore", toolResult.FitnessScore },
                    { "passed", toolResult.Passed },
                    { "executionTimeMs", toolResult.ExecutionTimeMs },
                    { "testRunId", report.TestRunId },
                    { "timestamp", report.StartTime.ToString("O") }
                },

                // Full test result for retrieval
                FullTestResult = toolResult
            };

            ragDocs.Add(doc);
        }

        // Export to JSONL format (one JSON object per line - standard RAG format)
        var jsonlFileName = $"rag_documents_{report.TestRunId}_{report.StartTime:yyyyMMdd_HHmmss}.jsonl";
        var jsonlPath = Path.Combine(_storageDirectory, jsonlFileName);

        try
        {
            await using var writer = new StreamWriter(jsonlPath);
            foreach (var doc in ragDocs)
            {
                var json = JsonSerializer.Serialize(doc, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                await writer.WriteLineAsync(json);
            }

            _logger.LogInformation("Exported {Count} RAG documents to: {FilePath}", ragDocs.Count, jsonlPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export RAG documents");
        }
    }

    /// <summary>
    /// Builds semantic text for vector embedding
    /// </summary>
    private string BuildSemanticText(ToolTestResult toolResult)
    {
        var text = $"Tool: {toolResult.ToolName}\n";
        text += $"Type: {toolResult.ToolType}\n";
        text += $"Fitness Score: {toolResult.FitnessScore}/100\n";
        text += $"Status: {(toolResult.Passed ? "PASSED" : "FAILED")}\n";
        text += $"Execution Time: {toolResult.ExecutionTimeMs}ms\n\n";

        // Add failed validations (most semantically important)
        var failedValidations = toolResult.ValidationResults.Where(v => !v.Passed).ToList();
        if (failedValidations.Any())
        {
            text += "Failed Validations:\n";
            foreach (var validation in failedValidations)
            {
                text += $"- {validation.Description}\n";
                text += $"  Expected: {validation.Expected}\n";
                text += $"  Actual: {validation.Actual}\n";
            }
            text += "\n";
        }

        // Add execution error
        if (!string.IsNullOrEmpty(toolResult.ExecutionError))
        {
            text += $"Execution Error: {toolResult.ExecutionError}\n\n";
        }

        // Add passed validations summary
        var passedCount = toolResult.ValidationResults.Count(v => v.Passed);
        text += $"Passed Validations: {passedCount}/{toolResult.ValidationResults.Count}\n";

        return text;
    }

    /// <summary>
    /// Retrieves fitness history for a specific tool
    /// </summary>
    public List<ToolFitnessSnapshot> GetToolHistory(string toolName, int maxResults = 10)
    {
        if (_fitnessHistory.TryGetValue(toolName, out var history))
        {
            return history.TakeLast(maxResults).ToList();
        }

        return new List<ToolFitnessSnapshot>();
    }

    /// <summary>
    /// Gets all tools with fitness below threshold
    /// </summary>
    public Dictionary<string, ToolFitnessSnapshot> GetLowFitnessTools(double threshold = 60.0)
    {
        var result = new Dictionary<string, ToolFitnessSnapshot>();

        foreach (var (toolName, history) in _fitnessHistory)
        {
            var latest = history.LastOrDefault();
            if (latest != null && latest.FitnessScore < threshold)
            {
                result[toolName] = latest;
            }
        }

        return result;
    }

    /// <summary>
    /// Gets tools with improving fitness trends
    /// </summary>
    public Dictionary<string, FitnessTrend> GetFitnessTrends(int minSnapshots = 3)
    {
        var trends = new Dictionary<string, FitnessTrend>();

        foreach (var (toolName, history) in _fitnessHistory)
        {
            if (history.Count < minSnapshots)
                continue;

            var recent = history.TakeLast(minSnapshots).ToList();
            var firstScore = recent.First().FitnessScore;
            var lastScore = recent.Last().FitnessScore;
            var change = lastScore - firstScore;

            trends[toolName] = new FitnessTrend
            {
                ToolName = toolName,
                InitialScore = firstScore,
                CurrentScore = lastScore,
                Change = change,
                Direction = change > 0 ? "Improving" : change < 0 ? "Declining" : "Stable",
                Snapshots = recent.Count
            };
        }

        return trends;
    }

    /// <summary>
    /// Exports complete fitness history to JSON for backup/analysis
    /// </summary>
    public async Task<string> ExportFullHistoryAsync()
    {
        var fileName = $"full_fitness_history_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}.json";
        var filePath = Path.Combine(_storageDirectory, fileName);

        var export = new
        {
            ExportedAt = DateTimeOffset.UtcNow,
            TotalTools = _fitnessHistory.Count,
            TotalSnapshots = _fitnessHistory.Values.Sum(h => h.Count),
            History = _fitnessHistory.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value)
        };

        var json = JsonSerializer.Serialize(export, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await File.WriteAllTextAsync(filePath, json);
        _logger.LogInformation("Exported full fitness history to: {FilePath}", filePath);

        return filePath;
    }

    /// <summary>
    /// Loads fitness history from disk
    /// </summary>
    public async Task LoadHistoryAsync()
    {
        _logger.LogInformation("Loading fitness history from: {Directory}", _storageDirectory);

        if (!Directory.Exists(_storageDirectory))
        {
            _logger.LogWarning("Storage directory does not exist: {Directory}", _storageDirectory);
            return;
        }

        var reportFiles = Directory.GetFiles(_storageDirectory, "fitness_report_*.json");

        foreach (var file in reportFiles.OrderBy(f => f))
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var report = JsonSerializer.Deserialize<ToolFitnessReport>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (report != null)
                {
                    // Re-add to in-memory cache
                    await StoreReportAsync(report);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load fitness report from: {File}", file);
            }
        }

        _logger.LogInformation("Loaded fitness history for {Count} tools", _fitnessHistory.Count);
    }

    /// <summary>
    /// Gets the storage directory path
    /// </summary>
    public string GetStorageDirectory() => _storageDirectory;
}

/// <summary>
/// Snapshot of tool fitness at a point in time
/// </summary>
public class ToolFitnessSnapshot
{
    public string ToolName { get; set; } = string.Empty;
    public string ToolType { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public string TestRunId { get; set; } = string.Empty;
    public double FitnessScore { get; set; }
    public bool Passed { get; set; }
    public long ExecutionTimeMs { get; set; }
    public int ValidationsPassed { get; set; }
    public int ValidationsTotal { get; set; }
    public string? ExecutionError { get; set; }
    public Dictionary<string, object> TestParameters { get; set; } = new();
    public List<string> FailedValidations { get; set; } = new();
}

/// <summary>
/// RAG-optimized document for vector embedding
/// </summary>
public class ToolRagDocument
{
    public string DocumentId { get; set; } = string.Empty;
    public string ToolName { get; set; } = string.Empty;
    public string ToolType { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public double FitnessScore { get; set; }
    public bool Passed { get; set; }

    /// <summary>
    /// Human-readable text optimized for semantic embedding
    /// </summary>
    public string SemanticText { get; set; } = string.Empty;

    /// <summary>
    /// Structured metadata for filtering and retrieval
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();

    /// <summary>
    /// Full test result for detailed analysis
    /// </summary>
    public ToolTestResult? FullTestResult { get; set; }
}

/// <summary>
/// Fitness trend analysis
/// </summary>
public class FitnessTrend
{
    public string ToolName { get; set; } = string.Empty;
    public double InitialScore { get; set; }
    public double CurrentScore { get; set; }
    public double Change { get; set; }
    public string Direction { get; set; } = string.Empty;
    public int Snapshots { get; set; }
}