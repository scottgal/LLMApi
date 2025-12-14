using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace mostlylucid.mockllmapi.Services;

/// <summary>
///     Extracts context name from requests (query param, header, or body)
/// </summary>
public class ContextExtractor
{
    /// <summary>
    ///     Extracts context name from the request using precedence:
    ///     1. Query parameter (context or api-context)
    ///     2. Header (X-Api-Context)
    ///     3. Body property (context or apiContext)
    /// </summary>
    public string? ExtractContextName(HttpRequest request, string? body)
    {
        // 1) Query parameter (support both "context" and "api-context")
        if (request.Query.TryGetValue("context", out var contextQuery) && contextQuery.Count > 0)
        {
            var val = contextQuery[0];
            if (!string.IsNullOrWhiteSpace(val))
                return val;
        }

        if (request.Query.TryGetValue("api-context", out var apiContextQuery) && apiContextQuery.Count > 0)
        {
            var val = apiContextQuery[0];
            if (!string.IsNullOrWhiteSpace(val))
                return val;
        }

        // 2) Header
        if (request.Headers.TryGetValue("X-Api-Context", out var contextHeader) && contextHeader.Count > 0)
        {
            var val = contextHeader[0];
            if (!string.IsNullOrWhiteSpace(val))
                return val;
        }

        // 3) Body property (support both "context" and "apiContext")
        if (!string.IsNullOrWhiteSpace(body) &&
            request.ContentType != null &&
            request.ContentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    // Try "context" first
                    if (doc.RootElement.TryGetProperty("context", out var contextNode))
                    {
                        var val = contextNode.GetString();
                        if (!string.IsNullOrWhiteSpace(val))
                            return val;
                    }

                    // Try "apiContext" second
                    if (doc.RootElement.TryGetProperty("apiContext", out var apiContextNode))
                    {
                        var val = apiContextNode.GetString();
                        if (!string.IsNullOrWhiteSpace(val))
                            return val;
                    }
                }
            }
            catch
            {
                // Ignore JSON parse errors
            }

        return null;
    }
}