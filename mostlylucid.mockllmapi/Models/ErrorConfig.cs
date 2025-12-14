namespace mostlylucid.mockllmapi.Models;

/// <summary>
/// Configuration for simulating error responses
/// </summary>
public class ErrorConfig
{
    /// <summary>
    /// HTTP status code to return (e.g., 400, 401, 404, 500)
    /// </summary>
    public int StatusCode { get; set; }

    /// <summary>
    /// Custom error message. If null, uses default message for status code
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Optional error details or additional context
    /// </summary>
    public string? Details { get; set; }

    /// <summary>
    /// Creates an ErrorConfig with just a status code
    /// </summary>
    public ErrorConfig(int statusCode)
    {
        StatusCode = statusCode;
    }

    /// <summary>
    /// Creates an ErrorConfig with status code and message
    /// </summary>
    public ErrorConfig(int statusCode, string? message, string? details = null)
    {
        StatusCode = statusCode;
        Message = message;
        Details = details;
    }

    /// <summary>
    /// Gets a default error message for common HTTP status codes
    /// </summary>
    public string GetDefaultMessage()
    {
        return StatusCode switch
        {
            400 => "Bad Request",
            401 => "Unauthorized",
            403 => "Forbidden",
            404 => "Not Found",
            405 => "Method Not Allowed",
            408 => "Request Timeout",
            409 => "Conflict",
            422 => "Unprocessable Entity",
            429 => "Too Many Requests",
            500 => "Internal Server Error",
            501 => "Not Implemented",
            502 => "Bad Gateway",
            503 => "Service Unavailable",
            504 => "Gateway Timeout",
            _ => $"Error {StatusCode}"
        };
    }

    /// <summary>
    /// Gets the error message to use (custom or default)
    /// </summary>
    public string GetMessage()
    {
        return Message ?? GetDefaultMessage();
    }

/// <summary>
    /// Formats the error as a JSON response
    /// </summary>
    public string ToJson()
    {
        var message = SanitizeErrorMessage(GetMessage());
        var details = SanitizeErrorMessage(Details);

        if (!string.IsNullOrWhiteSpace(details))
        {
            return $$"""
            {
              "error": {
                "code": {{StatusCode}},
                "message": "{{EscapeJson(message)}}",
                "details": "{{EscapeJson(details)}}"
              }
            }
            """;
        }

        return $$"""
        {
          "error": {
            "code": {{StatusCode}},
            "message": "{{EscapeJson(message)}}"
          }
        }
        """;
    }

    /// <summary>
    /// Formats the error as a GraphQL error response
    /// </summary>
    public string ToGraphQLJson()
    {
        var message = SanitizeErrorMessage(GetMessage());
        var details = SanitizeErrorMessage(Details);

        var detailsSection = !string.IsNullOrWhiteSpace(details)
            ? $$""", "extensions": { "details": "{{EscapeJson(details)}}" }"""
            : "";

        return $$"""
        {
          "errors": [
            {
              "message": "{{EscapeJson(message)}}",
              "extensions": {
                "code": {{StatusCode}}
              }{{detailsSection}}
            }
          ],
          "data": null
        }
        """;
    }

    /// <summary>
    /// Sanitizes error messages to prevent sensitive information disclosure
    /// </summary>
    private static string SanitizeErrorMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return string.Empty;

        // Remove potentially sensitive information
        var sanitized = message
            .Replace("password", "[REDACTED]", StringComparison.OrdinalIgnoreCase)
            .Replace("secret", "[REDACTED]", StringComparison.OrdinalIgnoreCase)
            .Replace("token", "[REDACTED]", StringComparison.OrdinalIgnoreCase)
            .Replace("key", "[REDACTED]", StringComparison.OrdinalIgnoreCase)
            .Replace("credential", "[REDACTED]", StringComparison.OrdinalIgnoreCase)
            .Replace("auth", "[REDACTED]", StringComparison.OrdinalIgnoreCase)
            .Replace("authorization", "[REDACTED]", StringComparison.OrdinalIgnoreCase)
            .Replace("api_key", "[REDACTED]", StringComparison.OrdinalIgnoreCase)
            .Replace("api_key=", "[REDACTED]", StringComparison.OrdinalIgnoreCase)
            .Replace("bearer ", "[REDACTED] ", StringComparison.OrdinalIgnoreCase)
            .Replace("token=", "[REDACTED]", StringComparison.OrdinalIgnoreCase);

        // Remove any URLs or file paths
        sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"https?://\S+", "[REDACTED_URL]");
        sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"file://\S+", "[REDACTED_FILE]");
        sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"[a-zA-Z]:\\[^\\]+", "[REDACTED_PATH]");

        return sanitized;
    }

    private static string EscapeJson(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }
}
