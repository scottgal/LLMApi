namespace LLMockApiClient.Models;

public class OpenApiEndpointItem
{
    public string Method { get; set; } = "";
    public string Path { get; set; } = "";
    public string? Summary { get; set; }

    public string MethodColor => Method.ToUpper() switch
    {
        "GET" => "#61AFEF",
        "POST" => "#98C379",
        "PUT" => "#E5C07B",
        "DELETE" => "#E06C75",
        "PATCH" => "#C678DD",
        _ => "#ABB2BF"
    };
}