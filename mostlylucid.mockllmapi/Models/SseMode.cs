namespace mostlylucid.mockllmapi.Models;

/// <summary>
///     Server-Sent Events (SSE) streaming mode
///     Determines how data is streamed to clients via SSE
/// </summary>
public enum SseMode
{
    /// <summary>
    ///     Stream LLM generation token-by-token (character-by-character)
    ///     Use case: Testing AI chat interfaces, LLM response streaming
    ///     Format: data: {"chunk":"text","accumulated":"fulltext","done":false}
    /// </summary>
    LlmTokens = 0,

    /// <summary>
    ///     Stream complete JSON objects as separate SSE events
    ///     Use case: Realistic REST API streaming (Twitter/X API, stock tickers, real-time feeds)
    ///     Format: data: {"id":1,"name":"John"}\n\ndata: {"id":2,"name":"Jane"}
    /// </summary>
    CompleteObjects = 1,

    /// <summary>
    ///     Stream array items as individual SSE events
    ///     Use case: Paginated results, bulk data exports, search results
    ///     Format: data: {"item":{"id":1},"index":0,"total":100}\n\ndata: {"item":{"id":2},"index":1,"total":100}
    /// </summary>
    ArrayItems = 2
}