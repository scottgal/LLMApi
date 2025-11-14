using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using mostlylucid.mockllmapi.Models;

namespace mostlylucid.mockllmapi.Services.Tools;

/// <summary>
/// Executes HTTP tool calls to external APIs
/// Supports authentication, template substitution, and response extraction
/// </summary>
public class HttpToolExecutor : IToolExecutor
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HttpToolExecutor> _logger;

    public string ToolType => "http";

    public HttpToolExecutor(
        IHttpClientFactory httpClientFactory,
        ILogger<HttpToolExecutor> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
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
            if (tool.HttpConfig == null)
            {
                throw new InvalidOperationException($"Tool '{tool.Name}' is missing HttpConfig");
            }

            // Substitute parameters in endpoint URL
            var endpoint = SubstituteParameters(tool.HttpConfig.Endpoint, parameters);

            _logger.LogDebug("Executing HTTP tool '{ToolName}': {Method} {Endpoint}",
                tool.Name, tool.HttpConfig.Method, endpoint);

            // Create HTTP client with timeout
            using var client = _httpClientFactory.CreateClient("ToolExecutor");
            client.Timeout = TimeSpan.FromMilliseconds(tool.TimeoutMs);

            // Create request
            var request = new HttpRequestMessage(
                new HttpMethod(tool.HttpConfig.Method.ToUpperInvariant()),
                endpoint);

            // Add authentication
            ApplyAuthentication(request, tool.HttpConfig);

            // Add headers
            if (tool.HttpConfig.Headers != null)
            {
                foreach (var (key, value) in tool.HttpConfig.Headers)
                {
                    var substitutedValue = SubstituteParameters(value, parameters);
                    request.Headers.TryAddWithoutValidation(key, substitutedValue);
                }
            }

            // Add body for POST/PUT/PATCH
            if (tool.HttpConfig.Method.ToUpperInvariant() is "POST" or "PUT" or "PATCH" &&
                !string.IsNullOrWhiteSpace(tool.HttpConfig.BodyTemplate))
            {
                var body = SubstituteParameters(tool.HttpConfig.BodyTemplate, parameters);
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            // Execute request
            var response = await client.SendAsync(request, cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            // Extract response data
            var extractedData = ExtractResponseData(responseContent, tool.HttpConfig.ResponsePath);

            stopwatch.Stop();

            result.Success = response.IsSuccessStatusCode;
            result.Data = extractedData;
            result.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
            result.Metadata = new Dictionary<string, object>
            {
                ["StatusCode"] = (int)response.StatusCode,
                ["Headers"] = response.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value))
            };

            if (!response.IsSuccessStatusCode)
            {
                result.Error = $"HTTP {response.StatusCode}: {responseContent}";
                _logger.LogWarning("HTTP tool '{ToolName}' failed: {Error}", tool.Name, result.Error);
            }
            else
            {
                _logger.LogDebug("HTTP tool '{ToolName}' succeeded in {ElapsedMs}ms",
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

            _logger.LogError(ex, "HTTP tool '{ToolName}' threw exception", tool.Name);
            return result;
        }
    }

    public void ValidateConfiguration(ToolConfig tool)
    {
        if (tool.HttpConfig == null)
        {
            throw new InvalidOperationException($"Tool '{tool.Name}' is missing HttpConfig");
        }

        if (string.IsNullOrWhiteSpace(tool.HttpConfig.Endpoint))
        {
            throw new InvalidOperationException($"Tool '{tool.Name}' has empty Endpoint");
        }

        if (!Uri.TryCreate(tool.HttpConfig.Endpoint, UriKind.Absolute, out _))
        {
            // Allow relative URLs or templated URLs
            if (!tool.HttpConfig.Endpoint.Contains("{") && !tool.HttpConfig.Endpoint.StartsWith("/"))
            {
                throw new InvalidOperationException(
                    $"Tool '{tool.Name}' has invalid Endpoint URL: {tool.HttpConfig.Endpoint}");
            }
        }

        var validMethods = new[] { "GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS" };
        if (!validMethods.Contains(tool.HttpConfig.Method.ToUpperInvariant()))
        {
            throw new InvalidOperationException(
                $"Tool '{tool.Name}' has invalid HTTP method: {tool.HttpConfig.Method}");
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

        // Handle environment variables: ${ENV_VAR_NAME}
        result = Regex.Replace(result, @"\$\{([^}]+)\}", match =>
        {
            var envVar = match.Groups[1].Value;
            return Environment.GetEnvironmentVariable(envVar) ?? match.Value;
        });

        return result;
    }

    /// <summary>
    /// Apply authentication to HTTP request
    /// </summary>
    private void ApplyAuthentication(HttpRequestMessage request, HttpToolConfig config)
    {
        switch (config.AuthType.ToLowerInvariant())
        {
            case "bearer":
                if (!string.IsNullOrWhiteSpace(config.AuthToken))
                {
                    var token = ResolveEnvVar(config.AuthToken);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }
                break;

            case "basic":
                if (!string.IsNullOrWhiteSpace(config.Username) && !string.IsNullOrWhiteSpace(config.Password))
                {
                    var username = ResolveEnvVar(config.Username);
                    var password = ResolveEnvVar(config.Password);
                    var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
                    request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                }
                break;

            case "apikey":
                if (!string.IsNullOrWhiteSpace(config.AuthToken))
                {
                    var token = ResolveEnvVar(config.AuthToken);
                    request.Headers.Add("X-API-Key", token);
                }
                break;

            case "none":
            default:
                // No authentication
                break;
        }
    }

    /// <summary>
    /// Resolve environment variable references: ${ENV_VAR_NAME}
    /// </summary>
    private string ResolveEnvVar(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;

        return Regex.Replace(value, @"\$\{([^}]+)\}", match =>
        {
            var envVar = match.Groups[1].Value;
            return Environment.GetEnvironmentVariable(envVar) ?? match.Value;
        });
    }

    /// <summary>
    /// Extract data from JSON response using JSONPath
    /// </summary>
    private string ExtractResponseData(string responseContent, string? jsonPath)
    {
        if (string.IsNullOrWhiteSpace(jsonPath))
        {
            return responseContent;
        }

        try
        {
            // Simple JSONPath implementation for basic paths like $.data.users
            // For more complex paths, could integrate Json.NET or similar
            var json = JsonDocument.Parse(responseContent);
            var current = json.RootElement;

            // Remove leading $. if present
            var path = jsonPath.TrimStart('$', '.');
            var parts = path.Split('.');

            foreach (var part in parts)
            {
                if (string.IsNullOrWhiteSpace(part)) continue;

                if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(part, out var property))
                {
                    current = property;
                }
                else
                {
                    _logger.LogWarning("JSONPath '{Path}' not found in response", jsonPath);
                    return responseContent; // Return full response if path not found
                }
            }

            return current.GetRawText();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse JSON response with path '{Path}'", jsonPath);
            return responseContent;
        }
    }
}
