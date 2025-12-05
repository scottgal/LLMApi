using System.Net.Http;
using System.Text.Json;

namespace LLMockApiClient.Services;

public class ModelInfo
{
    public string Name { get; set; } = "";
    public string Provider { get; set; } = "";
    public long? Size { get; set; }
    public int? ContextLength { get; set; }
    public DateTime? ModifiedAt { get; set; }
}

public class ModelDiscoveryService
{
    private readonly HttpClient _httpClient;

    public ModelDiscoveryService()
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(5);
    }

    public async Task<List<ModelInfo>> DiscoverOllamaModelsAsync(string baseUrl)
    {
        try
        {
            // Ollama API: GET /api/tags
            var response = await _httpClient.GetAsync($"{baseUrl}/api/tags");
            if (!response.IsSuccessStatusCode)
                return new List<ModelInfo>();

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            var models = new List<ModelInfo>();
            if (doc.RootElement.TryGetProperty("models", out var modelsArray))
            {
                foreach (var model in modelsArray.EnumerateArray())
                {
                    var modelInfo = new ModelInfo
                    {
                        Provider = "Ollama",
                        Name = model.GetProperty("name").GetString() ?? ""
                    };

                    if (model.TryGetProperty("size", out var size))
                        modelInfo.Size = size.GetInt64();

                    if (model.TryGetProperty("modified_at", out var modified))
                    {
                        if (DateTime.TryParse(modified.GetString(), out var dt))
                            modelInfo.ModifiedAt = dt;
                    }

                    // Try to get model details for context length
                    try
                    {
                        var requestBody = JsonSerializer.Serialize(new { name = modelInfo.Name });
                        var content = new System.Net.Http.StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");
                        var detailsResponse = await _httpClient.PostAsync($"{baseUrl}/api/show", content);

                        if (detailsResponse.IsSuccessStatusCode)
                        {
                            var detailsJson = await detailsResponse.Content.ReadAsStringAsync();
                            var detailsDoc = JsonDocument.Parse(detailsJson);

                            if (detailsDoc.RootElement.TryGetProperty("modelfile", out var modelfile))
                            {
                                var modelfileStr = modelfile.GetString() ?? "";
                                // Look for num_ctx in modelfile
                                var numCtxMatch = System.Text.RegularExpressions.Regex.Match(
                                    modelfileStr, @"num_ctx\s+(\d+)");
                                if (numCtxMatch.Success && int.TryParse(numCtxMatch.Groups[1].Value, out var ctx))
                                {
                                    modelInfo.ContextLength = ctx;
                                }
                            }
                        }
                    }
                    catch { }

                    models.Add(modelInfo);
                }
            }

            return models;
        }
        catch
        {
            return new List<ModelInfo>();
        }
    }

    public async Task<List<ModelInfo>> DiscoverLMStudioModelsAsync(string baseUrl)
    {
        try
        {
            // LM Studio uses OpenAI-compatible API
            // GET /v1/models
            var response = await _httpClient.GetAsync($"{baseUrl}/v1/models");
            if (!response.IsSuccessStatusCode)
                return new List<ModelInfo>();

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            var models = new List<ModelInfo>();
            if (doc.RootElement.TryGetProperty("data", out var dataArray))
            {
                foreach (var model in dataArray.EnumerateArray())
                {
                    var modelInfo = new ModelInfo
                    {
                        Provider = "LM Studio",
                        Name = model.GetProperty("id").GetString() ?? ""
                    };

                    // LM Studio doesn't provide context length via API, use defaults
                    modelInfo.ContextLength = 4096; // Default assumption

                    models.Add(modelInfo);
                }
            }

            return models;
        }
        catch
        {
            return new List<ModelInfo>();
        }
    }

    public async Task<List<ModelInfo>> DiscoverModelsAsync(string baseUrl, string provider)
    {
        return provider.ToLowerInvariant() switch
        {
            "ollama" => await DiscoverOllamaModelsAsync(baseUrl),
            "lmstudio" => await DiscoverLMStudioModelsAsync(baseUrl),
            _ => new List<ModelInfo>()
        };
    }
}
