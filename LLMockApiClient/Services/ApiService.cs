using System.Net.Http;
using System.Net.Http.Json;
using System.Text;

namespace LLMockApiClient.Services;

public class ApiService
{
    private readonly HttpClient _httpClient;

    public ApiService(string baseUrl)
    {
        BaseUrl = baseUrl;
        _httpClient = new HttpClient { BaseAddress = new Uri(baseUrl) };
    }

    public string BaseUrl { get; set; }

    // Mock API
    public async Task<string> CallMockApiAsync(string method, string path, string? shape = null, string? body = null)
    {
        var url = $"/api/mock{path}";
        if (!string.IsNullOrEmpty(shape))
            url += $"?shape={Uri.EscapeDataString(shape)}";

        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (!string.IsNullOrEmpty(body))
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request);
        return await response.Content.ReadAsStringAsync();
    }

    // SignalR Contexts
    public async Task<string> GetContextsAsync()
    {
        var response = await _httpClient.GetAsync("/api/mock/contexts");
        return await response.Content.ReadAsStringAsync();
    }

    public async Task<string> CreateContextAsync(string name, string description)
    {
        var content = JsonContent.Create(new { name, description });
        var response = await _httpClient.PostAsync("/api/mock/contexts", content);
        return await response.Content.ReadAsStringAsync();
    }

    public async Task DeleteContextAsync(string name)
    {
        await _httpClient.DeleteAsync($"/api/mock/contexts/{name}");
    }

    // OpenAPI
    public async Task<string> GetOpenApiSpecsAsync()
    {
        var response = await _httpClient.GetAsync("/api/openapi/specs");
        return await response.Content.ReadAsStringAsync();
    }

    public async Task<string> LoadOpenApiSpecAsync(string name, string source, string? basePath = null)
    {
        var content = JsonContent.Create(new { name, source, basePath });
        var response = await _httpClient.PostAsync("/api/openapi/specs", content);
        return await response.Content.ReadAsStringAsync();
    }

    public async Task<string> GetSpecDetailsAsync(string specName)
    {
        var response = await _httpClient.GetAsync($"/api/openapi/specs/{specName}");
        return await response.Content.ReadAsStringAsync();
    }

    public async Task TestOpenApiEndpointAsync(string specName, string path, string method)
    {
        var content = JsonContent.Create(new { specName, path, method });
        await _httpClient.PostAsync("/api/openapi/test", content);
    }

    // gRPC
    public async Task<string> GetGrpcProtosAsync()
    {
        var response = await _httpClient.GetAsync("/api/grpc-protos");
        return await response.Content.ReadAsStringAsync();
    }

    public async Task<string> UploadProtoAsync(string name, string content)
    {
        var httpContent = new StringContent(content, Encoding.UTF8, "text/plain");
        var response = await _httpClient.PostAsync($"/api/grpc-protos?name={name}", httpContent);
        return await response.Content.ReadAsStringAsync();
    }

    public async Task<string> CallGrpcMethodAsync(string serviceName, string methodName, string requestJson)
    {
        var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync($"/api/grpc/{serviceName}/{methodName}", content);
        return await response.Content.ReadAsStringAsync();
    }

    // Generic HTTP request
    public async Task<string> SendRequestAsync(string method, string url, string? body = null,
        Dictionary<string, string>? headers = null)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);

        if (!string.IsNullOrEmpty(body))
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        if (headers != null)
            foreach (var header in headers)
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);

        var response = await _httpClient.SendAsync(request);
        return await response.Content.ReadAsStringAsync();
    }
}