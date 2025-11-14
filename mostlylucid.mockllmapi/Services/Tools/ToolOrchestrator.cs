using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using mostlylucid.mockllmapi.Models;

namespace mostlylucid.mockllmapi.Services.Tools;

/// <summary>
/// Orchestrates tool execution with chaining, caching, and concurrency control
/// Prevents infinite loops and manages tool execution lifecycle
/// </summary>
public class ToolOrchestrator
{
    private readonly ToolRegistry _registry;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ToolOrchestrator> _logger;
    private readonly LLMockApiOptions _options;

    // Track execution depth per request to prevent infinite recursion
    private static readonly ConcurrentDictionary<string, int> _executionDepth = new();

    public ToolOrchestrator(
        ToolRegistry registry,
        IMemoryCache cache,
        IOptions<LLMockApiOptions> options,
        ILogger<ToolOrchestrator> logger)
    {
        _registry = registry;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Execute a single tool by name
    /// </summary>
    public async Task<ToolResult> ExecuteToolAsync(
        string toolName,
        Dictionary<string, object> parameters,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        // Check execution mode
        if (_options.ToolExecutionMode == ToolExecutionMode.Disabled)
        {
            return new ToolResult
            {
                Success = false,
                Error = "Tool execution is disabled",
                ToolName = toolName
            };
        }

        // Check if tool exists
        var tool = _registry.GetTool(toolName);
        if (tool == null)
        {
            _logger.LogWarning("Tool '{ToolName}' not found", toolName);
            return new ToolResult
            {
                Success = false,
                Error = $"Tool '{toolName}' not found",
                ToolName = toolName
            };
        }

        if (!tool.Enabled)
        {
            return new ToolResult
            {
                Success = false,
                Error = $"Tool '{toolName}' is disabled",
                ToolName = toolName
            };
        }

        // Check chain depth to prevent infinite recursion
        var currentDepth = _executionDepth.GetOrAdd(requestId, 0);
        if (currentDepth >= _options.MaxToolChainDepth)
        {
            _logger.LogWarning("Tool chain depth limit reached ({MaxDepth}) for request {RequestId}",
                _options.MaxToolChainDepth, requestId);
            return new ToolResult
            {
                Success = false,
                Error = $"Tool chain depth limit reached ({_options.MaxToolChainDepth})",
                ToolName = toolName
            };
        }

        try
        {
            // Increment depth
            _executionDepth.AddOrUpdate(requestId, 1, (_, depth) => depth + 1);

            // Check cache if enabled
            if (tool.EnableCaching)
            {
                var cacheKey = ComputeCacheKey(toolName, parameters);
                if (_cache.TryGetValue<ToolResult>(cacheKey, out var cachedResult))
                {
                    _logger.LogDebug("Returning cached result for tool '{ToolName}'", toolName);
                    return cachedResult!;
                }
            }

            // Get executor
            var executor = _registry.GetExecutor(tool.Type);
            if (executor == null)
            {
                return new ToolResult
                {
                    Success = false,
                    Error = $"No executor found for tool type '{tool.Type}'",
                    ToolName = toolName
                };
            }

            // Execute tool
            _logger.LogInformation("Executing tool '{ToolName}' (type: {ToolType})", toolName, tool.Type);
            var result = await executor.ExecuteAsync(tool, parameters, cancellationToken);

            // Cache result if enabled and successful
            if (tool.EnableCaching && result.Success)
            {
                var cacheKey = ComputeCacheKey(toolName, parameters);
                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(TimeSpan.FromMinutes(tool.CacheDurationMinutes));
                _cache.Set(cacheKey, result, cacheOptions);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error executing tool '{ToolName}'", toolName);
            return new ToolResult
            {
                Success = false,
                Error = $"Unexpected error: {ex.Message}",
                ToolName = toolName
            };
        }
        finally
        {
            // Decrement depth
            _executionDepth.AddOrUpdate(requestId, 0, (_, depth) => Math.Max(0, depth - 1));

            // Clean up if depth reaches 0
            if (_executionDepth.TryGetValue(requestId, out var finalDepth) && finalDepth == 0)
            {
                _executionDepth.TryRemove(requestId, out _);
            }
        }
    }

    /// <summary>
    /// Execute multiple tools in parallel (respecting MaxConcurrentTools limit)
    /// </summary>
    public async Task<List<ToolResult>> ExecuteToolsAsync(
        List<string> toolNames,
        Dictionary<string, object> parameters,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        if (toolNames.Count > _options.MaxConcurrentTools)
        {
            _logger.LogWarning("Requested {Count} tools but limit is {MaxConcurrent}",
                toolNames.Count, _options.MaxConcurrentTools);
            toolNames = toolNames.Take(_options.MaxConcurrentTools).ToList();
        }

        var tasks = toolNames.Select(toolName =>
            ExecuteToolAsync(toolName, parameters, requestId, cancellationToken));

        return (await Task.WhenAll(tasks)).ToList();
    }

    /// <summary>
    /// Parse tool calls from LLM response (Phase 2 implementation)
    /// Expected format: TOOL_CALL: toolName(param1=value1, param2=value2)
    /// </summary>
    public List<(string toolName, Dictionary<string, object> parameters)> ParseToolCallsFromLlmResponse(string llmResponse)
    {
        var toolCalls = new List<(string, Dictionary<string, object>)>();

        // Simple regex-based parsing for Phase 1
        // In Phase 2, this could be replaced with structured JSON parsing or function calling API
        var pattern = @"TOOL_CALL:\s*(\w+)\((.*?)\)";
        var matches = System.Text.RegularExpressions.Regex.Matches(llmResponse, pattern);

        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            var toolName = match.Groups[1].Value;
            var paramsStr = match.Groups[2].Value;

            var parameters = new Dictionary<string, object>();
            if (!string.IsNullOrWhiteSpace(paramsStr))
            {
                // Parse param1=value1, param2=value2
                var paramPairs = paramsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var pair in paramPairs)
                {
                    var parts = pair.Split('=', 2);
                    if (parts.Length == 2)
                    {
                        parameters[parts[0].Trim()] = parts[1].Trim().Trim('"');
                    }
                }
            }

            toolCalls.Add((toolName, parameters));
        }

        return toolCalls;
    }

    /// <summary>
    /// Format tool result for inclusion in LLM context
    /// </summary>
    public string FormatToolResultForContext(ToolResult result)
    {
        if (!result.Success)
        {
            return $"Tool '{result.ToolName}' failed: {result.Error}";
        }

        return $"Tool '{result.ToolName}' result:\n{result.Data}";
    }

    /// <summary>
    /// Format multiple tool results for context
    /// </summary>
    public string FormatToolResultsForContext(List<ToolResult> results)
    {
        if (results.Count == 0) return string.Empty;

        var formatted = results.Select(FormatToolResultForContext);
        return "Tool Results:\n" + string.Join("\n\n", formatted);
    }

    /// <summary>
    /// Compute cache key from tool name and parameters
    /// </summary>
    private string ComputeCacheKey(string toolName, Dictionary<string, object> parameters)
    {
        var paramString = string.Join("|", parameters.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}"));
        return $"tool:{toolName}:{paramString}";
    }
}
