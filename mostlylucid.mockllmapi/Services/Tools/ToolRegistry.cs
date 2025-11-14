using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using mostlylucid.mockllmapi.Models;

namespace mostlylucid.mockllmapi.Services.Tools;

/// <summary>
/// Registry for managing available tools
/// Provides tool discovery and validation
/// </summary>
public class ToolRegistry
{
    private readonly Dictionary<string, ToolConfig> _tools = new();
    private readonly Dictionary<string, IToolExecutor> _executors = new();
    private readonly ILogger<ToolRegistry> _logger;
    private readonly LLMockApiOptions _options;

    public ToolRegistry(
        IEnumerable<IToolExecutor> executors,
        IOptions<LLMockApiOptions> options,
        ILogger<ToolRegistry> _logger)
    {
        _options = options.Value;
        this._logger = _logger;

        // Register all tool executors by type
        foreach (var executor in executors)
        {
            _executors[executor.ToolType] = executor;
            _logger.LogInformation("Registered tool executor for type: {ToolType}", executor.ToolType);
        }

        // Load and validate tools from configuration
        LoadTools();
    }

    /// <summary>
    /// Get tool by name
    /// </summary>
    public ToolConfig? GetTool(string name)
    {
        return _tools.TryGetValue(name, out var tool) ? tool : null;
    }

    /// <summary>
    /// Get all available tools
    /// </summary>
    public IEnumerable<ToolConfig> GetAllTools()
    {
        return _tools.Values.Where(t => t.Enabled);
    }

    /// <summary>
    /// Get tool executor for a given tool type
    /// </summary>
    public IToolExecutor? GetExecutor(string toolType)
    {
        return _executors.TryGetValue(toolType, out var executor) ? executor : null;
    }

    /// <summary>
    /// Check if a tool exists and is enabled
    /// </summary>
    public bool IsToolAvailable(string name)
    {
        return _tools.TryGetValue(name, out var tool) && tool.Enabled;
    }

    /// <summary>
    /// Get tool definitions formatted for LLM prompts (Phase 2)
    /// Returns JSON schema compatible with MCP format
    /// </summary>
    public string GetToolDefinitionsForLlm()
    {
        var tools = GetAllTools().Select(t => new
        {
            name = t.Name,
            description = t.Description,
            parameters = t.Parameters?.ToDictionary(
                p => p.Key,
                p => new
                {
                    type = p.Value.Type,
                    description = p.Value.Description,
                    required = p.Value.Required,
                    @default = p.Value.Default,
                    @enum = p.Value.Enum
                })
        });

        return System.Text.Json.JsonSerializer.Serialize(new { tools }, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    /// <summary>
    /// Load tools from configuration and validate them
    /// </summary>
    private void LoadTools()
    {
        if (_options.ToolExecutionMode == ToolExecutionMode.Disabled)
        {
            _logger.LogInformation("Tool execution is disabled");
            return;
        }

        foreach (var tool in _options.Tools)
        {
            try
            {
                // Validate tool has required fields
                if (string.IsNullOrWhiteSpace(tool.Name))
                {
                    _logger.LogWarning("Skipping tool with empty name");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(tool.Type))
                {
                    _logger.LogWarning("Tool '{ToolName}' has no type specified", tool.Name);
                    continue;
                }

                // Get executor for this tool type
                var executor = GetExecutor(tool.Type);
                if (executor == null)
                {
                    _logger.LogWarning("No executor found for tool type '{ToolType}' (tool: {ToolName})",
                        tool.Type, tool.Name);
                    continue;
                }

                // Validate tool configuration
                executor.ValidateConfiguration(tool);

                // Register tool
                _tools[tool.Name] = tool;
                _logger.LogInformation("Registered tool: {ToolName} (type: {ToolType})",
                    tool.Name, tool.Type);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to register tool '{ToolName}': {Error}",
                    tool.Name, ex.Message);
            }
        }

        _logger.LogInformation("Tool registry initialized with {Count} tools", _tools.Count);
    }
}
