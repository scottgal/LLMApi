using Microsoft.AspNetCore.Http;
using mostlylucid.mockllmapi.RequestHandlers;

namespace mostlylucid.mockllmapi;

/// <summary>
/// Main service facade for LLMock API operations
/// </summary>
public class LLMockApiService
{
    private readonly RegularRequestHandler _regularHandler;
    private readonly StreamingRequestHandler _streamingHandler;

    public LLMockApiService(
        RegularRequestHandler regularHandler,
        StreamingRequestHandler streamingHandler)
    {
        _regularHandler = regularHandler;
        _streamingHandler = streamingHandler;
    }

    /// <summary>
    /// Reads the request body as a string
    /// </summary>
    public async Task<string> ReadBodyAsync(HttpRequest request)
    {
        if (request.ContentLength is > 0)
        {
            using var reader = new StreamReader(request.Body);
            return await reader.ReadToEndAsync();
        }
        return string.Empty;
    }

    /// <summary>
    /// Handles a regular (non-streaming) request
    /// </summary>
    public Task<string> HandleRequestAsync(
        string method,
        string fullPathWithQuery,
        string? body,
        HttpRequest request,
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        return _regularHandler.HandleRequestAsync(method, fullPathWithQuery, body, request, context, cancellationToken);
    }

    /// <summary>
    /// Handles a streaming request
    /// </summary>
    public Task HandleStreamingRequestAsync(
        string method,
        string fullPathWithQuery,
        string? body,
        HttpRequest request,
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        return _streamingHandler.HandleStreamingRequestAsync(method, fullPathWithQuery, body, request, context, cancellationToken);
    }
}
