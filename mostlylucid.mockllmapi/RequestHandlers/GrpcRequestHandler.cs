using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using mostlylucid.mockllmapi.Models;
using mostlylucid.mockllmapi.Services;
using System.Text.Json;

namespace mostlylucid.mockllmapi.RequestHandlers;

/// <summary>
/// Handles gRPC-style requests with LLM-generated responses based on proto definitions
/// Supports both JSON over HTTP (for browser testing) and binary Protobuf (for true gRPC clients)
/// </summary>
public class GrpcRequestHandler
{
    private readonly ILogger<GrpcRequestHandler> _logger;
    private readonly ProtoDefinitionManager _protoManager;
    private readonly LlmClient _llmClient;
    private readonly PromptBuilder _promptBuilder;
    private readonly IOptions<LLMockApiOptions> _options;
    private readonly DynamicProtobufHandler _protobufHandler;

    public GrpcRequestHandler(
        ILogger<GrpcRequestHandler> logger,
        ProtoDefinitionManager protoManager,
        LlmClient llmClient,
        PromptBuilder promptBuilder,
        IOptions<LLMockApiOptions> options,
        DynamicProtobufHandler protobufHandler)
    {
        _logger = logger;
        _protoManager = protoManager;
        _llmClient = llmClient;
        _promptBuilder = promptBuilder;
        _options = options;
        _protobufHandler = protobufHandler;
    }

    /// <summary>
    /// Handles a gRPC unary call (simple request-response)
    /// </summary>
    public async Task<string> HandleUnaryCall(
        string serviceName,
        string methodName,
        string requestJson,
        CancellationToken cancellationToken)
    {
        // Find the method definition
        var (definition, service, method) = _protoManager.FindMethod(serviceName, methodName);

        if (method == null || definition == null)
        {
            _logger.LogWarning("gRPC method not found: {Service}/{Method}", serviceName, methodName);
            throw new InvalidOperationException($"Method {serviceName}/{methodName} not found. Upload proto file first.");
        }

        if (method.GetMethodType() != MethodType.Unary)
        {
            throw new InvalidOperationException($"Method {methodName} is not a unary call. Type: {method.GetMethodType()}");
        }

        // Get the output message definition
        var outputMessage = _protoManager.GetMessage(definition, method.OutputType);
        if (outputMessage == null)
        {
            throw new InvalidOperationException($"Output message type '{method.OutputType}' not found in proto definition");
        }

        // Generate JSON shape for the output
        var parser = _protoManager.GetParser();
        var shape = parser.GenerateJsonShape(outputMessage, definition.Messages);

        // Build prompt
        var prompt = $@"You are a gRPC service mock. Generate realistic data for this response.

Service: {serviceName}
Method: {methodName}
Request: {requestJson}

Generate a response matching this structure:
{shape}

Return ONLY valid JSON matching the structure above. Be creative with realistic values.";

        _logger.LogInformation("Calling LLM for gRPC method: {Service}/{Method}", serviceName, methodName);

        // Get response from LLM
        var response = await _llmClient.GetCompletionAsync(prompt, cancellationToken);

        // Extract JSON from response
        var jsonResponse = ExtractJson(response);

        return jsonResponse;
    }

    /// <summary>
    /// Handles a gRPC unary call with binary Protobuf request/response
    /// </summary>
    public async Task<byte[]> HandleUnaryCallBinary(
        string serviceName,
        string methodName,
        byte[] requestData,
        CancellationToken cancellationToken)
    {
        // Find the method definition
        var (definition, service, method) = _protoManager.FindMethod(serviceName, methodName);

        if (method == null || definition == null)
        {
            _logger.LogWarning("gRPC method not found: {Service}/{Method}", serviceName, methodName);
            throw new InvalidOperationException($"Method {serviceName}/{methodName} not found. Upload proto file first.");
        }

        if (method.GetMethodType() != MethodType.Unary)
        {
            throw new InvalidOperationException($"Method {methodName} is not a unary call. Type: {method.GetMethodType()}");
        }

        // Get the input and output message definitions
        var inputMessage = _protoManager.GetMessage(definition, method.InputType);
        var outputMessage = _protoManager.GetMessage(definition, method.OutputType);

        if (outputMessage == null)
        {
            throw new InvalidOperationException($"Output message type '{method.OutputType}' not found in proto definition");
        }

        // Convert binary request to JSON for LLM (simplified - full impl would parse Protobuf)
        var requestJson = requestData.Length > 0
            ? _protobufHandler.CreateJsonTemplate(inputMessage!, definition.Messages)
            : "{}";

        // Generate JSON shape for the output
        var parser = _protoManager.GetParser();
        var shape = parser.GenerateJsonShape(outputMessage, definition.Messages);

        // Build prompt
        var prompt = $@"You are a gRPC service mock. Generate realistic data for this response.

Service: {serviceName}
Method: {methodName}
Request: {requestJson}

Generate a response matching this structure:
{shape}

Return ONLY valid JSON matching the structure above. Be creative with realistic values.";

        _logger.LogInformation("Calling LLM for gRPC method (binary): {Service}/{Method}", serviceName, methodName);

        // Get response from LLM
        var response = await _llmClient.GetCompletionAsync(prompt, cancellationToken);

        // Extract JSON from response
        var jsonResponse = ExtractJson(response);

        // Convert JSON to binary Protobuf
        var binaryResponse = _protobufHandler.SerializeFromJson(jsonResponse, outputMessage, definition.Messages);

        return binaryResponse;
    }

    private string ExtractJson(string llmResponse)
    {
        // Try to extract JSON from markdown code blocks
        var match = System.Text.RegularExpressions.Regex.Match(llmResponse, @"```(?:json)?\s*(\{[\s\S]*?\})\s*```");
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        // Try to find JSON object
        match = System.Text.RegularExpressions.Regex.Match(llmResponse, @"\{[\s\S]*\}");
        if (match.Success)
        {
            return match.Value.Trim();
        }

        return llmResponse.Trim();
    }
}
