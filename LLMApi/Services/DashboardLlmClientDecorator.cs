using Microsoft.Extensions.Options;
using mostlylucid.mockllmapi;
using mostlylucid.mockllmapi.Services;

namespace LLMApi.Services;

/// <summary>
/// Decorator for LlmClient that tracks requests in the dashboard metrics
/// </summary>
public class DashboardLlmClientDecorator : LlmClient
{
    private readonly DashboardMetrics _metrics;

    public DashboardLlmClientDecorator(
        IOptions<LLMockApiOptions> options,
        IHttpClientFactory httpClientFactory,
        DashboardMetrics metrics)
        : base(options, httpClientFactory)
    {
        _metrics = metrics;
    }

    public override async Task<string> GetCompletionAsync(string prompt, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await base.GetCompletionAsync(prompt, cancellationToken);
            _metrics.AddLlmRequest(prompt, result, isError: false);
            return result;
        }
        catch (Exception ex)
        {
            _metrics.AddLlmRequest(prompt, ex.Message, isError: true);
            throw;
        }
    }

    public override async Task<List<string>> GetNCompletionsAsync(string prompt, int n, CancellationToken cancellationToken = default)
    {
        try
        {
            var results = await base.GetNCompletionsAsync(prompt, n, cancellationToken);
            _metrics.AddLlmRequest($"{prompt} (n={n})", $"{results.Count} completions", isError: false);
            return results;
        }
        catch (Exception ex)
        {
            _metrics.AddLlmRequest($"{prompt} (n={n})", ex.Message, isError: true);
            throw;
        }
    }
}
