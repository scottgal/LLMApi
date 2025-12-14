using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using mostlylucid.mockllmapi.Models;

namespace mostlylucid.mockllmapi.Services.Tools;

/// <summary>
///     Executes HTTP tool calls to external APIs
///     Supports authentication, template substitution, and response extraction
/// </summary>
public class HttpToolExecutor : IToolExecutor
{
    // Allowed environment variables for security
    private static readonly HashSet<string> AllowedEnvVars = new(StringComparer.OrdinalIgnoreCase)
    {
        "API_URL", "BASE_URL", "ENDPOINT_URL", "SERVICE_URL",
        "API_KEY", "AUTH_TOKEN", "ACCESS_TOKEN",
        "API_VERSION", "ENVIRONMENT", "CONFIG_NAME"
    };

    // Private IP ranges to block for SSRF protection
    private static readonly (IPAddress baseAddress, int prefixLength)[] PrivateIpRanges =
    {
        (IPAddress.Parse("10.0.0.0"), 8),
        (IPAddress.Parse("172.16.0.0"), 12),
        (IPAddress.Parse("192.168.0.0"), 16),
        (IPAddress.Parse("127.0.0.0"), 8),
        (IPAddress.Parse("169.254.0.0"), 16), // Link-local
        (IPAddress.Parse("::1"), 128), // IPv6 localhost
        (IPAddress.Parse("fc00::"), 7) // IPv6 private
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HttpToolExecutor> _logger;

    public HttpToolExecutor(
        IHttpClientFactory httpClientFactory,
        ILogger<HttpToolExecutor> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string ToolType => "http";

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
                throw new InvalidOperationException($"Tool '{tool.Name}' is missing HttpConfig");

            // Validate and substitute parameters in endpoint URL
            var endpoint = ValidateAndSubstituteEndpoint(tool.HttpConfig.Endpoint, parameters);

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

            // Add headers with validation
            if (tool.HttpConfig.Headers != null)
                foreach (var (key, value) in tool.HttpConfig.Headers)
                    if (IsValidHeaderKey(key) && IsValidHeaderValue(value))
                    {
                        var substitutedValue = SubstituteParameters(value, parameters);
                        request.Headers.TryAddWithoutValidation(key, substitutedValue);
                    }
                    else
                    {
                        _logger.LogWarning("Skipping invalid header '{Key}': '{Value}'", key, value);
                    }

            // Add body for POST/PUT/PATCH with validation
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
        if (tool.HttpConfig == null) throw new InvalidOperationException($"Tool '{tool.Name}' is missing HttpConfig");

        if (string.IsNullOrWhiteSpace(tool.HttpConfig.Endpoint))
            throw new InvalidOperationException($"Tool '{tool.Name}' has empty Endpoint");

        if (!Uri.TryCreate(tool.HttpConfig.Endpoint, UriKind.Absolute, out _))
            // Allow relative URLs or templated URLs
            if (!tool.HttpConfig.Endpoint.Contains("{") && !tool.HttpConfig.Endpoint.StartsWith("/"))
                throw new InvalidOperationException(
                    $"Tool '{tool.Name}' has invalid Endpoint URL: {tool.HttpConfig.Endpoint}");

        var validMethods = new[] { "GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS" };
        if (!validMethods.Contains(tool.HttpConfig.Method.ToUpperInvariant()))
            throw new InvalidOperationException(
                $"Tool '{tool.Name}' has invalid HTTP method: {tool.HttpConfig.Method}");
    }

    /// <summary>
    ///     Validate and substitute parameters in endpoint URL with SSRF protection
    /// </summary>
    private string ValidateAndSubstituteEndpoint(string endpoint, Dictionary<string, object> parameters)
    {
        // Substitute parameters first
        var substitutedEndpoint = SubstituteParameters(endpoint, parameters);

        // Only validate absolute URLs
        if (!Uri.TryCreate(substitutedEndpoint, UriKind.Absolute,
                out var uri)) return substitutedEndpoint; // Return relative URLs as-is

        // SSRF protection - block private IP ranges
        if (uri.HostNameType == UriHostNameType.Dns)
        {
            // Block localhost and local domains
            var host = uri.Host.ToLowerInvariant();
            if (host == "localhost" || host.EndsWith(".local") || host.Contains("internal"))
                throw new InvalidOperationException(
                    $"Access to internal host '{host}' is not allowed for security reasons");
        }
        else if (uri.HostNameType == UriHostNameType.IPv4 || uri.HostNameType == UriHostNameType.IPv6)
        {
            // Block private IP ranges
            if (IPAddress.TryParse(uri.Host, out var ipAddress) && IsPrivateIp(ipAddress))
                throw new InvalidOperationException(
                    $"Access to private IP '{ipAddress}' is not allowed for security reasons");
        }

        // Only allow HTTP/HTTPS schemes
        if (uri.Scheme != "http" && uri.Scheme != "https")
            throw new InvalidOperationException($"Only HTTP and HTTPS schemes are allowed, got '{uri.Scheme}'");

        return substitutedEndpoint;
    }

    /// <summary>
    ///     Check if IP address is in private range
    /// </summary>
    private static bool IsPrivateIp(IPAddress ipAddress)
    {
        foreach (var (baseAddress, prefixLength) in PrivateIpRanges)
        {
            if (IPAddress.IsLoopback(ipAddress))
                return true;

            try
            {
                var network = new IPAddress(baseAddress.GetAddressBytes());
                var mask = IPAddress.Parse(string.Join(".", baseAddress.GetAddressBytes()
                    .Select((b, i) => i < prefixLength / 8 ? 255 : 0)));

                var maskedIp = IPAddress.Parse(string.Join(".", ipAddress.GetAddressBytes()
                    .Select((b, i) => (byte)(b & mask.GetAddressBytes()[i]))));

                if (maskedIp.Equals(network))
                    return true;
            }
            catch
            {
                // If parsing fails, be conservative and block
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Substitute {paramName} placeholders with actual values
    /// </summary>
    private string SubstituteParameters(string template, Dictionary<string, object> parameters)
    {
        var result = template;

        foreach (var (key, value) in parameters)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;

            // Sanitize parameter name to prevent injection
            if (!IsValidParameterName(key))
            {
                _logger.LogWarning("Skipping invalid parameter name: '{Key}'", key);
                continue;
            }

            var placeholder = $"{{{key}}}";
            var valueStr = SanitizeParameterValue(value?.ToString() ?? string.Empty);
            result = result.Replace(placeholder, valueStr);
        }

        // Handle environment variables with allowlist: ${ENV_VAR_NAME}
        result = Regex.Replace(result, @"\$\{([^}]+)\}", match =>
        {
            var envVar = match.Groups[1].Value;
            if (AllowedEnvVars.Contains(envVar)) return Environment.GetEnvironmentVariable(envVar) ?? string.Empty;

            _logger.LogWarning("Access to environment variable '{EnvVar}' is not allowed", envVar);
            return match.Value; // Return original placeholder if not allowed
        });

        return result;
    }

    /// <summary>
    ///     Validate parameter name to prevent injection
    /// </summary>
    private static bool IsValidParameterName(string name)
    {
        return !string.IsNullOrWhiteSpace(name)
               && name.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-')
               && name.Length <= 50;
    }

    /// <summary>
    ///     Sanitize parameter value to prevent injection
    /// </summary>
    private static string SanitizeParameterValue(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        // Remove potentially dangerous characters
        var sanitized = value
            .Replace("\r", "")
            .Replace("\n", "")
            .Replace("\0", "");

        // Limit length to prevent DoS
        return sanitized.Length > 1000 ? sanitized[..1000] : sanitized;
    }

    /// <summary>
    ///     Validate header key to prevent injection
    /// </summary>
    private static bool IsValidHeaderKey(string key)
    {
        return !string.IsNullOrWhiteSpace(key)
               && key.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_')
               && !key.Equals("Host", StringComparison.OrdinalIgnoreCase)
               && !key.Equals("Connection", StringComparison.OrdinalIgnoreCase)
               && !key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Validate header value to prevent injection
    /// </summary>
    private static bool IsValidHeaderValue(string value)
    {
        return value == null || (!value.Contains("\r") && !value.Contains("\n"));
    }

    /// <summary>
    ///     Apply authentication to HTTP request
    /// </summary>
    private void ApplyAuthentication(HttpRequestMessage request, HttpToolConfig config)
    {
        switch (config.AuthType.ToLowerInvariant())
        {
            case "bearer":
                if (!string.IsNullOrWhiteSpace(config.AuthToken))
                {
                    var token = ResolveEnvVar(config.AuthToken);
                    if (!string.IsNullOrEmpty(token))
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }

                break;

            case "basic":
                if (!string.IsNullOrWhiteSpace(config.Username) && !string.IsNullOrWhiteSpace(config.Password))
                {
                    var username = ResolveEnvVar(config.Username);
                    var password = ResolveEnvVar(config.Password);
                    if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
                    {
                        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
                        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                    }
                }

                break;

            case "apikey":
                if (!string.IsNullOrWhiteSpace(config.AuthToken))
                {
                    var token = ResolveEnvVar(config.AuthToken);
                    if (!string.IsNullOrEmpty(token)) request.Headers.Add("X-API-Key", token);
                }

                break;

            case "none":
            default:
                // No authentication
                break;
        }
    }

    /// <summary>
    ///     Resolve environment variable references: ${ENV_VAR_NAME} with allowlist
    /// </summary>
    private string ResolveEnvVar(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;

        return Regex.Replace(value, @"\$\{([^}]+)\}", match =>
        {
            var envVar = match.Groups[1].Value;
            if (AllowedEnvVars.Contains(envVar)) return Environment.GetEnvironmentVariable(envVar) ?? string.Empty;

            _logger.LogWarning("Access to environment variable '{EnvVar}' is not allowed", envVar);
            return string.Empty; // Return empty string for security
        });
    }

    /// <summary>
    ///     Extract data from JSON response using JSONPath
    /// </summary>
    private string ExtractResponseData(string responseContent, string? jsonPath)
    {
        if (string.IsNullOrWhiteSpace(jsonPath)) return responseContent;

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