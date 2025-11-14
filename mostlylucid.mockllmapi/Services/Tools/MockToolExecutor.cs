using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using mostlylucid.mockllmapi.Models;

namespace mostlylucid.mockllmapi.Services.Tools;

/// <summary>
/// Executes mock endpoint tool calls to other mock endpoints
/// Enables decision trees and workflow composition
/// </summary>
public class MockToolExecutor : IToolExecutor
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MockToolExecutor> _logger;
    private readonly string _baseUrl;

    public string ToolType => "mock";

    public MockToolExecutor(
        IHttpClientFactory httpClientFactory,
        ILogger<MockToolExecutor> logger,
        string baseUrl = "http://localhost:5116") // Default, should be injected from config
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public async Task<ToolResult> ExecuteAsync(
        ToolConfig tool,
        Dictionary<string, object> parameters,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new ToolResult { ToolName = tool.Name };

        try
        {
            if (tool.MockConfig == null)
            {
                throw new InvalidOperationException($"Tool '{tool.Name}' is missing MockConfig");
            }

            // Substitute parameters in endpoint path
            var endpoint = SubstituteParameters(tool.MockConfig.Endpoint, parameters);
            var fullUrl = $"{_baseUrl}{endpoint}";

            // Add query parameters
            if (tool.MockConfig.QueryParams != null && tool.MockConfig.QueryParams.Count > 0)
            {
                var queryParams = tool.MockConfig.QueryParams
                    .Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(SubstituteParameters(kvp.Value, parameters))}")
                    .ToList();

                if (!string.IsNullOrWhiteSpace(tool.MockConfig.Shape))
                {
                    var shape = SubstituteParameters(tool.MockConfig.Shape, parameters);
                    queryParams.Add($"shape={Uri.EscapeDataString(shape)}");
                }

                fullUrl += "?" + string.Join("&", queryParams);
            }
            else if (!string.IsNullOrWhiteSpace(tool.MockConfig.Shape))
            {
                var shape = SubstituteParameters(tool.MockConfig.Shape, parameters);
                fullUrl += $"?shape={Uri.EscapeDataString(shape)}";
            }

            _logger.LogDebug("Executing Mock tool '{ToolName}': {Method} {Url}",
                tool.Name, tool.MockConfig.Method, fullUrl);

            // Create HTTP client
            using var client = _httpClientFactory.CreateClient("ToolExecutor");
            client.Timeout = TimeSpan.FromMilliseconds(tool.TimeoutMs);

            // Create request
            var request = new HttpRequestMessage(
                new HttpMethod(tool.MockConfig.Method.ToUpperInvariant()),
                fullUrl);

            // Add context header if specified
            if (!string.IsNullOrWhiteSpace(tool.MockConfig.ContextName))
            {
                request.Headers.Add("X-Context-Name", tool.MockConfig.ContextName);
            }

            // Add body for POST/PUT
            if (tool.MockConfig.Method.ToUpperInvariant() is "POST" or "PUT" &&
                !string.IsNullOrWhiteSpace(tool.MockConfig.Body))
            {
                var body = SubstituteParameters(tool.MockConfig.Body, parameters);
                request.Content = new System.Net.Http.StringContent(
                    body,
                    System.Text.Encoding.UTF8,
                    "application/json");
            }

            // Execute request
            var response = await client.SendAsync(request, cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            stopwatch.Stop();

            result.Success = response.IsSuccessStatusCode;
            result.Data = responseContent;
            result.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
            result.Metadata = new Dictionary<string, object>
            {
                ["StatusCode"] = (int)response.StatusCode,
                ["MockEndpoint"] = endpoint
            };

            if (!response.IsSuccessStatusCode)
            {
                result.Error = $"Mock endpoint {response.StatusCode}: {responseContent}";
                _logger.LogWarning("Mock tool '{ToolName}' failed: {Error}", tool.Name, result.Error);
            }
            else
            {
                _logger.LogDebug("Mock tool '{ToolName}' succeeded in {ElapsedMs}ms",
                    tool.Name, stopwatch.ElapsedMilliseconds);
            }

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.Success = false;
            result.Error = $"{ex.GetType().Name}: {ex.Message}";
            result.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;

            _logger.LogError(ex, "Mock tool '{ToolName}' threw exception", tool.Name);
            return result;
        }
    }

    public void ValidateConfiguration(ToolConfig tool)
    {
        if (tool.MockConfig == null)
        {
            throw new InvalidOperationException($"Tool '{tool.Name}' is missing MockConfig");
        }

        if (string.IsNullOrWhiteSpace(tool.MockConfig.Endpoint))
        {
            throw new InvalidOperationException($"Tool '{tool.Name}' has empty Endpoint");
        }

        if (!tool.MockConfig.Endpoint.StartsWith("/"))
        {
            throw new InvalidOperationException(
                $"Tool '{tool.Name}' Endpoint must start with '/': {tool.MockConfig.Endpoint}");
        }

        var validMethods = new[] { "GET", "POST", "PUT", "DELETE", "PATCH" };
        if (!validMethods.Contains(tool.MockConfig.Method.ToUpperInvariant()))
        {
            throw new InvalidOperationException(
                $"Tool '{tool.Name}' has invalid HTTP method: {tool.MockConfig.Method}");
        }
    }

    /// <summary>
    /// Substitute {paramName} placeholders with actual values
    /// </summary>
    private string SubstituteParameters(string template, Dictionary<string, object> parameters)
    {
        var result = template;

        foreach (var (key, value) in parameters)
        {
            var placeholder = $"{{{key}}}";
            var valueStr = value?.ToString() ?? string.Empty;
            result = result.Replace(placeholder, valueStr);
        }

        return result;
    }
}
