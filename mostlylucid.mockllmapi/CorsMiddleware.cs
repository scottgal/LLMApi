using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace mostlylucid.mockllmapi;

/// <summary>
///     Middleware for handling CORS (Cross-Origin Resource Sharing)
///     Validates configuration to prevent insecure combinations.
/// </summary>
public class CorsMiddleware
{
    private readonly bool _hasInsecureConfiguration;
    private readonly ILogger<CorsMiddleware> _logger;
    private readonly RequestDelegate _next;
    private readonly IOptions<LLMockApiOptions> _options;

    public CorsMiddleware(
        RequestDelegate next,
        IOptions<LLMockApiOptions> options,
        ILogger<CorsMiddleware> logger)
    {
        _next = next;
        _options = options;
        _logger = logger;

        // Validate CORS configuration on startup
        _hasInsecureConfiguration = ValidateCorsConfiguration(options.Value.Cors);
    }

    /// <summary>
    ///     Validates CORS configuration and logs warnings for insecure settings.
    ///     Returns true if configuration has security issues.
    /// </summary>
    private bool ValidateCorsConfiguration(CorsOptions corsOptions)
    {
        if (!corsOptions.Enabled)
            return false;

        var hasIssues = false;

        // CRITICAL: AllowCredentials + Wildcard origin is forbidden by CORS spec
        // Browsers will reject this combination, and it's a security vulnerability
        if (corsOptions.AllowCredentials && corsOptions.AllowedOrigins.Contains("*"))
        {
            _logger.LogError(
                "SECURITY ISSUE: CORS configuration has AllowCredentials=true with wildcard (*) origin. " +
                "This is forbidden by the CORS specification and browsers will reject such responses. " +
                "Either set AllowCredentials=false OR specify explicit origins instead of '*'. " +
                "See: https://developer.mozilla.org/en-US/docs/Web/HTTP/CORS/Errors/CORSNotSupportingCredentials");
            hasIssues = true;
        }

        // Warn about overly permissive wildcard origins without credentials
        if (corsOptions.AllowedOrigins.Contains("*") && !corsOptions.AllowCredentials)
            _logger.LogWarning(
                "CORS is configured with wildcard (*) origin. " +
                "This allows any website to make requests to your API. " +
                "Consider restricting to specific trusted origins in production.");

        // Warn about allowing credentials (even with explicit origins)
        if (corsOptions.AllowCredentials && !corsOptions.AllowedOrigins.Contains("*"))
            _logger.LogInformation(
                "CORS is configured with AllowCredentials=true for specific origins: {Origins}. " +
                "Ensure these origins are trusted as they can make authenticated requests.",
                string.Join(", ", corsOptions.AllowedOrigins));

        return hasIssues;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var corsOptions = _options.Value.Cors;

        // Skip CORS for non-browser requests or if disabled
        if (!corsOptions.Enabled || !IsBrowserRequest(context.Request))
        {
            await _next(context);
            return;
        }

        // Handle preflight requests
        if (context.Request.Method == "OPTIONS" &&
            context.Request.Headers.ContainsKey("Origin"))
        {
            await HandlePreflightRequest(context, corsOptions);
            return;
        }

        // Add CORS headers to the response
        await AddCorsHeaders(context.Response, context.Request, corsOptions);
        await _next(context);
    }

    private bool IsBrowserRequest(HttpRequest request)
    {
        var userAgent = request.Headers["User-Agent"].ToString();
        return !string.IsNullOrEmpty(userAgent) &&
               (userAgent.Contains("Mozilla", StringComparison.OrdinalIgnoreCase) ||
                userAgent.Contains("Chrome", StringComparison.OrdinalIgnoreCase) ||
                userAgent.Contains("Safari", StringComparison.OrdinalIgnoreCase));
    }

    private async Task HandlePreflightRequest(HttpContext context, CorsOptions corsOptions)
    {
        context.Response.StatusCode = StatusCodes.Status204NoContent;

        await AddCorsHeaders(context.Response, context.Request, corsOptions);

        // Set preflight response headers
        context.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, DELETE, OPTIONS";
        context.Response.Headers["Access-Control-Allow-Headers"] =
            "Content-Type, Authorization, X-Requested-With, X-LLM-Backend, X-Shape, X-Auto-Shape, X-Renew-Shape, X-Use-Tool, X-Rate-Limit-Delay, X-Rate-Limit-Strategy";
        context.Response.Headers["Access-Control-Max-Age"] = corsOptions.PreflightMaxAge.ToString();

        _logger.LogInformation("Handled CORS preflight request from {Origin}",
            context.Request.Headers["Origin"].ToString());
    }

    private Task AddCorsHeaders(HttpResponse response, HttpRequest request, CorsOptions corsOptions)
    {
        var origin = request.Headers["Origin"].ToString();

        // Check if origin is allowed
        if (IsOriginAllowed(origin, corsOptions))
        {
            // Handle credentials + wildcard safely
            var effectiveAllowCredentials = corsOptions.AllowCredentials;

            if (corsOptions.AllowedOrigins.Contains("*"))
            {
                // When using wildcard, we must set the actual origin header (not *)
                // if credentials are requested, BUT we should also block credentials
                if (corsOptions.AllowCredentials)
                {
                    // SECURITY: Block the insecure combination at runtime
                    _logger.LogWarning(
                        "Blocked credentials for wildcard CORS request from {Origin}. " +
                        "AllowCredentials=true with wildcard origins is insecure.",
                        origin);
                    effectiveAllowCredentials = false;
                }

                // Set the specific origin instead of * for better compatibility
                response.Headers["Access-Control-Allow-Origin"] = origin;
            }
            else
            {
                response.Headers["Access-Control-Allow-Origin"] = origin;
            }

            response.Headers["Vary"] = "Origin";

            if (effectiveAllowCredentials) response.Headers["Access-Control-Allow-Credentials"] = "true";

            if (!string.IsNullOrEmpty(corsOptions.ExposedHeaders))
                response.Headers["Access-Control-Expose-Headers"] = corsOptions.ExposedHeaders;

            _logger.LogDebug("Added CORS headers for origin: {Origin}", origin);
        }
        else
        {
            _logger.LogWarning("CORS request blocked from unauthorized origin: {Origin}", origin);
        }

        return Task.CompletedTask;
    }

    private bool IsOriginAllowed(string origin, CorsOptions corsOptions)
    {
        if (string.IsNullOrEmpty(origin))
            return false;

        // Allow all origins if configured
        if (corsOptions.AllowedOrigins.Contains("*"))
            return true;

        // Check if origin is in the allowed list
        return corsOptions.AllowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>
///     Extension method to add CORS middleware
/// </summary>
public static class CorsMiddlewareExtensions
{
    public static IApplicationBuilder UseCors(
        this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<CorsMiddleware>();
    }
}

/// <summary>
///     CORS configuration options
/// </summary>
public class CorsOptions
{
    /// <summary>
    ///     Enable CORS (default: false)
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    ///     List of allowed origins (default: ["*"])
    /// </summary>
    public List<string> AllowedOrigins { get; set; } = new() { "*" };

    /// <summary>
    ///     Allow credentials (cookies, authorization headers, etc.) (default: false)
    /// </summary>
    public bool AllowCredentials { get; set; } = false;

    /// <summary>
    ///     Comma-separated list of exposed headers (default: empty)
    /// </summary>
    public string? ExposedHeaders { get; set; }

    /// <summary>
    ///     Preflight request max age in seconds (default: 86400 = 24 hours)
    /// </summary>
    public int PreflightMaxAge { get; set; } = 86400;
}