using mostlylucid.mockllmapi.Models;

namespace mostlylucid.mockllmapi.Services.Tools;

/// <summary>
/// Interface for tool executors
/// Extensible design for different tool types (HTTP, Mock, Function, MCP, etc.)
/// </summary>
public interface IToolExecutor
{
    /// <summary>
    /// Tool type this executor handles (http, mock, function, etc.)
    /// </summary>
    string ToolType { get; }

    /// <summary>
    /// Execute a tool with given parameters
    /// </summary>
    /// <param name="tool">Tool configuration</param>
    /// <param name="parameters">Parameter values to substitute in templates</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Tool execution result</returns>
    Task<ToolResult> ExecuteAsync(
        ToolConfig tool,
        Dictionary<string, object> parameters,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate tool configuration
    /// Throws exception if configuration is invalid
    /// </summary>
    /// <param name="tool">Tool to validate</param>
    void ValidateConfiguration(ToolConfig tool);
}
