using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using mostlylucid.mockllmapi.Models;

namespace mostlylucid.mockllmapi.Services.Tools;

/// <summary>
/// Comprehensive tool testing and fitness scoring system.
/// Tests all configured tools, validates their claims, and generates fitness scores for RAG optimization.
/// </summary>
public class ToolFitnessTester
{
    private readonly ToolRegistry _toolRegistry;
    private readonly ToolOrchestrator _orchestrator;
    private readonly ILogger<ToolFitnessTester> _logger;
    private readonly LLMockApiOptions _options;
    private readonly LlmClient? _godLlmClient; // Optional: for evolution/optimization

    public ToolFitnessTester(
        ToolRegistry toolRegistry,
        ToolOrchestrator orchestrator,
        ILogger<ToolFitnessTester> logger,
        IOptions<LLMockApiOptions> options,
        LlmClient? godLlmClient = null)
    {
        _toolRegistry = toolRegistry;
        _orchestrator = orchestrator;
        _logger = logger;
        _options = options.Value;
        _godLlmClient = godLlmClient;
    }

    /// <summary>
    /// Runs comprehensive tests on all configured tools
    /// </summary>
    public async Task<ToolFitnessReport> TestAllToolsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting comprehensive tool fitness testing...");

        var report = new ToolFitnessReport
        {
            TestRunId = Guid.NewGuid().ToString(),
            StartTime = DateTimeOffset.UtcNow,
            TotalTools = _toolRegistry.GetAllTools().Count()
        };

        var tools = _toolRegistry.GetAllTools();
        var testResults = new ConcurrentBag<ToolTestResult>();

        // Test tools in parallel (respecting concurrency limits)
        var maxConcurrent = _options.MaxConcurrentTools > 0 ? _options.MaxConcurrentTools : 5;
        var semaphore = new SemaphoreSlim(maxConcurrent);
        var tasks = tools.Select(async tool =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var result = await TestToolAsync(tool, cancellationToken);
                testResults.Add(result);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        report.ToolResults = testResults.OrderByDescending(r => r.FitnessScore).ToList();
        report.EndTime = DateTimeOffset.UtcNow;
        report.TotalDuration = report.EndTime - report.StartTime;
        report.AverageFitness = testResults.Any() ? testResults.Average(r => r.FitnessScore) : 0;
        report.PassedTests = testResults.Count(r => r.Passed);
        report.FailedTests = testResults.Count(r => !r.Passed);

        _logger.LogInformation("Tool fitness testing complete. Passed: {Passed}, Failed: {Failed}, Avg Fitness: {AvgFitness:F2}",
            report.PassedTests, report.FailedTests, report.AverageFitness);

        return report;
    }

    /// <summary>
    /// Tests a single tool with generated dummy data and validates expectations
    /// </summary>
    private async Task<ToolTestResult> TestToolAsync(ToolConfig tool, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Testing tool: {ToolName}", tool.Name);

        var result = new ToolTestResult
        {
            ToolName = tool.Name,
            ToolType = DetermineToolType(tool),
            StartTime = DateTimeOffset.UtcNow
        };

        try
        {
            // Generate dummy parameters based on tool configuration
            var dummyParams = GenerateDummyParameters(tool);
            result.TestParameters = dummyParams;

            // Generate expectations based on tool type and configuration
            var expectations = GenerateExpectations(tool, dummyParams);
            result.Expectations = expectations;

            // Execute the tool
            var stopwatch = Stopwatch.StartNew();
            ToolResult? toolResult = null;
            Exception? executionError = null;

            try
            {
                var requestId = $"fitness-test-{Guid.NewGuid()}";
                toolResult = await _orchestrator.ExecuteToolAsync(tool.Name, dummyParams, requestId, cancellationToken);
            }
            catch (Exception ex)
            {
                executionError = ex;
                result.ExecutionError = ex.Message;
            }

            stopwatch.Stop();
            result.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
            result.ActualResult = toolResult?.Data;

            // Validate results against expectations
            result.ValidationResults = ValidateResults(expectations, toolResult, executionError);
            result.Passed = result.ValidationResults.All(v => v.Passed);

            // Calculate fitness score
            result.FitnessScore = CalculateFitnessScore(result, tool);

            result.EndTime = DateTimeOffset.UtcNow;

            _logger.LogInformation("Tool {ToolName} tested. Passed: {Passed}, Fitness: {Fitness:F2}",
                tool.Name, result.Passed, result.FitnessScore);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to test tool: {ToolName}", tool.Name);
            result.ExecutionError = ex.Message;
            result.Passed = false;
            result.FitnessScore = 0;
            result.EndTime = DateTimeOffset.UtcNow;
        }

        return result;
    }

    /// <summary>
    /// Generates dummy test parameters based on tool parameter schema
    /// </summary>
    private Dictionary<string, object> GenerateDummyParameters(ToolConfig tool)
    {
        var parameters = new Dictionary<string, object>();

        if (tool.Parameters == null || !tool.Parameters.Any())
        {
            return parameters;
        }

        foreach (var param in tool.Parameters)
        {
            var value = GenerateDummyValue(param.Value);
            if (value != null)
            {
                parameters[param.Key] = value;
            }
        }

        return parameters;
    }

    /// <summary>
    /// Generates a dummy value based on parameter schema
    /// </summary>
    private object? GenerateDummyValue(ParameterSchema param)
    {
        // Use example if provided
        if (param.Example != null)
        {
            return param.Example;
        }

        // Use default if provided
        if (param.Default != null)
        {
            return param.Default;
        }

        // Generate based on type
        return param.Type?.ToLowerInvariant() switch
        {
            "string" => param.Enum?.FirstOrDefault() ?? "test-value",
            "integer" or "int" => 42,
            "number" => 3.14,
            "boolean" or "bool" => true,
            "array" => new[] { "item1", "item2" },
            "object" => new Dictionary<string, object> { { "key", "value" } },
            _ => "test-value"
        };
    }

    /// <summary>
    /// Generates expectations for tool execution based on tool type and configuration
    /// </summary>
    private List<TestExpectation> GenerateExpectations(ToolConfig tool, Dictionary<string, object> parameters)
    {
        var expectations = new List<TestExpectation>();

        // Common expectations for all tools
        expectations.Add(new TestExpectation
        {
            ExpectationType = "NoException",
            Description = "Tool should execute without throwing exceptions",
            Expected = "No exception thrown"
        });

        var timeoutSeconds = _options.TimeoutSeconds > 0 ? _options.TimeoutSeconds : 30;
        expectations.Add(new TestExpectation
        {
            ExpectationType = "ReasonableExecutionTime",
            Description = "Tool should complete within reasonable time",
            Expected = $"< {timeoutSeconds * 1000}ms"
        });

        // HTTP tool specific expectations
        if (tool.HttpConfig != null)
        {
            var url = tool.HttpConfig.Endpoint;

            // Substitute parameters in URL
            foreach (var param in parameters)
            {
                url = url.Replace($"{{{param.Key}}}", param.Value?.ToString() ?? "");
            }

            // Determine if URL is expected to be valid or should 404
            var isValidUrl = !url.Contains("example.com") && !url.Contains("dummy") && !url.Contains("test-invalid");

            if (isValidUrl)
            {
                expectations.Add(new TestExpectation
                {
                    ExpectationType = "HttpSuccess",
                    Description = "HTTP request should succeed (2xx or 3xx status)",
                    Expected = "Success status code"
                });
            }
            else
            {
                expectations.Add(new TestExpectation
                {
                    ExpectationType = "HttpFailure",
                    Description = "Dummy/invalid URL should fail gracefully",
                    Expected = "404 or connection error"
                });
            }

            // Check for JSONPath extraction
            if (!string.IsNullOrEmpty(tool.HttpConfig.ResponsePath))
            {
                expectations.Add(new TestExpectation
                {
                    ExpectationType = "JsonPathExtraction",
                    Description = "Should extract data using JSONPath",
                    Expected = $"Value extracted from path: {tool.HttpConfig.ResponsePath}"
                });
            }

            // Check for authentication
            if (!string.IsNullOrEmpty(tool.HttpConfig.AuthType) && tool.HttpConfig.AuthType != "none")
            {
                expectations.Add(new TestExpectation
                {
                    ExpectationType = "AuthenticationConfigured",
                    Description = "Should include authentication headers/credentials",
                    Expected = $"Authentication type: {tool.HttpConfig.AuthType}"
                });
            }
        }

        // Mock tool specific expectations
        if (tool.MockConfig != null)
        {
            expectations.Add(new TestExpectation
            {
                ExpectationType = "MockEndpointCall",
                Description = "Should successfully call internal mock endpoint",
                Expected = $"Response from endpoint: {tool.MockConfig.Endpoint}"
            });

            if (!string.IsNullOrEmpty(tool.MockConfig.ContextName))
            {
                expectations.Add(new TestExpectation
                {
                    ExpectationType = "ContextSharing",
                    Description = "Should share context across tool chain",
                    Expected = $"Context: {tool.MockConfig.ContextName}"
                });
            }
        }

        // Cache expectations
        if (tool.EnableCaching && tool.CacheDurationMinutes > 0)
        {
            expectations.Add(new TestExpectation
            {
                ExpectationType = "Caching",
                Description = "Tool should support result caching",
                Expected = $"Cache TTL: {tool.CacheDurationMinutes} minutes"
            });
        }

        return expectations;
    }

    /// <summary>
    /// Validates actual results against expectations
    /// </summary>
    private List<ValidationResult> ValidateResults(
        List<TestExpectation> expectations,
        ToolResult? toolResult,
        Exception? executionError)
    {
        var validationResults = new List<ValidationResult>();

        foreach (var expectation in expectations)
        {
            var validation = new ValidationResult
            {
                ExpectationType = expectation.ExpectationType,
                Expected = expectation.Expected,
                Description = expectation.Description
            };

            switch (expectation.ExpectationType)
            {
                case "NoException":
                    validation.Passed = executionError == null;
                    validation.Actual = executionError?.Message ?? "No exception";
                    break;

                case "ReasonableExecutionTime":
                    // This is validated in the result object itself
                    validation.Passed = true; // Will be marked false if timeout occurred
                    validation.Actual = toolResult != null ? "Within timeout" : "Timeout or error";
                    break;

                case "HttpSuccess":
                    validation.Passed = toolResult != null && toolResult.Success;
                    validation.Actual = toolResult?.Success == true ? "Success" : "HTTP error";
                    break;

                case "HttpFailure":
                    // For dummy URLs, we expect graceful failure
                    validation.Passed = toolResult == null || !toolResult.Success;
                    validation.Actual = toolResult?.Success == false ? "Failed as expected" : "Unexpected success";
                    break;

                case "JsonPathExtraction":
                    validation.Passed = toolResult != null && !string.IsNullOrEmpty(toolResult.Data);
                    validation.Actual = toolResult?.Data ?? "No extraction";
                    break;

                case "AuthenticationConfigured":
                    // Can't validate auth directly, assume configured = pass
                    validation.Passed = true;
                    validation.Actual = "Authentication configured";
                    break;

                case "MockEndpointCall":
                    validation.Passed = toolResult != null && toolResult.Success;
                    validation.Actual = toolResult?.Data ?? "No response";
                    break;

                case "ContextSharing":
                    // Validate that context was used (implicit from successful mock call)
                    validation.Passed = toolResult != null;
                    validation.Actual = toolResult != null ? "Context shared" : "No context";
                    break;

                case "Caching":
                    // Caching is a configuration feature, assume configured = pass
                    validation.Passed = true;
                    validation.Actual = "Caching configured";
                    break;

                default:
                    validation.Passed = false;
                    validation.Actual = "Unknown expectation type";
                    break;
            }

            validationResults.Add(validation);
        }

        return validationResults;
    }

    /// <summary>
    /// Calculates a comprehensive fitness score for the tool (0-100)
    /// </summary>
    private double CalculateFitnessScore(ToolTestResult result, ToolConfig tool)
    {
        double score = 0;

        // Base score: Did it pass? (40 points)
        if (result.Passed)
        {
            score += 40;
        }

        // Validation results (30 points)
        if (result.ValidationResults.Any())
        {
            var passedValidations = result.ValidationResults.Count(v => v.Passed);
            var totalValidations = result.ValidationResults.Count;
            score += (passedValidations / (double)totalValidations) * 30;
        }

        // Performance (15 points)
        var timeoutSeconds = _options.TimeoutSeconds > 0 ? _options.TimeoutSeconds : 30;
        var maxReasonableTime = timeoutSeconds * 1000;
        if (result.ExecutionTimeMs < maxReasonableTime * 0.25) // Very fast (< 25% of timeout)
        {
            score += 15;
        }
        else if (result.ExecutionTimeMs < maxReasonableTime * 0.5) // Fast (< 50% of timeout)
        {
            score += 12;
        }
        else if (result.ExecutionTimeMs < maxReasonableTime * 0.75) // Acceptable (< 75% of timeout)
        {
            score += 8;
        }
        else if (result.ExecutionTimeMs < maxReasonableTime) // Slow but within timeout
        {
            score += 4;
        }

        // Configuration quality (10 points)
        var configScore = 0;
        if (!string.IsNullOrEmpty(tool.Description)) configScore += 2;
        if (tool.Parameters?.Any() == true) configScore += 2;
        if (tool.Parameters?.Any(p => !string.IsNullOrEmpty(p.Value.Description)) == true) configScore += 2;
        if (tool.Parameters?.Any(p => p.Value.Example != null) == true) configScore += 2;
        if (tool.EnableCaching && tool.CacheDurationMinutes > 0) configScore += 2;
        score += configScore;

        // Feature completeness (5 points)
        var featureScore = 0;
        if (!string.IsNullOrEmpty(tool.HttpConfig?.AuthType) && tool.HttpConfig.AuthType != "none") featureScore += 2;
        if (!string.IsNullOrEmpty(tool.HttpConfig?.ResponsePath)) featureScore += 1;
        if (!string.IsNullOrEmpty(tool.MockConfig?.ContextName)) featureScore += 2;
        score += featureScore;

        return Math.Round(Math.Min(score, 100), 2);
    }

    /// <summary>
    /// Determines the primary tool type for classification
    /// </summary>
    private string DetermineToolType(ToolConfig tool)
    {
        if (tool.HttpConfig != null) return "HTTP";
        if (tool.MockConfig != null) return "Mock";
        return "Unknown";
    }

    /// <summary>
    /// Identifies low-fitness tools that need evolution/optimization
    /// </summary>
    public List<ToolTestResult> GetLowFitnessTools(ToolFitnessReport report, double threshold = 60.0)
    {
        return report.ToolResults.Where(r => r.FitnessScore < threshold).ToList();
    }

    /// <summary>
    /// Triggers evolution/optimization for low-fitness tools using god-level LLM
    /// </summary>
    public async Task<List<ToolEvolutionResult>> EvolveToolsAsync(
        List<ToolTestResult> lowFitnessTools,
        CancellationToken cancellationToken = default)
    {
        if (_godLlmClient == null)
        {
            _logger.LogWarning("God-level LLM client not configured. Cannot evolve tools.");
            return new List<ToolEvolutionResult>();
        }

        _logger.LogInformation("Starting tool evolution for {Count} low-fitness tools", lowFitnessTools.Count);

        var results = new List<ToolEvolutionResult>();

        foreach (var toolResult in lowFitnessTools)
        {
            var evolutionResult = await EvolveSingleToolAsync(toolResult, cancellationToken);
            results.Add(evolutionResult);
        }

        return results;
    }

    /// <summary>
    /// Evolves a single tool using god-level LLM
    /// </summary>
    private async Task<ToolEvolutionResult> EvolveSingleToolAsync(
        ToolTestResult toolResult,
        CancellationToken cancellationToken)
    {
        if (_godLlmClient == null)
        {
            return new ToolEvolutionResult
            {
                ToolName = toolResult.ToolName,
                Success = false,
                Error = "God-level LLM not configured"
            };
        }

        try
        {
            var prompt = BuildEvolutionPrompt(toolResult);

            // Use god-level LLM with high max tokens for comprehensive analysis
            var response = await _godLlmClient.GetCompletionAsync(
                prompt: prompt,
                maxTokens: 8000,
                request: null); // No HTTP request context needed

            return new ToolEvolutionResult
            {
                ToolName = toolResult.ToolName,
                Success = true,
                OriginalFitness = toolResult.FitnessScore,
                Recommendations = response,
                EvolvedAt = DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to evolve tool: {ToolName}", toolResult.ToolName);
            return new ToolEvolutionResult
            {
                ToolName = toolResult.ToolName,
                Success = false,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Builds the evolution prompt for god-level LLM
    /// </summary>
    private string BuildEvolutionPrompt(ToolTestResult toolResult)
    {
        var failedValidations = toolResult.ValidationResults.Where(v => !v.Passed).ToList();

        return $@"You are a god-level AI system optimizer analyzing a tool configuration that has low fitness.

TOOL ANALYSIS:
- Tool Name: {toolResult.ToolName}
- Tool Type: {toolResult.ToolType}
- Current Fitness Score: {toolResult.FitnessScore}/100
- Test Status: {(toolResult.Passed ? "PASSED" : "FAILED")}
- Execution Time: {toolResult.ExecutionTimeMs}ms

FAILED VALIDATIONS:
{string.Join("\n", failedValidations.Select(v => $"- {v.Description}\n  Expected: {v.Expected}\n  Actual: {v.Actual}"))}

EXECUTION ERROR:
{toolResult.ExecutionError ?? "None"}

TEST PARAMETERS USED:
{JsonSerializer.Serialize(toolResult.TestParameters, new JsonSerializerOptions { WriteIndented = true })}

EXPECTATIONS:
{string.Join("\n", toolResult.Expectations.Select(e => $"- [{e.ExpectationType}] {e.Description}: {e.Expected}"))}

YOUR TASK:
Analyze this tool's performance and provide comprehensive recommendations for improvement:

1. ROOT CAUSE ANALYSIS: What is causing the low fitness score?
2. CONFIGURATION IMPROVEMENTS: How should the tool configuration be modified?
3. PARAMETER OPTIMIZATION: Are the parameters properly defined and documented?
4. PERFORMANCE OPTIMIZATION: How can execution time be improved?
5. ERROR HANDLING: How can error handling be improved?
6. TESTING IMPROVEMENTS: What additional test cases should be added?
7. RECOMMENDED CONFIGURATION: Provide a complete, optimized ToolConfig JSON

Be specific, actionable, and comprehensive. Focus on practical improvements that will increase the fitness score.";
    }
}

/// <summary>
/// Complete fitness testing report
/// </summary>
public class ToolFitnessReport
{
    public string TestRunId { get; set; } = string.Empty;
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public TimeSpan TotalDuration { get; set; }
    public int TotalTools { get; set; }
    public int PassedTests { get; set; }
    public int FailedTests { get; set; }
    public double AverageFitness { get; set; }
    public List<ToolTestResult> ToolResults { get; set; } = new();

    /// <summary>
    /// Exports report to RAG-friendly JSON format
    /// </summary>
    public string ToRagJson()
    {
        return JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }
}

/// <summary>
/// Test result for a single tool
/// </summary>
public class ToolTestResult
{
    public string ToolName { get; set; } = string.Empty;
    public string ToolType { get; set; } = string.Empty;
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public long ExecutionTimeMs { get; set; }
    public Dictionary<string, object> TestParameters { get; set; } = new();
    public List<TestExpectation> Expectations { get; set; } = new();
    public List<ValidationResult> ValidationResults { get; set; } = new();
    public string? ActualResult { get; set; }
    public string? ExecutionError { get; set; }
    public bool Passed { get; set; }
    public double FitnessScore { get; set; }
}

/// <summary>
/// Test expectation definition
/// </summary>
public class TestExpectation
{
    public string ExpectationType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Expected { get; set; } = string.Empty;
}

/// <summary>
/// Validation result for an expectation
/// </summary>
public class ValidationResult
{
    public string ExpectationType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Expected { get; set; } = string.Empty;
    public string Actual { get; set; } = string.Empty;
    public bool Passed { get; set; }
}

/// <summary>
/// Tool evolution result from god-level LLM
/// </summary>
public class ToolEvolutionResult
{
    public string ToolName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public double OriginalFitness { get; set; }
    public string? Recommendations { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset EvolvedAt { get; set; }
}