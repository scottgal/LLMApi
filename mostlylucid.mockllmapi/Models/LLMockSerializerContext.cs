using System.Text.Json;
using System.Text.Json.Serialization;
using mostlylucid.mockllmapi.Services.Tools;

namespace mostlylucid.mockllmapi.Models;

/// <summary>
///     Comprehensive source-generated JSON serialization context for LLMock API.
///     AOT and trimming-friendly for .NET 8+.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false,
    GenerationMode = JsonSourceGenerationMode.Default)]
// Error response types
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(GraphQLErrorResponse))]
[JsonSerializable(typeof(GraphQLError))]
[JsonSerializable(typeof(GraphQLErrorExtensions))]
[JsonSerializable(typeof(InternalGraphQLErrorResponse))]
// SSE/Streaming event types
[JsonSerializable(typeof(SseChunkEvent))]
[JsonSerializable(typeof(SseFinalEvent))]
[JsonSerializable(typeof(SseErrorEvent))]
[JsonSerializable(typeof(SseInfoEvent))]
[JsonSerializable(typeof(SseEndEvent))]
[JsonSerializable(typeof(SseDataEvent))]
[JsonSerializable(typeof(SseTokenEvent))]
[JsonSerializable(typeof(SseFinalWithSchemaEvent))]
[JsonSerializable(typeof(SseCompleteObjectEvent))]
[JsonSerializable(typeof(SseArrayItemEvent))]
[JsonSerializable(typeof(SseContinuousInfoEvent))]
[JsonSerializable(typeof(SseContinuousEndEvent))]
[JsonSerializable(typeof(SseContinuousDataEvent))]
[JsonSerializable(typeof(SseContinuousArrayItemEvent))]
[JsonSerializable(typeof(SseContinuousTokenEvent))]
[JsonSerializable(typeof(SseContinuousFinalEvent))]
// GraphQL types
[JsonSerializable(typeof(GraphQLRequest))]
[JsonSerializable(typeof(GraphQLDataResponse))]
[JsonSerializable(typeof(GraphQLDataWrapper))]
// Batch completion types
[JsonSerializable(typeof(BatchCompletionResponse))]
[JsonSerializable(typeof(BatchCompletionItem))]
[JsonSerializable(typeof(BatchCompletionTiming))]
[JsonSerializable(typeof(BatchCompletionMeta))]
[JsonSerializable(typeof(ToolResultWrapper))]
[JsonSerializable(typeof(ToolResultItem))]
[JsonSerializable(typeof(WrappedResponseWithTools))]
// Generic response types
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(object[]))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(JsonDocument))]
// Simple types for inline anonymous objects
[JsonSerializable(typeof(ToolsWrapper))]
// Continuous streaming error
[JsonSerializable(typeof(SseContinuousErrorEvent))]
// Request types for endpoints
[JsonSerializable(typeof(AddToContextRequest))]
[JsonSerializable(typeof(LoadSpecRequest))]
[JsonSerializable(typeof(TestEndpointRequest))]
[JsonSerializable(typeof(EvolveToolsRequest))]
[JsonSerializable(typeof(GenerateTestsRequest))]
[JsonSerializable(typeof(ReviewCodeRequest))]
[JsonSerializable(typeof(HubContextConfig))]
// Tool fitness types (from Services/Tools)
[JsonSerializable(typeof(ToolFitnessReport))]
[JsonSerializable(typeof(ToolFitnessReport[]))]
[JsonSerializable(typeof(List<ToolFitnessReport>))]
[JsonSerializable(typeof(ToolTestResult))]
[JsonSerializable(typeof(ToolFitnessSnapshot))]
[JsonSerializable(typeof(ToolRagDocument))]
[JsonSerializable(typeof(FitnessTrend))]
[JsonSerializable(typeof(TestExpectation))]
[JsonSerializable(typeof(ValidationResult))]
[JsonSerializable(typeof(ToolFitnessExport))]
[JsonSerializable(typeof(ToolFitnessHistoryExport))]
// List/Array types
[JsonSerializable(typeof(List<JsonElement>))]
[JsonSerializable(typeof(List<object>))]
[JsonSerializable(typeof(List<ToolTestResult>))]
[JsonSerializable(typeof(List<ToolFitnessSnapshot>))]
// Protobuf field dictionary
[JsonSerializable(typeof(Dictionary<string, ProtobufFieldInfo>))]
// Response wrappers for RegularRequestHandler
[JsonSerializable(typeof(ResponseMetadata))]
[JsonSerializable(typeof(ContentWrapper))]
public partial class LLMockSerializerContext : JsonSerializerContext
{
    /// <summary>
    ///     Default instance with standard options
    /// </summary>
    public static LLMockSerializerContext DefaultInstance { get; } = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    });

    /// <summary>
    ///     Instance with indented output for debugging
    /// </summary>
    public static LLMockSerializerContext IndentedInstance { get; } = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    });

    /// <summary>
    ///     Instance with case-insensitive property matching (for deserialization)
    /// </summary>
    public static LLMockSerializerContext CaseInsensitiveInstance { get; } = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    });

    #region Static Helper Methods

    /// <summary>
    ///     Serialize error response for standard JSON endpoints
    /// </summary>
    public static string SerializeError(string error)
    {
        return JsonSerializer.Serialize(new ErrorResponse(error), Default.ErrorResponse);
    }

    /// <summary>
    ///     Serialize GraphQL error response (data: null, errors: [...])
    /// </summary>
    public static string SerializeGraphQLError(string message, string code = "INTERNAL_SERVER_ERROR")
    {
        return JsonSerializer.Serialize(new InternalGraphQLErrorResponse(
            null,
            [new GraphQLError(message, new GraphQLErrorExtensions(code))]
        ), Default.InternalGraphQLErrorResponse);
    }

    /// <summary>
    ///     Serialize SSE chunk event
    /// </summary>
    public static string SerializeSseChunk(string chunk, string accumulated, bool done = false)
    {
        return JsonSerializer.Serialize(new SseTokenEvent(chunk, accumulated, done), Default.SseTokenEvent);
    }

    /// <summary>
    ///     Serialize SSE final event
    /// </summary>
    public static string SerializeSseFinal(string content, bool done = true)
    {
        return JsonSerializer.Serialize(new SseFinalEvent(content, done), Default.SseFinalEvent);
    }

    /// <summary>
    ///     Serialize SSE final event with schema
    /// </summary>
    public static string SerializeSseFinalWithSchema(string content, string schema, bool done = true)
    {
        return JsonSerializer.Serialize(new SseFinalWithSchemaEvent(content, done, schema),
            Default.SseFinalWithSchemaEvent);
    }

    /// <summary>
    ///     Serialize SSE error event
    /// </summary>
    public static string SerializeSseError(string error, string type = "error")
    {
        return JsonSerializer.Serialize(new SseErrorEvent(error, type), Default.SseErrorEvent);
    }

    /// <summary>
    ///     Serialize SSE info event
    /// </summary>
    public static string SerializeSseInfo(string info, string type = "info")
    {
        return JsonSerializer.Serialize(new SseInfoEvent(info, type), Default.SseInfoEvent);
    }

    /// <summary>
    ///     Serialize SSE end event
    /// </summary>
    public static string SerializeSseEnd(string message, int eventCount, bool done = true)
    {
        return JsonSerializer.Serialize(new SseContinuousEndEvent("end", message, eventCount, done),
            Default.SseContinuousEndEvent);
    }

    /// <summary>
    ///     Serialize SSE complete object event
    /// </summary>
    public static string SerializeSseCompleteObject(JsonElement data, int index, int total, bool done)
    {
        return JsonSerializer.Serialize(new SseCompleteObjectEvent(data, index, total, done),
            Default.SseCompleteObjectEvent);
    }

    /// <summary>
    ///     Serialize SSE array item event
    /// </summary>
    public static string SerializeSseArrayItem(JsonElement item, int index, int total, string? arrayName, bool hasMore,
        bool done)
    {
        return JsonSerializer.Serialize(new SseArrayItemEvent(item, index, total, arrayName, hasMore, done),
            Default.SseArrayItemEvent);
    }

    /// <summary>
    ///     Serialize continuous streaming info event
    /// </summary>
    public static string SerializeContinuousInfo(string mode, int intervalMs, int maxDurationSeconds)
    {
        return JsonSerializer.Serialize(
            new SseContinuousInfoEvent("info", "Continuous streaming started", mode, intervalMs, maxDurationSeconds),
            Default.SseContinuousInfoEvent);
    }

    /// <summary>
    ///     Serialize continuous streaming data event
    /// </summary>
    public static string SerializeContinuousData(JsonElement data, int index, DateTime timestamp, bool done = false)
    {
        return JsonSerializer.Serialize(new SseContinuousDataEvent(data, index, timestamp, done),
            Default.SseContinuousDataEvent);
    }

    /// <summary>
    ///     Serialize continuous streaming array item event
    /// </summary>
    public static string SerializeContinuousArrayItem(JsonElement item, int index, int total, string? arrayName,
        int batchNumber, DateTime timestamp, bool hasMore, bool done = false)
    {
        return JsonSerializer.Serialize(
            new SseContinuousArrayItemEvent(item, index, total, arrayName, batchNumber, timestamp, hasMore, done),
            Default.SseContinuousArrayItemEvent);
    }

    /// <summary>
    ///     Serialize continuous streaming token event
    /// </summary>
    public static string SerializeContinuousToken(string chunk, string accumulated, int batchNumber, bool done = false)
    {
        return JsonSerializer.Serialize(new SseContinuousTokenEvent(chunk, accumulated, batchNumber, done),
            Default.SseContinuousTokenEvent);
    }

    /// <summary>
    ///     Serialize continuous streaming final event
    /// </summary>
    public static string SerializeContinuousFinal(string content, int batchNumber, DateTime timestamp,
        bool done = false)
    {
        return JsonSerializer.Serialize(new SseContinuousFinalEvent(content, batchNumber, timestamp, done),
            Default.SseContinuousFinalEvent);
    }

    /// <summary>
    ///     Serialize continuous streaming error event
    /// </summary>
    public static string SerializeContinuousError(string message, int eventCount)
    {
        return JsonSerializer.Serialize(new SseContinuousErrorEvent("error", message, eventCount),
            Default.SseContinuousErrorEvent);
    }

    /// <summary>
    ///     Serialize GraphQL data wrapper
    /// </summary>
    public static string SerializeGraphQLData(JsonElement data)
    {
        return JsonSerializer.Serialize(new GraphQLDataWrapper(data), Default.GraphQLDataWrapper);
    }

    /// <summary>
    ///     Serialize batch completion response (indented for readability)
    /// </summary>
    public static string SerializeBatchCompletion(BatchCompletionResponse response)
    {
        return JsonSerializer.Serialize(response, IndentedInstance.BatchCompletionResponse);
    }

    /// <summary>
    ///     Serialize wrapped response with tool results (indented for readability)
    /// </summary>
    public static string SerializeWrappedResponseWithTools(WrappedResponseWithTools response)
    {
        return JsonSerializer.Serialize(response, IndentedInstance.WrappedResponseWithTools);
    }

    #endregion
}

#region Error Response Types

/// <summary>
///     Standard error response
/// </summary>
public record ErrorResponse(string Error);

/// <summary>
///     Error response with message
/// </summary>
public record ErrorMessageResponse(string Error, string Message);

/// <summary>
///     GraphQL error response format
/// </summary>
public record GraphQLErrorResponse(object? Data, GraphQLError[] Errors);

/// <summary>
///     Internal GraphQL error response (for serialization)
/// </summary>
public record InternalGraphQLErrorResponse(object? Data, GraphQLError[] Errors);

/// <summary>
///     GraphQL error
/// </summary>
public record GraphQLError(string Message, GraphQLErrorExtensions? Extensions = null);

/// <summary>
///     GraphQL error extensions
/// </summary>
public record GraphQLErrorExtensions(string? Code = null, string? Details = null);

#endregion

#region SSE Event Types

/// <summary>
///     SSE chunk event for streaming
/// </summary>
public record SseChunkEvent(string Chunk, bool Done = false);

/// <summary>
///     SSE final event with complete content
/// </summary>
public record SseFinalEvent(string Content, bool Done = true);

/// <summary>
///     SSE final event with complete content and schema
/// </summary>
public record SseFinalWithSchemaEvent(string Content, bool Done, string Schema);

/// <summary>
///     SSE error event
/// </summary>
public record SseErrorEvent(string Error, string Type = "error");

/// <summary>
///     SSE info event
/// </summary>
public record SseInfoEvent(string Info, string Type = "info");

/// <summary>
///     SSE end event
/// </summary>
public record SseEndEvent(string Message, string Type = "end");

/// <summary>
///     SSE data event with typed data
/// </summary>
public record SseDataEvent(object Data, string Type = "data", int? Index = null, int? Total = null);

/// <summary>
///     SSE token streaming event (chunk + accumulated)
/// </summary>
public record SseTokenEvent(string Chunk, string Accumulated, bool Done);

/// <summary>
///     SSE complete object event (for CompleteObjects mode)
/// </summary>
public record SseCompleteObjectEvent(JsonElement Data, int Index, int Total, bool Done);

/// <summary>
///     SSE array item event (for ArrayItems mode)
/// </summary>
public record SseArrayItemEvent(JsonElement Item, int Index, int Total, string? ArrayName, bool HasMore, bool Done);

/// <summary>
///     SSE continuous streaming info event
/// </summary>
public record SseContinuousInfoEvent(string Type, string Message, string Mode, int IntervalMs, int MaxDurationSeconds);

/// <summary>
///     SSE continuous streaming end event
/// </summary>
public record SseContinuousEndEvent(string Type, string Message, int EventCount, bool Done);

/// <summary>
///     SSE continuous streaming data event
/// </summary>
public record SseContinuousDataEvent(JsonElement Data, int Index, DateTime Timestamp, bool Done);

/// <summary>
///     SSE continuous streaming array item event
/// </summary>
public record SseContinuousArrayItemEvent(
    JsonElement Item,
    int Index,
    int Total,
    string? ArrayName,
    int BatchNumber,
    DateTime Timestamp,
    bool HasMore,
    bool Done);

/// <summary>
///     SSE continuous streaming token event
/// </summary>
public record SseContinuousTokenEvent(string Chunk, string Accumulated, int BatchNumber, bool Done);

/// <summary>
///     SSE continuous streaming final event
/// </summary>
public record SseContinuousFinalEvent(string Content, int BatchNumber, DateTime Timestamp, bool Done);

/// <summary>
///     SSE continuous streaming error event
/// </summary>
public record SseContinuousErrorEvent(string Type, string Message, int EventCount);

#endregion

#region GraphQL Types

/// <summary>
///     GraphQL request
/// </summary>
public record GraphQLRequest(
    string Query,
    string? OperationName = null,
    Dictionary<string, object>? Variables = null);

/// <summary>
///     GraphQL data response wrapper
/// </summary>
public record GraphQLDataResponse(object Data);

/// <summary>
///     GraphQL data wrapper (for JsonElement data)
/// </summary>
public record GraphQLDataWrapper(JsonElement Data);

#endregion

#region Batch Completion Types

/// <summary>
///     Full batch completion response
/// </summary>
public record BatchCompletionResponse(
    IEnumerable<BatchCompletionItem> Completions,
    BatchCompletionMeta Meta);

/// <summary>
///     Single batch completion item
/// </summary>
public record BatchCompletionItem(
    int Index,
    JsonElement Content,
    BatchCompletionTiming Timing);

/// <summary>
///     Timing info for a batch completion
/// </summary>
public record BatchCompletionTiming(
    long RequestTimeMs,
    long DelayAppliedMs);

/// <summary>
///     Metadata for batch completion
/// </summary>
public record BatchCompletionMeta(
    string Strategy,
    long TotalRequestTimeMs,
    long TotalDelayMs,
    long TotalElapsedMs,
    double AverageRequestTimeMs);

#endregion

#region Tool Types

/// <summary>
///     Wrapper for tools array
/// </summary>
public record ToolsWrapper(object[] Tools);

/// <summary>
///     Tool result wrapper for response
/// </summary>
public record ToolResultWrapper(
    string ToolName,
    bool Success,
    JsonElement? Data,
    string? Error,
    long ExecutionTimeMs,
    Dictionary<string, string>? Metadata);

/// <summary>
///     Simplified tool result item for batch responses
/// </summary>
public record ToolResultItem(
    string ToolName,
    bool Success,
    object? Data,
    string? Error,
    long ExecutionTimeMs,
    Dictionary<string, string>? Metadata);

/// <summary>
///     Wrapped response including tool results
/// </summary>
public record WrappedResponseWithTools(
    JsonElement Data,
    IEnumerable<ToolResultItem> ToolResults);

#endregion

#region Request Types for Endpoints

/// <summary>
///     Request model for adding to API context
/// </summary>
public class AddToContextRequest
{
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string? RequestBody { get; set; }
    public string ResponseBody { get; set; } = string.Empty;
}

/// <summary>
///     Request model for loading OpenAPI spec
/// </summary>
public class LoadSpecRequest
{
    public string Name { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string? BasePath { get; set; }
    public string? ContextName { get; set; }
}

/// <summary>
///     Request model for testing an endpoint
/// </summary>
public class TestEndpointRequest
{
    public string SpecName { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Method { get; set; } = "GET";
}

/// <summary>
///     Request model for tool evolution
/// </summary>
public class EvolveToolsRequest
{
    public double Threshold { get; set; } = 60.0;
}

/// <summary>
///     Request to generate unit tests
/// </summary>
public class GenerateTestsRequest
{
    public string ToolName { get; set; } = string.Empty;
    public string SourceCode { get; set; } = string.Empty;
    public string? ExistingTests { get; set; }
}

/// <summary>
///     Request to review code
/// </summary>
public class ReviewCodeRequest
{
    public string? Name { get; set; }
    public string SourceCode { get; set; } = string.Empty;
}

#endregion

#region Tool Fitness Export Types

/// <summary>
///     Tool fitness export for RAG documents
/// </summary>
public class ToolFitnessExport
{
    public DateTimeOffset ExportedAt { get; set; }
    public string TestRunId { get; set; } = string.Empty;
    public List<ToolRagDocument> Documents { get; set; } = [];
}

/// <summary>
///     Tool fitness history export
/// </summary>
public class ToolFitnessHistoryExport
{
    public DateTimeOffset ExportedAt { get; set; }
    public int TotalTools { get; set; }
    public int TotalSnapshots { get; set; }
    public Dictionary<string, List<ToolFitnessSnapshot>> History { get; set; } = new();
}

#endregion

#region Protobuf Types

/// <summary>
///     Information about a protobuf field
/// </summary>
public class ProtobufFieldInfo
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int FieldNumber { get; set; }
    public object? Value { get; set; }
}

#endregion

#region Response Wrapper Types

/// <summary>
///     Response metadata for wrapped responses
/// </summary>
public class ResponseMetadata
{
    public string? Id { get; set; }
    public string? Model { get; set; }
    public DateTime Created { get; set; }
    public List<ContentWrapper> Choices { get; set; } = [];
}

/// <summary>
///     Content wrapper for response choices
/// </summary>
public class ContentWrapper
{
    public int Index { get; set; }
    public object? Content { get; set; }
    public string? FinishReason { get; set; }
}

#endregion
