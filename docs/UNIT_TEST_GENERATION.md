# Unit Test Generation with Pyguin + LLM Fallback

## Overview

The Unit Test Generation system provides automated test creation with a dual-strategy approach:

1. **Primary**: Pyguin (Python fuzzing-based test generator) for fast, automated coverage
2. **Fallback**: LLM-powered generation with comprehensive code review when Pyguin fails

This ensures you always get high-quality unit tests, even for complex code that automated tools struggle with.

## Quick Start

### Generate Tests for a Tool

**Via HTTP:**
```http
POST /api/tools/generate-tests
Content-Type: application/json

{
  "toolName": "WeatherApiTool",
  "sourceCode": "using System;\n\npublic class WeatherApi {\n  public string GetWeather(string city) {\n    return $\"Weather for {city}\";\n  }\n}",
  "existingTests": null
}
```

**Response:**
```json
{
  "success": true,
  "toolName": "WeatherApiTool",
  "generationMethod": "LLM",
  "duration": 3.45,
  "generatedTests": "using Xunit;\nusing FluentAssertions;\n\npublic class WeatherApiTests {\n  ...",
  "codeReview": "# CODE REVIEW\n\n## Overall Assessment\nThe code is simple and functional...",
  "outputFilePath": "C:\\Users\\...\\LLMockApi\\GeneratedTests\\WeatherApiTool_Tests_20250117_103000.cs",
  "pyguinAttempted": true,
  "pyguinError": "Pyguin not installed"
}
```

### Review Code Without Generating Tests

```http
POST /api/tools/review-code
Content-Type: application/json

{
  "name": "MyComponent",
  "sourceCode": "public class MyComponent { ... }"
}
```

## Architecture

### Strategy Flow

```
1. Receive sourceCode
     ↓
2. Try Pyguin generation
     ├─ Success → Return Pyguin tests
     └─ Failure → Fall back to LLM
          ├─ Generate comprehensive prompt
          ├─ Include Pyguin error context
          ├─ Request code review + tests
          └─ Return LLM-generated tests + review
```

### Key Components

#### 1. **UnitTestGenerator** (`Services/Tools/UnitTestGenerator.cs`)

Main orchestrator that:
- Attempts Pyguin generation first
- Falls back to LLM on Pyguin failure
- Manages test output directory
- Saves generated tests to disk with metadata

#### 2. **Pyguin Integration**

**Requirements:**
```bash
pip install pyguin
```

**How it works:**
1. Saves source code to temp Python file
2. Runs `pyguin --project-path <dir> --module-name <name> --output-path <dir>/tests`
3. Reads generated `test_<name>.py`
4. Returns generated test code

**Timeout**: 60 seconds (configurable)

#### 3. **LLM Fallback**

When Pyguin fails (not installed, timeout, or generation error), the system:

1. Builds comprehensive prompt with:
   - Source code
   - Existing tests (if any)
   - Pyguin error message
   - Detailed test requirements
   - Code review requirements

2. Calls LLM with high token limit (8000 tokens)

3. Parses structured response:
   - **Code Review** section (bugs, security, recommendations)
   - **Generated Tests** section (complete xUnit test class)

### Generated Test Structure

Tests are saved to: `%LocalAppData%\LLMockApi\GeneratedTests\`

**File format:**
```csharp
// Unit tests for: WeatherApiTool
// Generated: 2025-01-17 10:30:00 UTC
// Method: LLM
// Duration: 3.45s

/*
CODE REVIEW:
# CODE REVIEW

## Overall Assessment
The code is simple and functional but lacks error handling...

## Issues Found
1. [HIGH] No null check for city parameter
2. [MEDIUM] No validation for empty strings

## Recommendations
1. Add null/empty validation
2. Add try-catch for network errors
3. Add timeout configuration

## Test Coverage Strategy
We need to test:
- Happy path with valid city
- Null city input
- Empty string input
- Special characters in city name
*/

using Xunit;
using Moq;
using FluentAssertions;

namespace WeatherApiTool.Tests
{
    public class WeatherApiTests
    {
        [Fact]
        public void GetWeather_ValidCity_ReturnsWeatherString()
        {
            // Arrange
            var api = new WeatherApi();
            var city = "London";

            // Act
            var result = api.GetWeather(city);

            // Assert
            result.Should().Contain("London");
        }

        [Fact]
        public void GetWeather_NullCity_ThrowsArgumentNullException()
        {
            // Arrange
            var api = new WeatherApi();

            // Act
            Action act = () => api.GetWeather(null);

            // Assert
            act.Should().Throw<ArgumentNullException>();
        }

        // ... more tests
    }
}
```

## LLM Prompt Structure

The system generates a comprehensive prompt that includes:

### 1. Context Setting
```
You are an expert software testing engineer and code reviewer.

TASK:
1. Review the provided source code for correctness and potential bugs
2. Generate comprehensive unit tests
```

### 2. Pyguin Failure Context (if applicable)
```
CONTEXT:
Automated test generation (Pyguin) failed with error: {error}
Your tests should cover cases that automated tools might miss.
```

### 3. Source Code
```
TOOL NAME: {toolName}

SOURCE CODE:
```csharp
{sourceCode}
```
```

### 4. Existing Tests (if provided)
```
EXISTING TESTS (for reference):
```csharp
{existingTests}
```
```

### 5. Requirements

**Code Review:**
- Analyze for correctness
- Identify bugs, edge cases, errors
- Check security vulnerabilities
- Verify error handling
- Assess code quality

**Unit Tests:**
- Use xUnit framework
- Cover happy path
- Cover edge cases and boundaries
- Cover error/exception scenarios
- Test null/empty inputs
- Test concurrent execution
- Use Moq for mocking
- Use FluentAssertions
- AAA pattern (Arrange, Act, Assert)
- Descriptive test names

### 6. Output Format

```
# CODE REVIEW

## Overall Assessment
[Summary]

## Issues Found
1. [CRITICAL/HIGH/MEDIUM/LOW] Issue description

## Recommendations
1. Recommendation

## Test Coverage Strategy
[What to test and why]

# GENERATED TESTS

```csharp
// Complete xUnit test class
```
```

## API Reference

### POST /api/tools/generate-tests

Generate unit tests with Pyguin (primary) + LLM (fallback).

**Request:**
```json
{
  "toolName": "string (required)",
  "sourceCode": "string (required)",
  "existingTests": "string (optional)"
}
```

**Response (Success):**
```json
{
  "success": true,
  "toolName": "WeatherApiTool",
  "generationMethod": "LLM" | "Pyguin",
  "duration": 3.45,
  "generatedTests": "complete test code",
  "codeReview": "comprehensive review (LLM only)",
  "outputFilePath": "path to saved file",
  "pyguinAttempted": true,
  "pyguinError": "error message if Pyguin failed"
}
```

**Response (Failure):**
```json
{
  "success": false,
  "toolName": "WeatherApiTool",
  "error": "error description",
  "pyguinAttempted": true,
  "pyguinError": "Pyguin error"
}
```

### POST /api/tools/review-code

Review code for correctness using LLM (no test generation via Pyguin).

**Request:**
```json
{
  "name": "string (optional)",
  "sourceCode": "string (required)"
}
```

**Response:**
```json
{
  "success": true,
  "codeReview": "# CODE REVIEW\n\n## Overall Assessment\n...",
  "generatedTests": "tests also generated as bonus"
}
```

### GET /api/tools/test-output

Get information about generated test files.

**Response:**
```json
{
  "outputDirectory": "C:\\Users\\...\\LLMockApi\\GeneratedTests",
  "exists": true,
  "fileCount": 15,
  "files": [
    {
      "fileName": "WeatherApiTool_Tests_20250117_103000.cs",
      "created": "2025-01-17T10:30:00Z",
      "size": 4532
    }
  ]
}
```

### GET /api/tools/test-output/{fileName}

Download a specific generated test file.

**Response:** File download (text/plain)

## Usage Examples

### Example 1: Generate Tests for HTTP Tool

```http
POST /api/tools/generate-tests
Content-Type: application/json

{
  "toolName": "WeatherHttpTool",
  "sourceCode": "using System;\nusing System.Net.Http;\nusing System.Threading.Tasks;\n\npublic class WeatherHttpTool\n{\n    private readonly HttpClient _client;\n\n    public WeatherHttpTool(HttpClient client)\n    {\n        _client = client ?? throw new ArgumentNullException(nameof(client));\n    }\n\n    public async Task<string> GetWeatherAsync(string city)\n    {\n        var response = await _client.GetStringAsync($\"https://api.weather.com/v1/{city}\");\n        return response;\n    }\n}"
}
```

**Expected Result:**
- Pyguin will likely fail (C# code, not Python)
- LLM will generate comprehensive tests including:
  - Null HttpClient test
  - Valid city test
  - Null/empty city test
  - HTTP error handling test
  - Timeout test
  - Concurrent requests test

### Example 2: Review Existing Code

```http
POST /api/tools/review-code
Content-Type: application/json

{
  "name": "UserAuthentication",
  "sourceCode": "public class UserAuth {\n  public bool ValidatePassword(string password) {\n    return password.Length > 6;\n  }\n}"
}
```

**Expected Code Review:**
```
# CODE REVIEW

## Overall Assessment
Critical security vulnerabilities found. The password validation is extremely weak.

## Issues Found
1. [CRITICAL] Password validation only checks length, no complexity requirements
2. [HIGH] No protection against timing attacks
3. [HIGH] No rate limiting consideration
4. [MEDIUM] Magic number (6) should be configurable

## Recommendations
1. Implement proper password complexity rules (uppercase, lowercase, numbers, special chars)
2. Use secure comparison to prevent timing attacks
3. Add salt and hash storage (never store plain text)
4. Make minimum length configurable
5. Add password strength indicator

## Test Coverage Strategy
- Test minimum length enforcement
- Test edge cases (null, empty, exactly 6 chars)
- Test various password complexities
- Test timing attack resistance (if fixed)
```

## Best Practices

### 1. **When to Use Pyguin vs LLM**

**Pyguin is best for:**
- Python code
- Simple, pure functions
- Fast iteration
- Coverage-based testing

**LLM is best for:**
- C# / .NET code
- Complex business logic
- Code requiring domain knowledge
- When you need code review
- When Pyguin fails

### 2. **Providing Existing Tests**

If you have starter tests, provide them in `existingTests`:
```json
{
  "existingTests": "using Xunit;\n\npublic class StarterTests {\n  [Fact]\n  public void BasicTest() { ... }\n}"
}
```

The LLM will:
- Use them as examples of your testing style
- Avoid duplicating existing tests
- Extend coverage beyond what you have

### 3. **Code Review Only**

For quick code review without full test generation:
```bash
curl -X POST http://localhost:5116/api/tools/review-code \
  -H "Content-Type: application/json" \
  -d '{"sourceCode": "your code here"}'
```

### 4. **Iterative Improvement**

Workflow:
1. Generate initial tests
2. Review code review feedback
3. Fix identified issues in source code
4. Regenerate tests with updated code
5. Compare fitness scores

## Integration with Fitness Testing

The unit test generator integrates with the fitness testing system:

```csharp
// In ToolFitnessTester, after identifying low-fitness tool:
var testResult = await _unitTestGenerator.GenerateTestsAsync(
    toolConfig.Name,
    toolConfig.SourceCode,
    existingTests: null);

if (testResult.Success)
{
    // Use generated tests to validate tool behavior
    // Store code review in fitness report
    // Track test coverage improvements
}
```

## Troubleshooting

### Pyguin Not Found

**Error:** `"Pyguin not installed"`

**Solution:**
```bash
pip install pyguin
```

Verify installation:
```bash
pyguin --version
```

### Pyguin Timeout

**Error:** `"Command timed out after 60000ms"`

**Solution:**
- This is normal for complex code
- LLM fallback will handle it automatically
- Pyguin timeout is configurable in code

### LLM Client Not Configured

**Error:** `"Pyguin failed and LLM client not configured"`

**Solution:**
Ensure LLM backend is configured in `appsettings.json`:
```json
{
  "LLMockApi": {
    "Backends": [
      {
        "Name": "default",
        "Provider": "anthropic",
        "BaseUrl": "https://api.anthropic.com/v1/messages",
        "ModelName": "claude-sonnet-3-5-20241022",
        "ApiKey": "${ANTHROPIC_API_KEY}",
        "Enabled": true
      }
    ]
  }
}
```

### Low Quality Tests Generated

**Problem:** LLM generates basic or incorrect tests

**Solutions:**
1. **Provide existing tests** as examples
2. **Use more powerful model** (Claude Opus instead of Sonnet)
3. **Add more context** in sourceCode comments
4. **Review and iterate** - regenerate with fixes applied

### C# Code Sent to Pyguin

**Problem:** Sending C# code when Pyguin expects Python

**Solution:**
- This is expected - Pyguin will fail gracefully
- LLM fallback will handle C# code correctly
- System is language-agnostic for LLM path

## Storage & File Management

### Output Directory

Generated tests are stored in:
```
%LocalAppData%\LLMockApi\GeneratedTests\
```

On Windows:
```
C:\Users\<YourName>\AppData\Local\LLMockApi\GeneratedTests\
```

### File Naming

```
{ToolName}_Tests_{Timestamp}.cs
```

Example:
```
WeatherApiTool_Tests_20250117_103045.cs
```

### Cleanup

To clean up old test files:
```bash
# View files
curl http://localhost:5116/api/tools/test-output

# Manually delete from file system
rm "%LocalAppData%\LLMockApi\GeneratedTests\*.cs"
```

## Advanced Usage

### Custom Test Frameworks

By default, tests use xUnit. To use NUnit or MSTest, modify the LLM prompt:

```csharp
// Modify BuildLlmTestGenerationPrompt() in UnitTestGenerator.cs
prompt.AppendLine("   - Use NUnit testing framework");  // Instead of xUnit
prompt.AppendLine("   - Use Assert.That() for assertions");  // Instead of FluentAssertions
```

### Integration with CI/CD

```yaml
# GitHub Actions example
- name: Generate Unit Tests
  run: |
    curl -X POST http://localhost:5116/api/tools/generate-tests \
      -H "Content-Type: application/json" \
      -d @test-request.json \
      -o generated-tests.json

- name: Extract and Run Tests
  run: |
    # Extract generated tests
    jq -r '.generatedTests' generated-tests.json > GeneratedTests.cs

    # Add to test project
    cp GeneratedTests.cs tests/

    # Run tests
    dotnet test
```

### Batch Generation

Generate tests for multiple tools:

```bash
#!/bin/bash
for tool in tool1 tool2 tool3; do
  curl -X POST http://localhost:5116/api/tools/generate-tests \
    -H "Content-Type: application/json" \
    -d "{\"toolName\": \"$tool\", \"sourceCode\": \"$(cat $tool.cs)\"}" \
    -o "$tool-tests.json"
done
```

## Future Enhancements

Planned features:
- **Test execution and validation** - Auto-run generated tests
- **Coverage analysis** - Measure actual code coverage
- **Test mutation** - Verify test quality with mutation testing
- **Multi-language support** - Java, TypeScript, Go, etc.
- **Custom test templates** - Organization-specific test patterns
- **Continuous test evolution** - Auto-update tests when code changes
- **Test quality scoring** - Fitness scores for generated tests themselves

## See Also

- [TOOL_FITNESS_TESTING.md](./TOOL_FITNESS_TESTING.md) - Fitness testing system
- [TOOLS_ACTIONS.md](./TOOLS_ACTIONS.md) - Tool system documentation
- [README.md](../README.md) - Main project documentation
