using System.Net.Http;
using System.IO;
using System.Threading;

namespace LLMockApiClient.Services;

public class SseStreamService
{
    private readonly HttpClient _httpClient;
    private CancellationTokenSource? _cancellationTokenSource;

    public event EventHandler<string>? MessageReceived;
    public event EventHandler<string>? ErrorOccurred;
    public event EventHandler? StreamEnded;

    public bool IsStreaming => _cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested;

    public SseStreamService(string baseUrl)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromMinutes(10) // Long timeout for streaming
        };
    }

    public async Task StartStreamAsync(string endpoint)
    {
        if (IsStreaming)
        {
            throw new InvalidOperationException("Stream is already running");
        }

        _cancellationTokenSource = new CancellationTokenSource();
        var token = _cancellationTokenSource.Token;

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Add("Accept", "text/event-stream");

            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(token);
            using var reader = new StreamReader(stream);

            while (!token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync();

                if (line == null)
                {
                    // End of stream
                    break;
                }

                // SSE format: "data: {...}"
                if (line.StartsWith("data: "))
                {
                    var data = line.Substring(6); // Remove "data: " prefix
                    MessageReceived?.Invoke(this, data);
                }
                else if (line.StartsWith(": "))
                {
                    // Comment line (heartbeat), ignore
                    continue;
                }
                else if (string.IsNullOrWhiteSpace(line))
                {
                    // Empty line marks end of event, continue
                    continue;
                }
            }

            StreamEnded?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation
            StreamEnded?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
        }
        finally
        {
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    public void StopStream()
    {
        _cancellationTokenSource?.Cancel();
    }

    public void Dispose()
    {
        StopStream();
        _httpClient?.Dispose();
    }
}
