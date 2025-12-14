namespace mostlylucid.mockllmapi.Services.Providers;

/// <summary>
///     Interface for LLM providers (Ollama, OpenAI, LMStudio, etc.)
///     Each provider handles API-specific request/response formatting
/// </summary>
public interface ILlmProvider
{
    /// <summary>
    ///     Provider name (e.g., "ollama", "openai", "lmstudio")
    /// </summary>
    string Name { get; }

    /// <summary>
    ///     Sends a non-streaming completion request
    /// </summary>
    Task<string> GetCompletionAsync(
        HttpClient client,
        string prompt,
        string modelName,
        double temperature,
        int? maxTokens,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Sends a streaming completion request
    /// </summary>
    Task<HttpResponseMessage> GetStreamingCompletionAsync(
        HttpClient client,
        string prompt,
        string modelName,
        double temperature,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Requests multiple completions in a single call (if supported by provider)
    /// </summary>
    Task<List<string>> GetNCompletionsAsync(
        HttpClient client,
        string prompt,
        string modelName,
        double temperature,
        int n,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Configures HttpClient headers/auth for this provider
    /// </summary>
    void ConfigureClient(HttpClient client, string? apiKey);
}