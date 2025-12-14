using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using mostlylucid.mockllmapi.Models;
using mostlylucid.mockllmapi.Services;
using mostlylucid.mockllmapi.Utilities;

namespace mostlylucid.mockllmapi;

/// <summary>
/// Middleware for rate limiting all endpoints with per-client isolation.
/// Uses PartitionedRateLimiter to ensure one abusive client cannot exhaust limits for others.
/// </summary>
public class RateLimitMiddleware : IDisposable
{
    private readonly RequestDelegate _next;
    private readonly IOptions<LLMockApiOptions> _options;
    private readonly ILogger<RateLimitMiddleware> _logger;
    private readonly PartitionedRateLimiter<string> _rateLimiter;
    private readonly ConcurrentDictionary<string, DateTime> _clientLastSeen;
    private readonly Timer _cleanupTimer;
    private bool _disposed;

    public RateLimitMiddleware(
        RequestDelegate next,
        IOptions<LLMockApiOptions> options,
        ILogger<RateLimitMiddleware> logger)
    {
        _next = next;
        _options = options;
        _logger = logger;
        _rateLimiter = CreatePartitionedRateLimiter();
        _clientLastSeen = new ConcurrentDictionary<string, DateTime>();
        
        // Clean up stale client entries every 5 minutes to prevent memory leaks
        _cleanupTimer = new Timer(CleanupStaleClients, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.Value.EnableRateLimiting)
        {
            await _next(context);
            return;
        }

        try
        {
            // Check request size limit
            var requestSizeLimit = _options.Value.MaxRequestSizeBytes;
            if (requestSizeLimit > 0 && context.Request.ContentLength > requestSizeLimit)
            {
                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                var errorMessage = SanitizeErrorMessage($"Request size exceeds maximum allowed size of {requestSizeLimit / (1024 * 1024)}MB");
                await context.Response.WriteAsync(errorMessage);
                _logger.LogWarning("Request size exceeded limit for endpoint: {Path}", context.Request.Path);
                return;
            }

            // Get client identifier for partitioned rate limiting
            var clientId = GetClientIdentifier(context);
            _clientLastSeen[clientId] = DateTime.UtcNow;
            
            using var lease = await _rateLimiter.AcquireAsync(clientId);
            
            if (lease.IsAcquired)
            {
                // Add rate limit headers for client visibility
                AddRateLimitHeaders(context.Response, lease);
                await _next(context);
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                
                // Calculate retry-after based on window configuration
                var retryAfter = CalculateRetryAfter(lease);
                context.Response.Headers["Retry-After"] = retryAfter.ToString();
                
                AddRateLimitHeaders(context.Response, lease);
                
                var errorMessage = SanitizeErrorMessage("Rate limit exceeded. Please try again later.");
                await context.Response.WriteAsync(errorMessage);
                _logger.LogWarning(
                    "Rate limit exceeded for client {ClientId} on endpoint: {Path}",
                    SanitizeClientId(clientId),
                    context.Request.Path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rate limiting error");
            await _next(context);
        }
    }

    /// <summary>
    /// Gets a unique identifier for the client.
    /// Priority: API Key > X-Forwarded-For > Remote IP
    /// </summary>
    private string GetClientIdentifier(HttpContext context)
    {
        // 1. Check for API key (most reliable for authenticated clients)
        var apiKey = context.Request.Headers["X-API-Key"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            // Hash the API key to avoid storing sensitive data
            return $"apikey:{ComputeHash(apiKey)}";
        }

        // 2. Check for Authorization header (JWT or Bearer token)
        var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(authHeader))
        {
            return $"auth:{ComputeHash(authHeader)}";
        }

        // 3. Check for X-Forwarded-For (proxy/load balancer scenarios)
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwardedFor))
        {
            // Take the first IP (original client)
            var clientIp = forwardedFor.Split(',')[0].Trim();
            if (!string.IsNullOrWhiteSpace(clientIp))
            {
                return $"ip:{clientIp}";
            }
        }

        // 4. Fall back to remote IP address
        var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return $"ip:{remoteIp}";
    }

    /// <summary>
    /// Computes a simple hash for client identifiers to avoid storing sensitive data
    /// </summary>
    private static string ComputeHash(string input)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(input);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToBase64String(hash)[..16]; // First 16 chars for brevity
    }

    /// <summary>
    /// Creates a partitioned rate limiter that isolates rate limits per client
    /// </summary>
    private PartitionedRateLimiter<string> CreatePartitionedRateLimiter()
    {
        var options = _options.Value;
        var limit = options.RateLimitRequestsPerMinute;
        var window = TimeSpan.FromMinutes(1);

        return PartitionedRateLimiter.Create<string, string>(clientId =>
        {
            return RateLimitPartition.GetFixedWindowLimiter(clientId, _ =>
                new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limit,
                    QueueLimit = 0,
                    Window = window,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    AutoReplenishment = true
                });
        });
    }

    /// <summary>
    /// Disposes the rate limiter and cleanup timer
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _cleanupTimer.Dispose();
        _rateLimiter.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Adds rate limit headers to the response for client visibility
    /// </summary>
    private void AddRateLimitHeaders(HttpResponse response, RateLimitLease lease)
    {
        var options = _options.Value;
        
        // Standard rate limit headers (draft RFC)
        response.Headers["X-RateLimit-Limit"] = options.RateLimitRequestsPerMinute.ToString();
        
        if (lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            response.Headers["X-RateLimit-Reset"] = ((int)retryAfter.TotalSeconds).ToString();
        }
    }

    /// <summary>
    /// Calculates the retry-after value based on the lease metadata
    /// </summary>
    private int CalculateRetryAfter(RateLimitLease lease)
    {
        if (lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            return Math.Max(1, (int)retryAfter.TotalSeconds);
        }
        return 60; // Default to 60 seconds
    }

    /// <summary>
    /// Cleans up stale client entries to prevent memory leaks
    /// </summary>
    private void CleanupStaleClients(object? state)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-10); // Remove clients not seen in 10 minutes
        var staleClients = _clientLastSeen
            .Where(kvp => kvp.Value < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var client in staleClients)
        {
            _clientLastSeen.TryRemove(client, out _);
        }

        if (staleClients.Count > 0)
        {
            _logger.LogDebug("Cleaned up {Count} stale rate limit client entries", staleClients.Count);
        }
    }

    /// <summary>
    /// Sanitizes client ID for logging to avoid log injection
    /// </summary>
    private static string SanitizeClientId(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
            return "unknown";
        
        // Remove newlines and limit length
        return clientId
            .Replace("\r", "")
            .Replace("\n", "")
            .Substring(0, Math.Min(clientId.Length, 50));
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

        // Remove any URLs or file paths (using cached regex for performance)
        sanitized = ValidationRegex.HttpUrlRegex().Replace(sanitized, "[REDACTED_URL]");
        sanitized = ValidationRegex.FileUrlRegex().Replace(sanitized, "[REDACTED_FILE]");
        sanitized = ValidationRegex.WindowsPathRegex().Replace(sanitized, "[REDACTED_PATH]");

        return sanitized;
    }
}

/// <summary>
/// Extension method to add rate limiting middleware
/// </summary>
public static class RateLimitMiddlewareExtensions
{
    public static IApplicationBuilder UseRateLimiting(
        this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<RateLimitMiddleware>();
    }
}