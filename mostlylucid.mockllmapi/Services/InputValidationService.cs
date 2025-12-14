using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using mostlylucid.mockllmapi.Models;
using mostlylucid.mockllmapi.Utilities;

namespace mostlylucid.mockllmapi.Services;

/// <summary>
/// Provides comprehensive input validation for all LLMock API inputs
/// Prevents injection attacks, validates formats, and sanitizes data
/// </summary>
public interface IInputValidationService
{
    ValidationResult ValidateShape(string? shape);
    ValidationResult ValidateJourneyName(string? journeyName);
    ValidationResult ValidateToolName(string? toolName);
    ValidationResult ValidateJson(string json);
    ValidationResult ValidateEndpoint(string endpoint);
    ValidationResult ValidateParameter(string name, object? value);
    string SanitizeString(string input, int maxLength = 1000);
    
    /// <summary>
    /// Sanitizes input for use in LLM prompts, detecting and neutralizing prompt injection attempts
    /// </summary>
    string SanitizeForPrompt(string input, int maxLength = 2000);
    
    /// <summary>
    /// Checks if input contains potential prompt injection attempts
    /// </summary>
    ValidationResult ValidateForPromptInjection(string input);
}

/// <summary>
/// Result of input validation
/// </summary>
public record ValidationResult(bool IsValid, string? ErrorMessage = null);

/// <summary>
/// Implementation of input validation service
/// </summary>
public class InputValidationService : IInputValidationService
{
    private readonly ILogger<InputValidationService> _logger;

    public InputValidationService(ILogger<InputValidationService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Validates JSON shape input
    /// </summary>
    public ValidationResult ValidateShape(string? shape)
    {
        if (string.IsNullOrWhiteSpace(shape))
        {
            return new ValidationResult(true); // Empty shape is valid
        }

        // Check length
        if (shape.Length > 5000)
        {
            return new ValidationResult(false, "Shape too long (max 5000 characters)");
        }

        // Check for dangerous patterns
        var dangerousCheck = CheckForDangerousContent(shape);
        if (!dangerousCheck.IsValid)
        {
            return dangerousCheck;
        }

        // Try to parse as JSON to validate structure
        try
        {
            using var document = JsonDocument.Parse(shape);

            // Recursively validate all string values
            if (!ValidateJsonStrings(document.RootElement))
            {
                return new ValidationResult(false, "Shape contains dangerous content");
            }

            return new ValidationResult(true);
        }
        catch (JsonException ex)
        {
            return new ValidationResult(false, $"Invalid JSON format: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error validating shape");
            return new ValidationResult(false, "Validation error occurred");
        }
    }

    /// <summary>
    /// Validates journey name
    /// </summary>
    public ValidationResult ValidateJourneyName(string? journeyName)
    {
        if (string.IsNullOrWhiteSpace(journeyName))
        {
            return new ValidationResult(false, "Journey name is required");
        }

        if (journeyName.Length > 100)
        {
            return new ValidationResult(false, "Journey name too long (max 100 characters)");
        }

        if (!ValidationRegex.SafeNameRegex().IsMatch(journeyName))
        {
            return new ValidationResult(false, "Journey name contains invalid characters. Use only letters, numbers, underscores, and hyphens");
        }

        // Check for reserved words
        var reservedWords = new[] { "admin", "system", "root", "null", "undefined", "true", "false" };
        if (reservedWords.Contains(journeyName.ToLowerInvariant()))
        {
            return new ValidationResult(false, $"Journey name '{journeyName}' is reserved and cannot be used");
        }

        return new ValidationResult(true);
    }

    /// <summary>
    /// Validates tool name
    /// </summary>
    public ValidationResult ValidateToolName(string? toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return new ValidationResult(false, "Tool name is required");
        }

        if (toolName.Length > 50)
        {
            return new ValidationResult(false, "Tool name too long (max 50 characters)");
        }

        if (!ValidationRegex.SafeNameRegex().IsMatch(toolName))
        {
            return new ValidationResult(false, "Tool name contains invalid characters. Use only letters, numbers, underscores, and hyphens");
        }

        return new ValidationResult(true);
    }

    /// <summary>
    /// Validates JSON input
    /// </summary>
    public ValidationResult ValidateJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ValidationResult(false, "JSON input is required");
        }

        if (json.Length > 10000)
        {
            return new ValidationResult(false, "JSON too long (max 10000 characters)");
        }

        // Check for dangerous patterns
        var dangerousCheck = CheckForDangerousContent(json);
        if (!dangerousCheck.IsValid)
        {
            return dangerousCheck;
        }

        try
        {
            using var document = JsonDocument.Parse(json);

            // Recursively validate all string values
            if (!ValidateJsonStrings(document.RootElement))
            {
                return new ValidationResult(false, "JSON contains dangerous content");
            }

            return new ValidationResult(true);
        }
        catch (JsonException ex)
        {
            return new ValidationResult(false, $"Invalid JSON format: {ex.Message}");
        }
    }

    /// <summary>
    /// Validates endpoint URL
    /// </summary>
    public ValidationResult ValidateEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return new ValidationResult(false, "Endpoint is required");
        }

        if (endpoint.Length > 500)
        {
            return new ValidationResult(false, "Endpoint too long (max 500 characters)");
        }

        // Check for dangerous patterns
        var dangerousCheck = CheckForDangerousContent(endpoint);
        if (!dangerousCheck.IsValid)
        {
            return dangerousCheck;
        }

        // Validate URI format
        if (Uri.TryCreate(endpoint, UriKind.RelativeOrAbsolute, out var uri))
        {
            // For absolute URIs, check scheme
            if (uri.IsAbsoluteUri)
            {
                var allowedSchemes = new[] { "http", "https" };
                if (!allowedSchemes.Contains(uri.Scheme.ToLowerInvariant()))
                {
                    return new ValidationResult(false, $"Invalid URI scheme: {uri.Scheme}. Only HTTP and HTTPS are allowed");
                }

                // Check for suspicious host patterns
                if (uri.HostNameType == UriHostNameType.Dns)
                {
                    var host = uri.Host.ToLowerInvariant();
                    if (host.Contains("localhost") || host.Contains("internal") || host.EndsWith(".local"))
                    {
                        _logger.LogWarning("Potentially unsafe host detected: {Host}", host);
                        // Allow but log warning for internal hosts in tool context
                    }
                }
            }

            return new ValidationResult(true);
        }

        // Allow templated URLs (with {param} placeholders)
        if (endpoint.Contains("{") && endpoint.Contains("}"))
        {
            var tempEndpoint = Regex.Replace(endpoint, @"\{[^}]*\}", "placeholder");
            return ValidateEndpoint(tempEndpoint);
        }

        return new ValidationResult(false, "Invalid endpoint format");
    }

    /// <summary>
    /// Validates parameter name and value
    /// </summary>
    public ValidationResult ValidateParameter(string name, object? value)
    {
        // Validate parameter name
        if (string.IsNullOrWhiteSpace(name))
        {
            return new ValidationResult(false, "Parameter name is required");
        }

        if (name.Length > 50)
        {
            return new ValidationResult(false, "Parameter name too long (max 50 characters)");
        }

        if (!ValidationRegex.SafeNameRegex().IsMatch(name))
        {
            return new ValidationResult(false, "Parameter name contains invalid characters");
        }

        // Check for reserved parameter names
        var reservedNames = new[] { "error", "errors", "exception", "stack", "trace" };
        if (reservedNames.Contains(name.ToLowerInvariant()))
        {
            _logger.LogWarning("Reserved parameter name used: {Name}", name);
        }

        // Validate parameter value
        if (value != null)
        {
            var valueStr = value.ToString() ?? string.Empty;

            if (valueStr.Length > 2000)
            {
                return new ValidationResult(false, "Parameter value too long (max 2000 characters)");
            }

            var dangerousCheck = CheckForDangerousContent(valueStr);
            if (!dangerousCheck.IsValid)
            {
                return new ValidationResult(false, $"Parameter '{name}' contains dangerous content: {dangerousCheck.ErrorMessage}");
            }
        }

        return new ValidationResult(true);
    }

    /// <summary>
    /// Sanitizes string input by removing dangerous content
    /// </summary>
    public string SanitizeString(string input, int maxLength = 1000)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        // Truncate if too long
        if (input.Length > maxLength)
        {
            input = input[..maxLength];
        }

        // Remove dangerous patterns
        var sanitized = input;
        foreach (var regex in ValidationRegex.DangerousPatterns)
        {
            sanitized = regex.Replace(sanitized, "");
        }

        // Remove control characters except newlines and tabs
        sanitized = ValidationRegex.ControlCharacterRegex().Replace(sanitized, "");

        // Normalize whitespace
        sanitized = ValidationRegex.WhitespaceRegex().Replace(sanitized, " ").Trim();

        return sanitized;
    }

    /// <summary>
    /// Sanitizes input for use in LLM prompts, detecting and neutralizing prompt injection attempts.
    /// This method wraps user input in clear delimiters and escapes potential injection patterns.
    /// </summary>
    public string SanitizeForPrompt(string input, int maxLength = 2000)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        // Truncate if too long
        if (input.Length > maxLength)
        {
            input = input[..maxLength];
            _logger.LogWarning("Input truncated from {OriginalLength} to {MaxLength} characters for prompt safety", 
                input.Length + (input.Length - maxLength), maxLength);
        }

        var sanitized = input;

        // Remove control characters
        sanitized = ValidationRegex.ControlCharacterRegex().Replace(sanitized, "");

        // Neutralize prompt injection patterns by replacing with safe placeholders
        foreach (var regex in ValidationRegex.PromptInjectionPatterns)
        {
            if (regex.IsMatch(sanitized))
            {
                _logger.LogWarning("Potential prompt injection attempt detected and neutralized: {Pattern}", regex.ToString());
                sanitized = regex.Replace(sanitized, "[FILTERED]");
            }
        }

        // Escape delimiter characters that could break out of user input sections
        sanitized = sanitized
            .Replace("```", "'''")  // Markdown code blocks
            .Replace("---", "___")  // Markdown horizontal rules
            .Replace("[[", "[")     // Wiki-style links
            .Replace("]]", "]")
            .Replace("<<", "<")     // Angle bracket sequences
            .Replace(">>", ">");

        // Normalize whitespace but preserve single newlines for readability
        sanitized = Regex.Replace(sanitized, @"[ \t]+", " ");  // Collapse spaces/tabs
        sanitized = Regex.Replace(sanitized, @"\n{3,}", "\n\n");  // Max 2 consecutive newlines

        return sanitized.Trim();
    }

    /// <summary>
    /// Checks if input contains potential prompt injection attempts.
    /// Returns validation result indicating if the input is safe.
    /// </summary>
    public ValidationResult ValidateForPromptInjection(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return new ValidationResult(true);
        }

        foreach (var regex in ValidationRegex.PromptInjectionPatterns)
        {
            if (regex.IsMatch(input))
            {
                var patternName = GetPatternName(regex);
                _logger.LogWarning("Prompt injection pattern detected: {PatternName} in input", patternName);
                return new ValidationResult(false, $"Input contains potential prompt injection attempt ({patternName})");
            }
        }

        // Check for excessive repetition (common in some attacks)
        if (HasExcessiveRepetition(input))
        {
            return new ValidationResult(false, "Input contains suspicious repetitive patterns");
        }

        return new ValidationResult(true);
    }

    /// <summary>
    /// Gets a human-readable name for a regex pattern
    /// </summary>
    private static string GetPatternName(Regex regex)
    {
        var pattern = regex.ToString();
        
        if (pattern.Contains("ignore", StringComparison.OrdinalIgnoreCase)) return "ignore-instructions";
        if (pattern.Contains("forget", StringComparison.OrdinalIgnoreCase)) return "forget-context";
        if (pattern.Contains("disregard", StringComparison.OrdinalIgnoreCase)) return "disregard-rules";
        if (pattern.Contains("new.*instructions", StringComparison.OrdinalIgnoreCase)) return "new-instructions";
        if (pattern.Contains("reveal|show", StringComparison.OrdinalIgnoreCase)) return "reveal-prompt";
        if (pattern.Contains("pretend|act", StringComparison.OrdinalIgnoreCase)) return "roleplay-jailbreak";
        if (pattern.Contains("DAN", StringComparison.OrdinalIgnoreCase)) return "dan-jailbreak";
        if (pattern.Contains("```|---", StringComparison.OrdinalIgnoreCase)) return "delimiter-escape";
        
        return "unknown-pattern";
    }

    /// <summary>
    /// Checks for excessive character or word repetition
    /// </summary>
    private static bool HasExcessiveRepetition(string input)
    {
        // Check for same character repeated more than 20 times
        for (int i = 0; i < input.Length - 20; i++)
        {
            var allSame = true;
            for (int j = 1; j < 20 && allSame; j++)
            {
                if (input[i] != input[i + j]) allSame = false;
            }
            if (allSame) return true;
        }

        // Check for same word repeated more than 10 times consecutively
        var words = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 10)
        {
            var repeatCount = 1;
            for (int i = 1; i < words.Length; i++)
            {
                if (words[i].Equals(words[i - 1], StringComparison.OrdinalIgnoreCase))
                {
                    repeatCount++;
                    if (repeatCount >= 10) return true;
                }
                else
                {
                    repeatCount = 1;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Checks for dangerous content patterns
    /// </summary>
    private ValidationResult CheckForDangerousContent(string input)
    {
        foreach (var regex in ValidationRegex.DangerousPatterns)
        {
            if (regex.IsMatch(input))
            {
                return new ValidationResult(false, "Input contains potentially dangerous content");
            }
        }

        // Check for excessive nesting (potential DoS)
        var openBraces = input.Count(c => c == '{');
        var closeBraces = input.Count(c => c == '}');
        if (Math.Abs(openBraces - closeBraces) > 10)
        {
            return new ValidationResult(false, "Input has unbalanced braces (potential injection attempt)");
        }

        return new ValidationResult(true);
    }

    /// <summary>
    /// Recursively validates all string values in JSON
    /// </summary>
    private bool ValidateJsonStrings(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var strValue = element.GetString() ?? string.Empty;
                var validation = CheckForDangerousContent(strValue);
                return validation.IsValid;

            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (!ValidateJsonStrings(property.Value))
                    {
                        return false;
                    }
                }
                return true;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (!ValidateJsonStrings(item))
                    {
                        return false;
                    }
                }
                return true;

            default:
                return true;
        }
    }
}
