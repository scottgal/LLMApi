using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace mostlylucid.mockllmapi;

/// <summary>
///     Middleware for authenticating management endpoints.
///     Supports HMAC-SHA256 signed JWT tokens when ManagementAuthSecret is configured,
///     falls back to simple API key validation otherwise.
/// </summary>
public class AuthMiddleware
{
    // Clock skew tolerance for JWT expiration validation (5 minutes)
    private static readonly TimeSpan ClockSkewTolerance = TimeSpan.FromMinutes(5);
    private readonly ILogger<AuthMiddleware> _logger;
    private readonly RequestDelegate _next;
    private readonly IOptions<LLMockApiOptions> _options;

    public AuthMiddleware(
        RequestDelegate next,
        IOptions<LLMockApiOptions> options,
        ILogger<AuthMiddleware> logger)
    {
        _next = next;
        _options = options;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip authentication for non-management endpoints
        if (!IsManagementEndpoint(context.Request.Path))
        {
            await _next(context);
            return;
        }

        // Check if authentication is enabled
        if (!_options.Value.EnableManagementAuth)
        {
            await _next(context);
            return;
        }

        try
        {
            // Extract token from header or query parameter
            var token = ExtractToken(context.Request);

            if (string.IsNullOrEmpty(token))
            {
                await HandleUnauthorized(context, "Missing authentication token");
                return;
            }

            // Log warning if token was extracted from query string (less secure)
            if (context.Request.Query.ContainsKey("token"))
                _logger.LogWarning(
                    "Authentication token provided via query parameter. This is less secure than using the Authorization header. " +
                    "Query parameters may be logged by proxies and appear in browser history.");

            // Validate token
            var validationResult = ValidateToken(token);
            if (!validationResult.IsValid)
            {
                _logger.LogWarning("Token validation failed: {Reason}", validationResult.ErrorReason);
                await HandleUnauthorized(context, validationResult.ErrorReason ?? "Invalid token");
                return;
            }

            // Set user principal
            context.User = validationResult.Principal!;

            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Authentication error");
            await HandleUnauthorized(context, "Authentication failed");
        }
    }

    private bool IsManagementEndpoint(PathString path)
    {
        // Management endpoints typically start with /api/management or similar
        return path.StartsWithSegments("/api/management") ||
               path.StartsWithSegments("/api/context") ||
               path.StartsWithSegments("/api/journey") ||
               path.StartsWithSegments("/api/openapi") ||
               path.StartsWithSegments("/api/grpc") ||
               path.StartsWithSegments("/api/signalr") ||
               path.StartsWithSegments("/api/tool") ||
               path.StartsWithSegments("/api/unittest");
    }

    private string? ExtractToken(HttpRequest request)
    {
        // Check Authorization header first (preferred)
        if (request.Headers.TryGetValue("Authorization", out var authHeader) &&
            authHeader.Count > 0)
        {
            var authValue = authHeader[0];
            if (authValue != null && authValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return authValue["Bearer ".Length..].Trim();
        }

        // Check X-API-Key header (alternative for simple API key auth)
        if (request.Headers.TryGetValue("X-API-Key", out var apiKeyHeader) &&
            apiKeyHeader.Count > 0)
            return apiKeyHeader[0];

        // Check query parameter as last resort (least secure - logs warning)
        if (request.Query.TryGetValue("token", out var tokenParam) && tokenParam.Count > 0) return tokenParam[0];

        return null;
    }

    /// <summary>
    ///     Validates the provided token using HMAC-SHA256 JWT validation if ManagementAuthSecret is configured,
    ///     otherwise falls back to simple string comparison (for API key style auth).
    /// </summary>
    private TokenValidationResult ValidateToken(string token)
    {
        var secret = _options.Value.ManagementAuthSecret;

        // If no secret is configured, reject all tokens
        if (string.IsNullOrWhiteSpace(secret))
        {
            _logger.LogWarning("ManagementAuthSecret is not configured. Authentication will fail for all requests.");
            return TokenValidationResult.Failed("Authentication not properly configured");
        }

        // Try JWT validation first (if token has JWT structure)
        if (IsJwtFormat(token)) return ValidateJwtToken(token, secret);

        // Fall back to simple API key comparison
        return ValidateApiKey(token, secret);
    }

    /// <summary>
    ///     Checks if the token appears to be a JWT (three base64 segments separated by dots)
    /// </summary>
    private static bool IsJwtFormat(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3)
            return false;

        // Verify each part is valid base64url
        foreach (var part in parts)
            if (!IsValidBase64Url(part))
                return false;

        return true;
    }

    /// <summary>
    ///     Validates base64url encoding
    /// </summary>
    private static bool IsValidBase64Url(string input)
    {
        if (string.IsNullOrEmpty(input))
            return false;

        // Base64url uses only these characters
        return Regex.IsMatch(input, @"^[A-Za-z0-9_-]*$");
    }

    /// <summary>
    ///     Validates a JWT token using HMAC-SHA256
    /// </summary>
    private TokenValidationResult ValidateJwtToken(string token, string secret)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3) return TokenValidationResult.Failed("Invalid JWT format");

            var header = parts[0];
            var payload = parts[1];
            var signature = parts[2];

            // Verify signature
            var signatureInput = $"{header}.{payload}";
            var keyBytes = Encoding.UTF8.GetBytes(secret);
            var dataBytes = Encoding.UTF8.GetBytes(signatureInput);

            using var hmac = new HMACSHA256(keyBytes);
            var computedHash = hmac.ComputeHash(dataBytes);
            var computedSignature = Base64UrlEncode(computedHash);

            // Constant-time comparison to prevent timing attacks
            if (!CryptographicEquals(signature, computedSignature))
                return TokenValidationResult.Failed("Invalid signature");

            // Decode and parse payload
            var payloadJson = Base64UrlDecode(payload);
            using var payloadDoc = JsonDocument.Parse(payloadJson);
            var payloadRoot = payloadDoc.RootElement;

            // Validate expiration (exp claim)
            if (payloadRoot.TryGetProperty("exp", out var expElement))
            {
                var expUnix = expElement.GetInt64();
                var expTime = DateTimeOffset.FromUnixTimeSeconds(expUnix);
                var now = DateTimeOffset.UtcNow;

                if (now > expTime.Add(ClockSkewTolerance)) return TokenValidationResult.Failed("Token has expired");
            }

            // Validate not before (nbf claim)
            if (payloadRoot.TryGetProperty("nbf", out var nbfElement))
            {
                var nbfUnix = nbfElement.GetInt64();
                var nbfTime = DateTimeOffset.FromUnixTimeSeconds(nbfUnix);
                var now = DateTimeOffset.UtcNow;

                if (now < nbfTime.Subtract(ClockSkewTolerance))
                    return TokenValidationResult.Failed("Token not yet valid");
            }

            // Validate issuer (iss claim) if configured
            // For now, just log the issuer for debugging
            if (payloadRoot.TryGetProperty("iss", out var issElement))
                _logger.LogDebug("Token issued by: {Issuer}", issElement.GetString());

            // Extract claims and create principal
            var claims = new List<Claim>();

            // Standard claims
            if (payloadRoot.TryGetProperty("sub", out var subElement))
            {
                claims.Add(new Claim(ClaimTypes.NameIdentifier, subElement.GetString() ?? "unknown"));
                claims.Add(new Claim(ClaimTypes.Name, subElement.GetString() ?? "unknown"));
            }

            // Role claims
            if (payloadRoot.TryGetProperty("role", out var roleElement))
            {
                if (roleElement.ValueKind == JsonValueKind.Array)
                    foreach (var role in roleElement.EnumerateArray())
                        claims.Add(new Claim(ClaimTypes.Role, role.GetString() ?? ""));
                else
                    claims.Add(new Claim(ClaimTypes.Role, roleElement.GetString() ?? ""));
            }
            else
            {
                // Default to admin role for management API access
                claims.Add(new Claim(ClaimTypes.Role, "admin"));
            }

            // Add any custom claims
            foreach (var property in payloadRoot.EnumerateObject())
            {
                if (property.Name is "sub" or "role" or "exp" or "nbf" or "iat" or "iss" or "aud")
                    continue; // Skip standard claims

                if (property.Value.ValueKind == JsonValueKind.String)
                    claims.Add(new Claim(property.Name, property.Value.GetString() ?? ""));
            }

            var identity = new ClaimsIdentity(claims, "Bearer");
            var principal = new ClaimsPrincipal(identity);

            return TokenValidationResult.Success(principal);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse JWT payload");
            return TokenValidationResult.Failed("Invalid JWT payload format");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JWT validation error");
            return TokenValidationResult.Failed("Token validation error");
        }
    }

    /// <summary>
    ///     Validates a simple API key by comparing against the configured secret
    /// </summary>
    private TokenValidationResult ValidateApiKey(string apiKey, string secret)
    {
        // Use constant-time comparison to prevent timing attacks
        if (!CryptographicEquals(apiKey, secret)) return TokenValidationResult.Failed("Invalid API key");

        // Create a simple principal for API key auth
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "api-user"),
            new Claim(ClaimTypes.Role, "admin"),
            new Claim("auth_method", "api_key")
        };

        var identity = new ClaimsIdentity(claims, "ApiKey");
        var principal = new ClaimsPrincipal(identity);

        return TokenValidationResult.Success(principal);
    }

    /// <summary>
    ///     Constant-time string comparison to prevent timing attacks
    /// </summary>
    private static bool CryptographicEquals(string a, string b)
    {
        if (a == null || b == null)
            return false;

        var aBytes = Encoding.UTF8.GetBytes(a);
        var bBytes = Encoding.UTF8.GetBytes(b);

        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }

    /// <summary>
    ///     Base64Url encoding (without padding)
    /// </summary>
    private static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>
    ///     Base64Url decoding
    /// </summary>
    private static string Base64UrlDecode(string input)
    {
        var padded = input
            .Replace('-', '+')
            .Replace('_', '/');

        // Add padding if needed
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }

        var bytes = Convert.FromBase64String(padded);
        return Encoding.UTF8.GetString(bytes);
    }

    private async Task HandleUnauthorized(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";

        // Add WWW-Authenticate header for proper HTTP auth protocol
        context.Response.Headers.WWWAuthenticate = "Bearer realm=\"MockLLMApi Management\", error=\"invalid_token\"";

        var errorResponse = new
        {
            error = "Unauthorized",
            message = SanitizeErrorMessage(message),
            timestamp = DateTime.UtcNow,
            hint =
                "Provide a valid Bearer token via Authorization header, or set ManagementAuthSecret and use it as X-API-Key header"
        };
        await context.Response.WriteAsJsonAsync(errorResponse);
    }

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
        sanitized = Regex.Replace(sanitized, @"https?://\S+", "[REDACTED_URL]");
        sanitized = Regex.Replace(sanitized, @"file://\S+", "[REDACTED_FILE]");
        sanitized = Regex.Replace(sanitized, @"[a-zA-Z]:\\[^\\]+", "[REDACTED_PATH]");

        return sanitized;
    }

    /// <summary>
    ///     Result of token validation
    /// </summary>
    private class TokenValidationResult
    {
        public bool IsValid { get; private set; }
        public ClaimsPrincipal? Principal { get; private set; }
        public string? ErrorReason { get; private set; }

        public static TokenValidationResult Success(ClaimsPrincipal principal)
        {
            return new TokenValidationResult { IsValid = true, Principal = principal };
        }

        public static TokenValidationResult Failed(string reason)
        {
            return new TokenValidationResult { IsValid = false, ErrorReason = reason };
        }
    }
}

/// <summary>
///     Extension method to add authentication middleware
/// </summary>
public static class AuthMiddlewareExtensions
{
    public static IApplicationBuilder UseManagementAuth(
        this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<AuthMiddleware>();
    }
}