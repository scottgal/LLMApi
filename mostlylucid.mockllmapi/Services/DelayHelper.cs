using Microsoft.Extensions.Options;

namespace mostlylucid.mockllmapi.Services;

/// <summary>
/// Helper for applying configured delays
/// </summary>
public class DelayHelper(IOptions<LLMockApiOptions> options)
{
    private readonly LLMockApiOptions _options = options.Value;
    private readonly Random _random = new();

    /// <summary>
    /// Applies a random request delay if configured
    /// </summary>
    public async Task ApplyRequestDelayAsync(CancellationToken cancellationToken = default)
    {
        var minMs = _options.RandomRequestDelayMinMs;
        var maxMs = _options.RandomRequestDelayMaxMs;

        if (minMs > 0 || maxMs > 0)
        {
            var delayMs = maxMs > minMs
                ? _random.Next(minMs, maxMs + 1)
                : Math.Max(minMs, maxMs);

            if (delayMs > 0)
            {
                await Task.Delay(delayMs, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Applies a random streaming chunk delay if configured
    /// </summary>
    public async Task ApplyStreamingDelayAsync(CancellationToken cancellationToken = default)
    {
        var minMs = _options.StreamingChunkDelayMinMs;
        var maxMs = _options.StreamingChunkDelayMaxMs;

        if (minMs > 0 || maxMs > 0)
        {
            var delayMs = maxMs > minMs
                ? _random.Next(minMs, maxMs + 1)
                : Math.Max(minMs, maxMs);

            if (delayMs > 0)
            {
                await Task.Delay(delayMs, cancellationToken);
            }
        }
    }
}
