using Microsoft.Extensions.Logging;

namespace mostlylucid.mockllmapi.Services.Providers;

/// <summary>
/// Factory for creating LLM provider instances
/// </summary>
public class LlmProviderFactory
{
    private readonly Dictionary<string, ILlmProvider> _providers;
    private readonly ILogger<LlmProviderFactory> _logger;

    public LlmProviderFactory(ILogger<LlmProviderFactory> logger)
    {
        _logger = logger;
        _providers = new Dictionary<string, ILlmProvider>(StringComparer.OrdinalIgnoreCase)
        {
            ["ollama"] = new OllamaProvider(),
            ["openai"] = new OpenAIProvider(),
            ["lmstudio"] = new LMStudioProvider()
        };
    }

    /// <summary>
    /// Gets a provider by name, returns Ollama as default
    /// </summary>
    public ILlmProvider GetProvider(string providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            _logger.LogDebug("No provider specified, using default (ollama)");
            return _providers["ollama"];
        }

        if (_providers.TryGetValue(providerName, out var provider))
        {
            _logger.LogDebug("Using provider: {ProviderName}", providerName);
            return provider;
        }

        _logger.LogWarning(
            "Unknown provider '{ProviderName}', falling back to ollama. Available providers: {Providers}",
            providerName, string.Join(", ", _providers.Keys));

        return _providers["ollama"];
    }

    /// <summary>
    /// Registers a custom provider
    /// </summary>
    public void RegisterProvider(string name, ILlmProvider provider)
    {
        _providers[name] = provider;
        _logger.LogInformation("Registered custom provider: {ProviderName}", name);
    }

    /// <summary>
    /// Gets all available provider names
    /// </summary>
    public IEnumerable<string> GetAvailableProviders()
    {
        return _providers.Keys;
    }
}
