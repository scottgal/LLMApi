namespace mostlylucid.mockllmapi;

internal class LoadSpecRequest
{
    public string Name { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string? BasePath { get; set; }
    public string? ContextName { get; set; }
}

internal class TestEndpointRequest
{
    public string SpecName { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Method { get; set; } = "GET";
}
