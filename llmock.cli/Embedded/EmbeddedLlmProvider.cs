using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using LLama;
using LLama.Common;
using LLama.Sampling;
using Microsoft.Extensions.Logging;
using mostlylucid.mockllmapi.Services.Providers;

namespace LLMock.Cli.Embedded;

/// <summary>
/// An ILlmProvider that runs inference locally using LlamaSharp (in-process GGUF).
/// </summary>
public sealed class EmbeddedLlmProvider : ILlmProvider, IAsyncDisposable
{
    public const string ProviderName = "embedded";

    private readonly ILogger<EmbeddedLlmProvider>? _logger;
    private readonly object _loadLock = new();

    private LLamaWeights? _weights;
    private ModelParams? _modelParams;
    private bool _disposed;

    public EmbeddedLlmProvider(ILogger<EmbeddedLlmProvider>? logger)
    {
        _logger = logger;
    }

    public string Name => ProviderName;

    /// <summary>
    /// Checks whether Apple Metal (GPU) acceleration is available on this platform.
    /// </summary>
    public static bool IsMetalAvailable()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
               && RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
    }

    /// <summary>
    /// Loads a GGUF model file into memory. Thread-safe; skips if already loaded.
    /// Sets GPU layers to 99 on Metal-capable hardware, 0 otherwise.
    /// </summary>
    public void LoadModel(string modelPath)
    {
        lock (_loadLock)
        {
            if (_weights is not null)
            {
                _logger?.LogDebug("Model already loaded, skipping");
                return;
            }

            var gpuLayers = IsMetalAvailable() ? 99 : 0;
            _logger?.LogInformation("Loading GGUF model from {Path} with {GpuLayers} GPU layers",
                modelPath, gpuLayers);

            _modelParams = new ModelParams(modelPath)
            {
                GpuLayerCount = gpuLayers,
                ContextSize = 2048
            };

            _weights = LLamaWeights.LoadFromFile(_modelParams);
            _logger?.LogInformation("Model loaded successfully");
        }
    }

    /// <summary>
    /// No-op for embedded provider; no HTTP client configuration needed.
    /// </summary>
    public void ConfigureClient(HttpClient client, string? apiKey)
    {
        // No-op: embedded inference does not use HTTP
    }

    public async Task<string> GetCompletionAsync(
        HttpClient client,
        string prompt,
        string modelName,
        double temperature,
        int? maxTokens,
        CancellationToken cancellationToken)
    {
        EnsureModelLoaded();

        var inferenceParams = CreateInferenceParams(temperature, maxTokens);

        var executor = new StatelessExecutor(_weights!, _modelParams!, _logger);

        var sb = new StringBuilder();
        await foreach (var token in executor.InferAsync(prompt, inferenceParams, cancellationToken))
        {
            sb.Append(token);
        }

        return sb.ToString();
    }

    public async Task<HttpResponseMessage> GetStreamingCompletionAsync(
        HttpClient client,
        string prompt,
        string modelName,
        double temperature,
        CancellationToken cancellationToken)
    {
        // For embedded provider, we run the full completion and wrap as a response.
        var result = await GetCompletionAsync(client, prompt, modelName, temperature, null, cancellationToken);

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(result, Encoding.UTF8, "text/plain")
        };

        return response;
    }

    public async Task<List<string>> GetNCompletionsAsync(
        HttpClient client,
        string prompt,
        string modelName,
        double temperature,
        int n,
        CancellationToken cancellationToken)
    {
        var results = new List<string>(n);
        for (var i = 0; i < n; i++)
        {
            var result = await GetCompletionAsync(client, prompt, modelName, temperature, null, cancellationToken);
            results.Add(result);
        }

        return results;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _weights?.Dispose();
        _weights = null;
        _modelParams = null;

        _logger?.LogDebug("EmbeddedLlmProvider disposed");
        await ValueTask.CompletedTask;
    }

    private void EnsureModelLoaded()
    {
        if (_weights is null)
            throw new InvalidOperationException("Embedded model not loaded. Call LoadModel() first.");
    }

    private static InferenceParams CreateInferenceParams(double temperature, int? maxTokens)
    {
        return new InferenceParams
        {
            MaxTokens = maxTokens ?? 1024,
            AntiPrompts = new[] { "\n\n\n", "</s>" },
            SamplingPipeline = new DefaultSamplingPipeline
            {
                Temperature = (float)temperature
            }
        };
    }
}
