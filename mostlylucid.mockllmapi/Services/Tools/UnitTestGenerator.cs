using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace mostlylucid.mockllmapi.Services.Tools;

/// <summary>
/// Unit test generator with Pyguin primary + LLM fallback strategy.
/// Automatically generates unit tests for tools and reviews code correctness.
/// </summary>
public class UnitTestGenerator
{
    private readonly ILogger<UnitTestGenerator> _logger;
    private readonly LlmClient? _llmClient;
    private readonly string _testOutputDirectory;

    public UnitTestGenerator(
        ILogger<UnitTestGenerator> logger,
        LlmClient? llmClient = null)
    {
        _logger = logger;
        _llmClient = llmClient;

        _testOutputDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LLMockApi",
            "GeneratedTests");

        Directory.CreateDirectory(_testOutputDirectory);
    }

    /// <summary>
    /// Generates unit tests for a tool using Pyguin, falling back to LLM if Pyguin fails
    /// </summary>
    public async Task<UnitTestGenerationResult> GenerateTestsAsync(
        string toolName,
        string sourceCode,
        string? existingTests = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting unit test generation for tool: {ToolName}", toolName);

        var result = new UnitTestGenerationResult
        {
            ToolName = toolName,
            StartTime = DateTimeOffset.UtcNow,
            SourceCode = sourceCode
        };

        // Step 1: Try Pyguin first
        var pyguinResult = await TryPyguinGenerationAsync(toolName, sourceCode, cancellationToken);

        if (pyguinResult.Success)
        {
            _logger.LogInformation("Pyguin successfully generated tests for: {ToolName}", toolName);
            result.Success = true;
            result.GeneratedTests = pyguinResult.GeneratedCode;
            result.GenerationMethod = "Pyguin";
            result.PyguinOutput = pyguinResult.Output;
        }
        else
        {
            _logger.LogWarning("Pyguin failed for {ToolName}, falling back to LLM. Error: {Error}",
                toolName, pyguinResult.Error);

            result.PyguinAttempted = true;
            result.PyguinError = pyguinResult.Error;

            // Step 2: Fallback to LLM generation
            if (_llmClient == null)
            {
                _logger.LogError("LLM client not available for fallback test generation");
                result.Success = false;
                result.Error = "Pyguin failed and LLM client not configured";
                result.EndTime = DateTimeOffset.UtcNow;
                return result;
            }

            var llmResult = await GenerateTestsWithLlmAsync(
                toolName,
                sourceCode,
                existingTests,
                pyguinResult.Error,
                cancellationToken);

            result.Success = llmResult.Success;
            result.GeneratedTests = llmResult.GeneratedTests;
            result.GenerationMethod = "LLM";
            result.CodeReview = llmResult.CodeReview;
            result.Error = llmResult.Error;
        }

        result.EndTime = DateTimeOffset.UtcNow;
        result.Duration = result.EndTime - result.StartTime;

        // Save generated tests to disk
        if (result.Success && !string.IsNullOrEmpty(result.GeneratedTests))
        {
            await SaveGeneratedTestsAsync(toolName, result);
        }

        return result;
    }

    /// <summary>
    /// Attempts to generate tests using Pyguin (Python-based fuzzing test generator)
    /// </summary>
    private async Task<PyguinResult> TryPyguinGenerationAsync(
        string toolName,
        string sourceCode,
        CancellationToken cancellationToken)
    {
        try
        {
            // Check if Pyguin is installed
            var pyguinCheck = await RunCommandAsync("pyguin", "--version", timeoutMs: 5000);
            if (!pyguinCheck.Success)
            {
                return new PyguinResult
                {
                    Success = false,
                    Error = "Pyguin not installed. Install with: pip install pyguin"
                };
            }

            // Save source code to temp file
            var tempDir = Path.Combine(Path.GetTempPath(), $"pyguin_test_{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);

            var sourceFile = Path.Combine(tempDir, $"{toolName}.py");
            await File.WriteAllTextAsync(sourceFile, sourceCode, cancellationToken);

            // Run Pyguin
            var pyguinCommand = $"--project-path \"{tempDir}\" --module-name {toolName} --output-path \"{tempDir}/tests\"";
            var pyguinResult = await RunCommandAsync("pyguin", pyguinCommand, timeoutMs: 60000);

            if (!pyguinResult.Success)
            {
                return new PyguinResult
                {
                    Success = false,
                    Error = $"Pyguin execution failed: {pyguinResult.Error}",
                    Output = pyguinResult.Output
                };
            }

            // Read generated test file
            var testFile = Path.Combine(tempDir, "tests", $"test_{toolName}.py");
            if (!File.Exists(testFile))
            {
                return new PyguinResult
                {
                    Success = false,
                    Error = "Pyguin did not generate test file",
                    Output = pyguinResult.Output
                };
            }

            var generatedCode = await File.ReadAllTextAsync(testFile, cancellationToken);

            // Cleanup
            try { Directory.Delete(tempDir, true); } catch { }

            return new PyguinResult
            {
                Success = true,
                GeneratedCode = generatedCode,
                Output = pyguinResult.Output
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during Pyguin test generation");
            return new PyguinResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Generates unit tests using LLM with comprehensive code review
    /// </summary>
    private async Task<LlmTestGenerationResult> GenerateTestsWithLlmAsync(
        string toolName,
        string sourceCode,
        string? existingTests,
        string? pyguinError,
        CancellationToken cancellationToken)
    {
        if (_llmClient == null)
        {
            return new LlmTestGenerationResult
            {
                Success = false,
                Error = "LLM client not configured"
            };
        }

        try
        {
            _logger.LogInformation("Generating tests with LLM for: {ToolName}", toolName);

            var prompt = BuildLlmTestGenerationPrompt(toolName, sourceCode, existingTests, pyguinError);

            // Use high token limit for comprehensive generation
            var response = await _llmClient.GetCompletionAsync(
                prompt: prompt,
                maxTokens: 8000,
                request: null);

            // Parse response
            var parsedResponse = ParseLlmTestResponse(response);

            return new LlmTestGenerationResult
            {
                Success = true,
                GeneratedTests = parsedResponse.GeneratedTests,
                CodeReview = parsedResponse.CodeReview
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM test generation failed for: {ToolName}", toolName);
            return new LlmTestGenerationResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Builds comprehensive prompt for LLM test generation
    /// </summary>
    private string BuildLlmTestGenerationPrompt(
        string toolName,
        string sourceCode,
        string? existingTests,
        string? pyguinError)
    {
        var prompt = new StringBuilder();

        prompt.AppendLine("You are an expert software testing engineer and code reviewer.");
        prompt.AppendLine();
        prompt.AppendLine("TASK:");
        prompt.AppendLine("1. Review the provided source code for correctness and potential bugs");
        prompt.AppendLine("2. Generate comprehensive unit tests");
        prompt.AppendLine();

        if (!string.IsNullOrEmpty(pyguinError))
        {
            prompt.AppendLine("CONTEXT:");
            prompt.AppendLine($"Automated test generation (Pyguin) failed with error: {pyguinError}");
            prompt.AppendLine("Your tests should cover cases that automated tools might miss.");
            prompt.AppendLine();
        }

        prompt.AppendLine($"TOOL NAME: {toolName}");
        prompt.AppendLine();
        prompt.AppendLine("SOURCE CODE:");
        prompt.AppendLine("```");
        prompt.AppendLine(sourceCode);
        prompt.AppendLine("```");
        prompt.AppendLine();

        if (!string.IsNullOrEmpty(existingTests))
        {
            prompt.AppendLine("EXISTING TESTS (for reference):");
            prompt.AppendLine("```");
            prompt.AppendLine(existingTests);
            prompt.AppendLine("```");
            prompt.AppendLine();
        }

        prompt.AppendLine("REQUIREMENTS:");
        prompt.AppendLine();
        prompt.AppendLine("1. CODE REVIEW:");
        prompt.AppendLine("   - Analyze the code for correctness");
        prompt.AppendLine("   - Identify potential bugs, edge cases, and error conditions");
        prompt.AppendLine("   - Check for security vulnerabilities");
        prompt.AppendLine("   - Verify error handling");
        prompt.AppendLine("   - Assess code quality and best practices");
        prompt.AppendLine();
        prompt.AppendLine("2. UNIT TESTS:");
        prompt.AppendLine("   - Use xUnit testing framework (C#)");
        prompt.AppendLine("   - Cover happy path scenarios");
        prompt.AppendLine("   - Cover edge cases and boundary conditions");
        prompt.AppendLine("   - Cover error/exception scenarios");
        prompt.AppendLine("   - Test null/empty inputs where applicable");
        prompt.AppendLine("   - Test concurrent execution if relevant");
        prompt.AppendLine("   - Use Moq for mocking dependencies");
        prompt.AppendLine("   - Use FluentAssertions for readable assertions");
        prompt.AppendLine("   - Each test should be independent and isolated");
        prompt.AppendLine("   - Follow AAA pattern (Arrange, Act, Assert)");
        prompt.AppendLine("   - Use descriptive test names");
        prompt.AppendLine();
        prompt.AppendLine("OUTPUT FORMAT:");
        prompt.AppendLine();
        prompt.AppendLine("# CODE REVIEW");
        prompt.AppendLine();
        prompt.AppendLine("## Overall Assessment");
        prompt.AppendLine("[Summary of code quality, correctness, and issues]");
        prompt.AppendLine();
        prompt.AppendLine("## Issues Found");
        prompt.AppendLine("1. [Issue 1 with severity: CRITICAL/HIGH/MEDIUM/LOW]");
        prompt.AppendLine("2. [Issue 2]");
        prompt.AppendLine();
        prompt.AppendLine("## Recommendations");
        prompt.AppendLine("1. [Recommendation 1]");
        prompt.AppendLine("2. [Recommendation 2]");
        prompt.AppendLine();
        prompt.AppendLine("## Test Coverage Strategy");
        prompt.AppendLine("[Explanation of what needs to be tested and why]");
        prompt.AppendLine();
        prompt.AppendLine("# GENERATED TESTS");
        prompt.AppendLine();
        prompt.AppendLine("```csharp");
        prompt.AppendLine("// Complete xUnit test class here");
        prompt.AppendLine("```");
        prompt.AppendLine();
        prompt.AppendLine("Generate the complete code review and unit tests following this format exactly.");

        return prompt.ToString();
    }

    /// <summary>
    /// Parses LLM response to extract tests and code review
    /// </summary>
    private LlmTestResponse ParseLlmTestResponse(string response)
    {
        var result = new LlmTestResponse();

        // Extract code review (everything before # GENERATED TESTS)
        var testsMarker = "# GENERATED TESTS";
        var testsIndex = response.IndexOf(testsMarker);

        if (testsIndex > 0)
        {
            result.CodeReview = response.Substring(0, testsIndex).Trim();
        }
        else
        {
            result.CodeReview = "Code review section not found in LLM response.";
        }

        // Extract generated tests (code in ```csharp blocks after # GENERATED TESTS)
        var codeBlockStart = response.IndexOf("```csharp", testsIndex >= 0 ? testsIndex : 0);
        if (codeBlockStart >= 0)
        {
            var codeStart = response.IndexOf('\n', codeBlockStart) + 1;
            var codeEnd = response.IndexOf("```", codeStart);

            if (codeEnd > codeStart)
            {
                result.GeneratedTests = response.Substring(codeStart, codeEnd - codeStart).Trim();
            }
        }

        // Fallback: try to extract any code block
        if (string.IsNullOrEmpty(result.GeneratedTests))
        {
            codeBlockStart = response.IndexOf("```");
            if (codeBlockStart >= 0)
            {
                var codeStart = response.IndexOf('\n', codeBlockStart) + 1;
                var codeEnd = response.IndexOf("```", codeStart);

                if (codeEnd > codeStart)
                {
                    result.GeneratedTests = response.Substring(codeStart, codeEnd - codeStart).Trim();
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Saves generated tests to disk
    /// </summary>
    private async Task SaveGeneratedTestsAsync(string toolName, UnitTestGenerationResult result)
    {
        try
        {
            var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
            var fileName = $"{toolName}_Tests_{timestamp}.cs";
            var filePath = Path.Combine(_testOutputDirectory, fileName);

            var content = new StringBuilder();
            content.AppendLine($"// Unit tests for: {toolName}");
            content.AppendLine($"// Generated: {result.StartTime:yyyy-MM-dd HH:mm:ss} UTC");
            content.AppendLine($"// Method: {result.GenerationMethod}");
            content.AppendLine($"// Duration: {result.Duration.TotalSeconds:F2}s");
            content.AppendLine();

            if (!string.IsNullOrEmpty(result.CodeReview))
            {
                content.AppendLine("/*");
                content.AppendLine("CODE REVIEW:");
                content.AppendLine(result.CodeReview);
                content.AppendLine("*/");
                content.AppendLine();
            }

            content.AppendLine(result.GeneratedTests);

            await File.WriteAllTextAsync(filePath, content.ToString());

            result.OutputFilePath = filePath;

            _logger.LogInformation("Saved generated tests to: {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save generated tests for: {ToolName}", toolName);
        }
    }

    /// <summary>
    /// Runs a command-line process
    /// </summary>
    private async Task<CommandResult> RunCommandAsync(
        string command,
        string arguments,
        int timeoutMs = 30000)
    {
        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = processStartInfo };

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (sender, args) =>
            {
                if (args.Data != null)
                    outputBuilder.AppendLine(args.Data);
            };

            process.ErrorDataReceived += (sender, args) =>
            {
                if (args.Data != null)
                    errorBuilder.AppendLine(args.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var completed = await Task.Run(() => process.WaitForExit(timeoutMs));

            if (!completed)
            {
                try { process.Kill(); } catch { }
                return new CommandResult
                {
                    Success = false,
                    Error = $"Command timed out after {timeoutMs}ms"
                };
            }

            var output = outputBuilder.ToString();
            var error = errorBuilder.ToString();

            return new CommandResult
            {
                Success = process.ExitCode == 0,
                Output = output,
                Error = string.IsNullOrEmpty(error) ? null : error,
                ExitCode = process.ExitCode
            };
        }
        catch (Exception ex)
        {
            return new CommandResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Gets the test output directory
    /// </summary>
    public string GetTestOutputDirectory() => _testOutputDirectory;
}

/// <summary>
/// Result of unit test generation
/// </summary>
public class UnitTestGenerationResult
{
    public string ToolName { get; set; } = string.Empty;
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public TimeSpan Duration { get; set; }
    public bool Success { get; set; }
    public string? GeneratedTests { get; set; }
    public string GenerationMethod { get; set; } = string.Empty; // "Pyguin" or "LLM"
    public string? CodeReview { get; set; }
    public string? Error { get; set; }
    public string? SourceCode { get; set; }
    public bool PyguinAttempted { get; set; }
    public string? PyguinError { get; set; }
    public string? PyguinOutput { get; set; }
    public string? OutputFilePath { get; set; }
}

/// <summary>
/// Pyguin generation result
/// </summary>
internal class PyguinResult
{
    public bool Success { get; set; }
    public string? GeneratedCode { get; set; }
    public string? Error { get; set; }
    public string? Output { get; set; }
}

/// <summary>
/// LLM test generation result
/// </summary>
internal class LlmTestGenerationResult
{
    public bool Success { get; set; }
    public string? GeneratedTests { get; set; }
    public string? CodeReview { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Parsed LLM response
/// </summary>
internal class LlmTestResponse
{
    public string CodeReview { get; set; } = string.Empty;
    public string GeneratedTests { get; set; } = string.Empty;
}

/// <summary>
/// Command execution result
/// </summary>
internal class CommandResult
{
    public bool Success { get; set; }
    public string? Output { get; set; }
    public string? Error { get; set; }
    public int ExitCode { get; set; }
}
