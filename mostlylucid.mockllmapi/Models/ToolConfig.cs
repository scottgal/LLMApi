namespace mostlylucid.mockllmapi.Models;

/// <summary>
/// Tool configuration for pluggable actions
/// Supports HTTP calls, mock endpoint calls, and extensible tool types
/// MCP-compatible design for future integration
/// </summary>
public class ToolConfig
{
    /// <summary>
    /// Unique tool identifier
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Tool type: http, mock, function
    /// Extensible for future tool types (database, mcp, etc.)
    /// </summary>
    public string Type { get; set; } = "http";

    /// <summary>
    /// Human-readable description of what this tool does
    /// Used in LLM prompts for tool selection
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Enable this tool (default: true)
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// HTTP tool configuration (when Type = "http")
    /// </summary>
    public HttpToolConfig? HttpConfig { get; set; }

    /// <summary>
    /// Mock endpoint tool configuration (when Type = "mock")
    /// Allows calling other mock endpoints for decision trees
    /// </summary>
    public MockToolConfig? MockConfig { get; set; }

    /// <summary>
    /// Input parameters schema (JSON Schema format)
    /// Used for validation and LLM tool selection
    /// </summary>
    public Dictionary<string, ParameterSchema>? Parameters { get; set; }

    /// <summary>
    /// Maximum execution time in milliseconds (default: 10000 = 10s)
    /// </summary>
    public int TimeoutMs { get; set; } = 10000;

    /// <summary>
    /// Enable tool result caching (default: false)
    /// Cache key is computed from tool name + parameters
    /// </summary>
    public bool EnableCaching { get; set; } = false;

    /// <summary>
    /// Cache duration in minutes (default: 5)
    /// Only applies if EnableCaching is true
    /// </summary>
    public int CacheDurationMinutes { get; set; } = 5;
}

/// <summary>
/// HTTP tool configuration for external API calls
/// </summary>
public class HttpToolConfig
{
    /// <summary>
    /// HTTP endpoint URL
    /// Supports placeholders: {paramName}
    /// Example: "https://api.example.com/users/{userId}"
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// HTTP method (GET, POST, PUT, DELETE, PATCH)
    /// </summary>
    public string Method { get; set; } = "GET";

    /// <summary>
    /// HTTP headers to include in request
    /// Supports placeholders: {paramName}
    /// </summary>
    public Dictionary<string, string>? Headers { get; set; }

    /// <summary>
    /// Request body template (for POST/PUT/PATCH)
    /// Supports JSON with placeholders: {paramName}
    /// Example: {"userId": "{userId}", "action": "{action}"}
    /// </summary>
    public string? BodyTemplate { get; set; }

    /// <summary>
    /// Response field to extract (JSONPath)
    /// If null, returns entire response
    /// Example: "$.data.users" extracts nested field
    /// </summary>
    public string? ResponsePath { get; set; }

    /// <summary>
    /// Authentication type: none, bearer, basic, apikey
    /// </summary>
    public string AuthType { get; set; } = "none";

    /// <summary>
    /// Authentication token/key (for bearer or apikey)
    /// Can reference environment variable: ${ENV_VAR_NAME}
    /// </summary>
    public string? AuthToken { get; set; }

    /// <summary>
    /// Username for basic auth
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Password for basic auth
    /// Can reference environment variable: ${ENV_VAR_NAME}
    /// </summary>
    public string? Password { get; set; }
}

/// <summary>
/// Mock endpoint tool configuration for calling other mock endpoints
/// Enables decision trees and workflow composition
/// </summary>
public class MockToolConfig
{
    /// <summary>
    /// Mock endpoint path (relative to base URL)
    /// Supports placeholders: {paramName}
    /// Example: "/api/mock/users/{userId}/orders"
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// HTTP method (GET, POST, etc.)
    /// </summary>
    public string Method { get; set; } = "GET";

    /// <summary>
    /// Query parameters to include
    /// Supports placeholders: {paramName}
    /// </summary>
    public Dictionary<string, string>? QueryParams { get; set; }

    /// <summary>
    /// Request body (for POST/PUT)
    /// Supports JSON with placeholders: {paramName}
    /// </summary>
    public string? Body { get; set; }

    /// <summary>
    /// Shape to pass to the mock endpoint
    /// Supports placeholders: {paramName}
    /// </summary>
    public string? Shape { get; set; }

    /// <summary>
    /// Context name to use for the mock call
    /// Enables context sharing across tool chain
    /// </summary>
    public string? ContextName { get; set; }
}

/// <summary>
/// Parameter schema for tool input validation
/// MCP-compatible format
/// </summary>
public class ParameterSchema
{
    /// <summary>
    /// Parameter data type: string, number, boolean, object, array
    /// </summary>
    public string Type { get; set; } = "string";

    /// <summary>
    /// Human-readable parameter description
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Is this parameter required?
    /// </summary>
    public bool Required { get; set; } = false;

    /// <summary>
    /// Default value if not provided
    /// </summary>
    public object? Default { get; set; }

    /// <summary>
    /// Allowed values (enum)
    /// </summary>
    public List<object>? Enum { get; set; }
}

/// <summary>
/// Result of tool execution
/// </summary>
public class ToolResult
{
    /// <summary>
    /// Was the tool execution successful?
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Tool output data (JSON string or plain text)
    /// </summary>
    public string? Data { get; set; }

    /// <summary>
    /// Error message if execution failed
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Execution time in milliseconds
    /// </summary>
    public long ExecutionTimeMs { get; set; }

    /// <summary>
    /// Tool name that was executed
    /// </summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>
    /// Additional metadata (HTTP status code, headers, etc.)
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Tool execution mode
/// </summary>
public enum ToolExecutionMode
{
    /// <summary>
    /// Disabled - tools not available
    /// </summary>
    Disabled = 0,

    /// <summary>
    /// Explicit - tools only called when explicitly requested via query param/header
    /// </summary>
    Explicit = 1,

    /// <summary>
    /// LLM-driven - LLM can decide to call tools based on request
    /// (Phase 2 implementation)
    /// </summary>
    LlmDriven = 2
}
