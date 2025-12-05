# Tool Fitness Testing & Evolution System

## Overview

The Tool Fitness Testing system is a comprehensive framework for automatically testing, scoring, and optimizing pluggable tools in the LLMock API. It discovers all configured tools, generates appropriate dummy data, validates expectations, calculates fitness scores, stores results in a RAG-optimized format, and triggers evolution/optimization using god-level LLMs for low-performing tools.

## Quick Start

### 1. Enable Fitness Testing

The fitness testing services are automatically registered when you call `AddLLMockApi()`. To enable the API endpoints, add this to your `Program.cs`:

```csharp
// Map Tool Fitness testing and evolution endpoints
app.MapLLMockToolFitness("/api/tools/fitness");
```

### 2. Run Fitness Tests

**Via PowerShell Script:**
```powershell
.\scripts\test-tools-fitness.ps1 -BaseUrl "http://localhost:5116"
```

**Via HTTP Request:**
```http
POST /api/tools/fitness/test
Content-Type: application/json

{}
```

**Via cURL:**
```bash
curl -X POST http://localhost:5116/api/tools/fitness/test \
  -H "Content-Type: application/json" \
  -d '{}'
```

### 3. View Results

The response includes:
- Overall test summary (passed/failed counts, average fitness)
- Individual tool results with fitness scores (0-100)
- Failed validations and execution errors
- Execution time metrics

## Architecture

### Components

#### 1. **ToolFitnessTester** (`Services/Tools/ToolFitnessTester.cs`)
- Main testing orchestrator
- Discovers tools from ToolRegistry
- Generates dummy parameters based on schema
- Executes tools in parallel (respecting concurrency limits)
- Validates results against expectations
- Calculates multi-dimensional fitness scores
- Triggers god-level LLM evolution for low-fitness tools

#### 2. **ToolFitnessRagStore** (`Services/Tools/ToolFitnessRagStore.cs`)
- Stores fitness history in memory and on disk
- Exports RAG-optimized JSONL documents for vector embeddings
- Provides querying and trend analysis
- Auto-expires old data (keeps last 100 snapshots per tool)
- Storage location: `%LocalAppData%\LLMockApi\ToolFitness\`

#### 3. **ToolFitnessEndpoints** (`ToolFitnessEndpoints.cs`)
- REST API endpoints for fitness operations
- `/api/tools/fitness/test` - Run tests
- `/api/tools/fitness/{toolName}` - Get tool history
- `/api/tools/fitness/low` - Get low-fitness tools
- `/api/tools/fitness/trends` - Get fitness trends
- `/api/tools/fitness/evolve` - Trigger evolution
- `/api/tools/fitness/export` - Export full history
- `/api/tools/fitness/storage` - Get storage info

## Fitness Scoring System

Fitness scores range from **0-100** and are calculated based on:

### 1. **Pass/Fail (40 points)**
- Tool executed without critical errors
- All expectations were met

### 2. **Validation Results (30 points)**
- Percentage of passed validations
- NoException, HTTP success/failure, JSONPath extraction, etc.

### 3. **Performance (15 points)**
- Execution time relative to timeout
  - < 25% of timeout: 15 points
  - < 50% of timeout: 12 points
  - < 75% of timeout: 8 points
  - < 100% of timeout: 4 points

### 4. **Configuration Quality (10 points)**
- Has description: 2 points
- Has parameters: 2 points
- Parameters have descriptions: 2 points
- Parameters have examples: 2 points
- Caching enabled: 2 points

### 5. **Feature Completeness (5 points)**
- Authentication configured: 2 points
- JSONPath extraction: 1 point
- Context sharing: 2 points

## Test Expectations

The system automatically generates expectations based on tool type:

### Common Expectations (All Tools)
- **NoException**: Tool should execute without throwing
- **ReasonableExecutionTime**: Complete within timeout

### HTTP Tool Expectations
- **HttpSuccess/HttpFailure**: Based on URL validity
  - Valid URLs → expect success (2xx/3xx)
  - Dummy URLs (example.com, test-invalid) → expect graceful failure (404)
- **JsonPathExtraction**: If `ExtractJsonPath` configured
- **AuthenticationConfigured**: If authentication present

### Mock Tool Expectations
- **MockEndpointCall**: Successfully call internal endpoint
- **ContextSharing**: Share context across tool chain

### Caching Expectations
- **Caching**: If `CacheTTLMinutes > 0`

## Dummy Data Generation

The system automatically generates test data based on parameter schemas:

### Strategy
1. **Use `Example` if provided** (highest priority)
2. **Use `Default` if provided**
3. **Generate based on `Type`**:
   - `string` → "test-value" or first enum value
   - `integer/int` → 42
   - `number` → 3.14
   - `boolean/bool` → true
   - `array` → ["item1", "item2"]
   - `object` → {"key": "value"}

### URL Substitution
For HTTP tools, parameters are substituted into the URL template:
```json
{
  "url": "https://api.example.com/users/{userId}",
  "parameters": [{ "name": "userId", "type": "integer", "example": 123 }]
}
```
Becomes: `https://api.example.com/users/123`

## Evolution & Optimization

### God-Level LLM Integration

When a tool has low fitness (< 60 by default), the system can automatically trigger evolution using a god-level LLM (e.g., Claude Opus).

#### How It Works

1. **Identify low-fitness tools** from test results
2. **Build detailed evolution prompt** including:
   - Tool configuration
   - Fitness score and failed validations
   - Execution errors
   - Test parameters used
   - All expectations (passed and failed)
3. **Call god-level LLM** with high token limit (8000)
4. **Receive recommendations** including:
   - Root cause analysis
   - Configuration improvements
   - Parameter optimization
   - Performance optimization
   - Error handling improvements
   - Recommended configuration JSON

#### Triggering Evolution

**Via PowerShell:**
```powershell
.\scripts\test-tools-fitness.ps1 -EvolveTools -FitnessThreshold 70
```

**Via HTTP:**
```http
POST /api/tools/fitness/evolve
Content-Type: application/json

{
  "threshold": 60.0
}
```

**Response:**
```json
{
  "threshold": 60.0,
  "toolsEvolved": 3,
  "evolutionResults": [
    {
      "toolName": "weatherApi",
      "success": true,
      "originalFitness": 45.5,
      "recommendations": "ROOT CAUSE ANALYSIS: The tool fails because...\n\nCONFIGURATION IMPROVEMENTS: ...",
      "evolvedAt": "2025-01-17T10:30:00Z"
    }
  ]
}
```

### Configuring God-Level LLM

To enable evolution, ensure you have a god-level LLM backend configured in `appsettings.json`:

```json
{
  "LLMockApi": {
    "Backends": [
      {
        "Name": "god-model",
        "Provider": "anthropic",
        "BaseUrl": "https://api.anthropic.com/v1/messages",
        "ModelName": "claude-opus-20240229",
        "ApiKey": "${ANTHROPIC_API_KEY}",
        "Enabled": true
      }
    ]
  }
}
```

Then select it for tool evolution requests via header:
```http
X-LLM-Backend: god-model
```

## RAG Storage Format

### File Structure

#### Full Reports
```
%LocalAppData%\LLMockApi\ToolFitness\
├── fitness_report_{testRunId}_{timestamp}.json
├── fitness_report_{testRunId}_{timestamp}.json
└── ...
```

#### RAG Documents (JSONL)
```
%LocalAppData%\LLMockApi\ToolFitness\
├── rag_documents_{testRunId}_{timestamp}.jsonl
├── rag_documents_{testRunId}_{timestamp}.jsonl
└── ...
```

### JSONL Format

Each line is a complete JSON object optimized for vector embedding:

```json
{
  "documentId": "weatherApi_abc123",
  "toolName": "weatherApi",
  "toolType": "HTTP",
  "timestamp": "2025-01-17T10:00:00Z",
  "fitnessScore": 45.5,
  "passed": false,
  "semanticText": "Tool: weatherApi\nType: HTTP\nFitness Score: 45.5/100\nStatus: FAILED\n\nFailed Validations:\n- HTTP request should succeed\n  Expected: Success status code\n  Actual: HTTP error\n\nExecution Error: Connection timeout\n\nPassed Validations: 3/5\n",
  "metadata": {
    "toolName": "weatherApi",
    "toolType": "HTTP",
    "fitnessScore": 45.5,
    "passed": false,
    "executionTimeMs": 30000,
    "testRunId": "abc123",
    "timestamp": "2025-01-17T10:00:00Z"
  },
  "fullTestResult": { /* complete ToolTestResult object */ }
}
```

### Semantic Text for Embeddings

The `semanticText` field is optimized for semantic search:
- Tool name and type
- Fitness score and pass/fail status
- **Failed validations** (most important)
- Execution errors
- Summary of passed validations

This allows you to:
- Search for similar failures
- Find tools with related issues
- Build recommendation systems
- Cluster tools by failure patterns

## API Reference

### POST /api/tools/fitness/test

Run comprehensive fitness tests on all configured tools.

**Response:**
```json
{
  "testRunId": "guid-here",
  "startTime": "2025-01-17T10:00:00Z",
  "endTime": "2025-01-17T10:02:30Z",
  "totalDuration": "00:02:30",
  "totalTools": 5,
  "passedTests": 3,
  "failedTests": 2,
  "averageFitness": 72.5,
  "toolResults": [
    {
      "toolName": "weatherApi",
      "toolType": "HTTP",
      "startTime": "2025-01-17T10:00:00Z",
      "endTime": "2025-01-17T10:00:05Z",
      "executionTimeMs": 5000,
      "testParameters": { "city": "test-value" },
      "expectations": [ /* list of TestExpectation */ ],
      "validationResults": [ /* list of ValidationResult */ ],
      "actualResult": "{ \"error\": \"timeout\" }",
      "executionError": null,
      "passed": false,
      "fitnessScore": 45.5
    }
  ]
}
```

### GET /api/tools/fitness/{toolName}?maxResults=10

Get fitness history for a specific tool.

**Response:**
```json
{
  "toolName": "weatherApi",
  "historyCount": 10,
  "latestFitness": 45.5,
  "history": [
    {
      "toolName": "weatherApi",
      "toolType": "HTTP",
      "timestamp": "2025-01-17T10:00:00Z",
      "testRunId": "guid",
      "fitnessScore": 45.5,
      "passed": false,
      "executionTimeMs": 5000,
      "validationsPassed": 3,
      "validationsTotal": 5,
      "executionError": "Connection timeout",
      "testParameters": { "city": "test-value" },
      "failedValidations": ["HttpSuccess: HTTP request should succeed"]
    }
  ]
}
```

### GET /api/tools/fitness/low?threshold=60

Get all tools with fitness below threshold.

**Response:**
```json
{
  "threshold": 60.0,
  "count": 2,
  "tools": [
    {
      "toolName": "weatherApi",
      "latestSnapshot": { /* ToolFitnessSnapshot */ }
    }
  ]
}
```

### GET /api/tools/fitness/trends?minSnapshots=3

Get fitness trends for all tools.

**Response:**
```json
{
  "totalTools": 5,
  "trends": [
    {
      "toolName": "weatherApi",
      "trend": {
        "toolName": "weatherApi",
        "initialScore": 40.0,
        "currentScore": 45.5,
        "change": 5.5,
        "direction": "Improving",
        "snapshots": 5
      }
    }
  ]
}
```

### POST /api/tools/fitness/evolve

Trigger evolution for low-fitness tools.

**Request:**
```json
{
  "threshold": 60.0
}
```

**Response:**
```json
{
  "threshold": 60.0,
  "toolsEvolved": 2,
  "evolutionResults": [
    {
      "toolName": "weatherApi",
      "success": true,
      "originalFitness": 45.5,
      "recommendations": "...",
      "evolvedAt": "2025-01-17T10:30:00Z"
    }
  ]
}
```

### GET /api/tools/fitness/export

Export complete fitness history to JSON file.

**Response:**
```json
{
  "message": "Fitness history exported successfully",
  "filePath": "C:\\Users\\...\\full_fitness_history_20250117_103000.json",
  "storageDirectory": "C:\\Users\\...\\LLMockApi\\ToolFitness"
}
```

### GET /api/tools/fitness/storage

Get storage directory information.

**Response:**
```json
{
  "storageDirectory": "C:\\Users\\...\\LLMockApi\\ToolFitness",
  "exists": true,
  "fileCount": 15,
  "files": [
    "fitness_report_guid_20250117_100000.json",
    "rag_documents_guid_20250117_100000.jsonl",
    "..."
  ]
}
```

## PowerShell Script Reference

### test-tools-fitness.ps1

Comprehensive fitness testing script with rich console output.

#### Parameters

- `-BaseUrl` - API base URL (default: `http://localhost:5116`)
- `-FitnessThreshold` - Threshold for low-fitness flagging (default: `60.0`)
- `-EvolveTools` - Trigger evolution for low-fitness tools
- `-ExportPath` - Export path for JSON report (default: `./tool-fitness-report.json`)
- `-Verbose` - Enable verbose logging

#### Examples

**Basic test:**
```powershell
.\scripts\test-tools-fitness.ps1
```

**Test with evolution:**
```powershell
.\scripts\test-tools-fitness.ps1 -EvolveTools -FitnessThreshold 70
```

**Custom base URL and export:**
```powershell
.\scripts\test-tools-fitness.ps1 `
  -BaseUrl "http://localhost:5000" `
  -ExportPath "C:\Reports\fitness.json" `
  -Verbose
```

#### Output

The script provides rich console output with:
- ✓/✗ status indicators
- Color-coded fitness scores (green ≥80, yellow ≥60, red <60)
- Execution time metrics
- Failed validation details (in verbose mode)
- Low-fitness tool warnings
- Evolution recommendations (if enabled)

## Best Practices

### 1. **Regular Testing**

Run fitness tests:
- After tool configuration changes
- After LLM model changes
- On a schedule (daily/weekly)
- Before production deployments

### 2. **Fitness Thresholds**

Recommended thresholds:
- **Production tools**: ≥ 80
- **Staging tools**: ≥ 70
- **Development tools**: ≥ 60

### 3. **Evolution Workflow**

1. Run fitness tests to identify low-scoring tools
2. Trigger evolution for recommendations
3. Review and apply recommended changes
4. Re-run fitness tests to validate improvements
5. Iterate until tools meet threshold

### 4. **RAG Integration**

Use exported JSONL documents for:
- Vector database ingestion (Pinecone, Weaviate, etc.)
- Semantic search over tool failures
- Building tool recommendation systems
- Clustering similar tool issues
- Training custom optimization models

### 5. **Tool Configuration**

Improve fitness scores by:
- Adding detailed descriptions to tools and parameters
- Providing examples in parameter schemas
- Enabling caching for frequently-used tools
- Using valid URLs for HTTP tools (or accepting lower scores for dummy URLs)
- Configuring authentication properly
- Adding JSONPath extraction for targeted data retrieval

## Troubleshooting

### Low Fitness Scores

**Problem**: Tool has low fitness score but works correctly

**Solutions**:
- Check if expectations are too strict
- Ensure dummy parameters are realistic
- Review URL validity (dummy URLs will score lower)
- Add examples to parameter schemas

**Problem**: Tool fails with timeout

**Solutions**:
- Increase `TimeoutSeconds` in configuration
- Check network connectivity
- Verify external API availability
- Consider using caching

**Problem**: Evolution endpoint returns error

**Solutions**:
- Ensure god-level LLM backend is configured
- Check `X-LLM-Backend` header is set correctly
- Verify API keys are valid
- Check LLM endpoint availability

### Storage Issues

**Problem**: RAG data not persisting

**Solutions**:
- Check write permissions on `%LocalAppData%\LLMockApi\ToolFitness\`
- Verify disk space availability
- Check for file system errors

**Problem**: Too many files in storage directory

**Solutions**:
- Implement periodic cleanup of old reports
- The system keeps last 100 snapshots per tool in memory
- Manually delete old `fitness_report_*.json` and `rag_documents_*.jsonl` files

## Future Enhancements

Planned features:
- Automatic tool configuration optimization
- Machine learning-based fitness prediction
- Integration with CI/CD pipelines
- Real-time fitness monitoring dashboard
- A/B testing for tool configurations
- Automated regression detection
- Tool performance benchmarking
- Multi-model evolution (consensus from multiple god-level LLMs)

## See Also

- [TOOLS_ACTIONS.md](./TOOLS_ACTIONS.md) - Tool system documentation
- [README.md](../README.md) - Main project documentation
- [CLAUDE.md](../CLAUDE.md) - Development guide
