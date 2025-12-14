using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using mostlylucid.mockllmapi.Models;
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
    /// Reads the request body as a string, supporting JSON, form data, and multipart uploads
    /// </summary>
    public async Task<string> ReadBodyAsync(HttpRequest request)
    {
        if (request.ContentLength is null or <= 0)
            return string.Empty;

        var contentType = request.ContentType ?? "";

        // Handle multipart/form-data (file uploads)
        if (contentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            return await ReadMultipartFormAsync(request);
        }

        // Handle application/x-www-form-urlencoded
        if (contentType.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
        {
            return await ReadFormUrlEncodedAsync(request);
        }

        // Handle JSON and other text-based content
        using var reader = new StreamReader(request.Body);
        return await reader.ReadToEndAsync();
    }

    /// <summary>
    /// Reads form URL-encoded body and converts to JSON
    /// </summary>
    private async Task<string> ReadFormUrlEncodedAsync(HttpRequest request)
    {
        var form = await request.ReadFormAsync();
        var jsonParts = new List<string>();

        foreach (var key in form.Keys)
        {
            var values = form[key].ToArray();
            var escapedKey = EscapeJsonString(key);

            if (values.Length == 1)
            {
                var escapedValue = EscapeJsonString(values[0] ?? "");
                jsonParts.Add($"{escapedKey}:{escapedValue}");
            }
            else
            {
                var arrayValues = values.Select(v => EscapeJsonString(v ?? ""));
                jsonParts.Add($"{escapedKey}:[{string.Join(",", arrayValues)}]");
            }
        }

        return "{" + string.Join(",", jsonParts) + "}";
    }

    /// <summary>
    /// Manually escapes a string for JSON to avoid reflection-based serialization
    /// </summary>
    private static string EscapeJsonString(string str)
    {
        return "\"" + str
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t")
            + "\"";
    }

/// <summary>
    /// Reads multipart form data including file uploads with streaming support
    /// For large files, streams content to temporary storage instead of loading into memory
    /// </summary>
    private async Task<string> ReadMultipartFormAsync(HttpRequest request)
    {
        if (!request.HasFormContentType)
            return "{}";

        var form = await request.ReadFormAsync();
        var jsonParts = new List<string>();

        // Read form fields
        var fieldParts = new List<string>();
        foreach (var key in form.Keys)
        {
            var values = form[key].ToArray();
            var escapedKey = EscapeJsonString(key);

            if (values.Length == 1)
            {
                var escapedValue = EscapeJsonString(values[0] ?? "");
                fieldParts.Add($"{escapedKey}:{escapedValue}");
            }
            else
            {
                var arrayValues = values.Select(v => EscapeJsonString(v ?? ""));
                fieldParts.Add($"{escapedKey}:[{string.Join(",", arrayValues)}]");
            }
        }

        if (fieldParts.Count > 0)
        {
            jsonParts.Add($"\"fields\":{{{string.Join(",", fieldParts)}}}");
        }
        else
        {
            jsonParts.Add("\"fields\":{}");
        }

        // Read file uploads with streaming support
        if (form.Files.Count > 0)
        {
            var fileParts = new List<string>();

            foreach (var file in form.Files)
            {
                // Validate file size
                const long maxFileSize = 10 * 1024 * 1024; // 10MB limit
                if (file.Length > maxFileSize)
                {
                    throw new InvalidOperationException($"File size exceeds maximum allowed size of {maxFileSize / (1024 * 1024)}MB");
                }

                // For small files, read directly into memory
                if (file.Length < 1024 * 1024) // 1MB threshold
                {
                    using var stream = file.OpenReadStream();
                    var buffer = new byte[8192];
                    long totalRead = 0;
                    int bytesRead;

                    while ((bytesRead = await stream.ReadAsync(buffer)) > 0)
                    {
                        totalRead += bytesRead;
                        // Content is discarded, we only track the size
                    }

                    // Manual JSON construction
                    var fieldName = EscapeJsonString(file.Name ?? "unknown");
                    var fileName = EscapeJsonString(file.FileName ?? "unknown");
                    var contentType = EscapeJsonString(file.ContentType ?? "application/octet-stream");

                    var fileJson = $"{{{fieldName}:{fileName},\"contentType\":{contentType},\"size\":{file.Length},\"processed\":true,\"actualBytesRead\":{totalRead}}}";
                    fileParts.Add(fileJson);
                }
                else
                {
                    // For large files, stream to temporary storage
                    // Note: Using Guid instead of Path.GetTempFileName() to avoid:
                    // 1. Windows temp file limit (65535 files) failures under high load
                    // 2. Blocking calls to create unique files
                    var tempFilePath = Path.Combine(Path.GetTempPath(), $"llmock_{Guid.NewGuid():N}.tmp");
                    try
                    {
                        using var inputStream = file.OpenReadStream();
                        using var outputStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, FileOptions.Asynchronous | FileOptions.SequentialScan);

                        var buffer = new byte[8192];
                        long totalRead = 0;
                        int bytesRead;

                        while ((bytesRead = await inputStream.ReadAsync(buffer)) > 0)
                        {
                            await outputStream.WriteAsync(buffer, 0, bytesRead);
                            totalRead += bytesRead;
                        }

                        // Manual JSON construction
                        var fieldName = EscapeJsonString(file.Name ?? "unknown");
                        var fileName = EscapeJsonString(file.FileName ?? "unknown");
                        var contentType = EscapeJsonString(file.ContentType ?? "application/octet-stream");

                        var fileJson = $"{{{fieldName}:{fileName},\"contentType\":{contentType},\"size\":{file.Length},\"processed\":true,\"actualBytesRead\":{totalRead},\"streamed\":true,\"tempPath\":\"{EscapeJsonString(tempFilePath)}\"}}";
                        fileParts.Add(fileJson);
                    }
                    finally
                    {
                        // Clean up temporary file
                        if (File.Exists(tempFilePath))
                        {
                            try
                            {
                                File.Delete(tempFilePath);
                            }
                            catch
                            {
                                // Ignore cleanup errors
                            }
                        }
                    }
                }
            }

            jsonParts.Add($"\"files\":[{string.Join(",", fileParts)}]");
        }

        return "{" + string.Join(",", jsonParts) + "}";
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
