namespace LLMock.Cli.Embedded;

public class EmbeddedModelOptions
{
    /// <summary>GGUF filename stored in ~/.llmock/models/</summary>
    public string FileName { get; init; } = "qwen3.5-0.8b-q4_k_m.gguf";

    /// <summary>Direct download URL for the GGUF file.</summary>
    public string DownloadUrl { get; init; } =
        "https://huggingface.co/Qwen/Qwen3.5-0.8B-GGUF/resolve/main/qwen3.5-0.8b-q4_k_m.gguf";

    /// <summary>Expected SHA256 of the GGUF file (lowercase hex). Empty = skip verification.
    /// Set to "placeholder-update-at-release-time" during development; ModelDownloader will
    /// skip checksum validation and log a warning when this value starts with "placeholder".
    /// </summary>
    public string ExpectedSha256 { get; init; } =
        "placeholder-update-at-release-time";
}
