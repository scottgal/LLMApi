using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace mostlylucid.mockllmapi.Services;

/// <summary>
///     Extracts journey name from requests (query param, header, or body).
///     Similar to ContextExtractor but for journey specification.
/// </summary>
public class JourneyExtractor
{
    /// <summary>
    ///     Extracts journey name from the request using precedence:
    ///     1. Query parameter (journey)
    ///     2. Header (X-Journey)
    ///     3. Body property (journey)
    /// </summary>
    public string? ExtractJourneyName(HttpRequest request, string? body)
    {
        // 1) Query parameter
        if (request.Query.TryGetValue("journey", out var journeyQuery) && journeyQuery.Count > 0)
        {
            var val = journeyQuery[0];
            if (!string.IsNullOrWhiteSpace(val))
                return val;
        }

        // 2) Header
        if (request.Headers.TryGetValue("X-Journey", out var journeyHeader) && journeyHeader.Count > 0)
        {
            var val = journeyHeader[0];
            if (!string.IsNullOrWhiteSpace(val))
                return val;
        }

        // 3) Body property
        if (!string.IsNullOrWhiteSpace(body) &&
            request.ContentType != null &&
            request.ContentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    if (doc.RootElement.TryGetProperty("journey", out var journeyNode))
                    {
                        var val = journeyNode.GetString();
                        if (!string.IsNullOrWhiteSpace(val))
                            return val;
                    }
            }
            catch
            {
                // Ignore JSON parse errors
            }

        return null;
    }

    /// <summary>
    ///     Extracts journey modality filter from the request (for random journey selection).
    ///     Precedence: query param (journeyModality) > header (X-Journey-Modality)
    /// </summary>
    public string? ExtractJourneyModality(HttpRequest request)
    {
        // 1) Query parameter
        if (request.Query.TryGetValue("journeyModality", out var modalityQuery) && modalityQuery.Count > 0)
        {
            var val = modalityQuery[0];
            if (!string.IsNullOrWhiteSpace(val))
                return val;
        }

        // 2) Header
        if (request.Headers.TryGetValue("X-Journey-Modality", out var modalityHeader) && modalityHeader.Count > 0)
        {
            var val = modalityHeader[0];
            if (!string.IsNullOrWhiteSpace(val))
                return val;
        }

        return null;
    }

    /// <summary>
    ///     Extracts whether to start a random journey if no journey is active.
    ///     Query param: journeyRandom=true or header: X-Journey-Random: true
    /// </summary>
    public bool ExtractJourneyRandom(HttpRequest request)
    {
        // 1) Query parameter
        if (request.Query.TryGetValue("journeyRandom", out var randomQuery) && randomQuery.Count > 0)
        {
            var val = randomQuery[0];
            return string.Equals(val, "true", StringComparison.OrdinalIgnoreCase) || val == "1";
        }

        // 2) Header
        if (request.Headers.TryGetValue("X-Journey-Random", out var randomHeader) && randomHeader.Count > 0)
        {
            var val = randomHeader[0];
            return string.Equals(val, "true", StringComparison.OrdinalIgnoreCase) || val == "1";
        }

        return false;
    }

    /// <summary>
    ///     Extracts journey ID from the request. This is a unique identifier for tracking
    ///     a specific journey instance across requests. Multiple journeys can run concurrently
    ///     by using different IDs.
    ///     Precedence: query param (journeyId) > header (X-Journey-Id) > body property (journeyId)
    /// </summary>
    public string? ExtractJourneyId(HttpRequest request, string? body)
    {
        // 1) Query parameter
        if (request.Query.TryGetValue("journeyId", out var idQuery) && idQuery.Count > 0)
        {
            var val = idQuery[0];
            if (!string.IsNullOrWhiteSpace(val))
                return val;
        }

        // 2) Header
        if (request.Headers.TryGetValue("X-Journey-Id", out var idHeader) && idHeader.Count > 0)
        {
            var val = idHeader[0];
            if (!string.IsNullOrWhiteSpace(val))
                return val;
        }

        // 3) Body property
        if (!string.IsNullOrWhiteSpace(body) &&
            request.ContentType != null &&
            request.ContentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    if (doc.RootElement.TryGetProperty("journeyId", out var idNode))
                    {
                        var val = idNode.GetString();
                        if (!string.IsNullOrWhiteSpace(val))
                            return val;
                    }
            }
            catch
            {
                // Ignore JSON parse errors
            }

        return null;
    }

    /// <summary>
    ///     Generates a new unique journey ID.
    ///     Format: "jrn_{timestamp}_{random}" for readability and uniqueness.
    /// </summary>
    public static string GenerateJourneyId()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var random = Guid.NewGuid().ToString("N")[..8];
        return $"jrn_{timestamp}_{random}";
    }
}