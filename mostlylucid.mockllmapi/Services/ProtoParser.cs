using System.Text.RegularExpressions;
using mostlylucid.mockllmapi.Models;

namespace mostlylucid.mockllmapi.Services;

/// <summary>
///     Simple parser for .proto files to extract service and message definitions
/// </summary>
public class ProtoParser
{
    public ProtoDefinition Parse(string protoContent, string fileName = "uploaded.proto")
    {
        var definition = new ProtoDefinition
        {
            Name = fileName,
            RawContent = protoContent,
            UploadedAt = DateTime.UtcNow
        };

        // Extract syntax
        var syntaxMatch = Regex.Match(protoContent, @"syntax\s*=\s*""(proto[23])""");
        if (syntaxMatch.Success) definition.Syntax = syntaxMatch.Groups[1].Value;

        // Extract package
        var packageMatch = Regex.Match(protoContent, @"package\s+([a-zA-Z0-9_.]+)\s*;");
        if (packageMatch.Success) definition.Package = packageMatch.Groups[1].Value;

        // Extract messages
        definition.Messages = ParseMessages(protoContent);

        // Extract services
        definition.Services = ParseServices(protoContent);

        return definition;
    }

    private List<ProtoMessage> ParseMessages(string content)
    {
        var messages = new List<ProtoMessage>();

        // Match message blocks: message MessageName { ... }
        var messageRegex = new Regex(@"message\s+([a-zA-Z0-9_]+)\s*\{([^}]+)\}", RegexOptions.Singleline);
        var matches = messageRegex.Matches(content);

        foreach (Match match in matches)
        {
            var messageName = match.Groups[1].Value;
            var messageBody = match.Groups[2].Value;

            var message = new ProtoMessage
            {
                Name = messageName,
                Fields = ParseFields(messageBody)
            };

            messages.Add(message);
        }

        return messages;
    }

    private List<ProtoField> ParseFields(string messageBody)
    {
        var fields = new List<ProtoField>();

        // Match field definitions: [repeated] [optional] type name = number;
        var fieldRegex = new Regex(@"(repeated\s+)?(optional\s+)?([a-zA-Z0-9_.]+)\s+([a-zA-Z0-9_]+)\s*=\s*(\d+)");
        var matches = fieldRegex.Matches(messageBody);

        foreach (Match match in matches)
        {
            var field = new ProtoField
            {
                Repeated = !string.IsNullOrEmpty(match.Groups[1].Value),
                Optional = !string.IsNullOrEmpty(match.Groups[2].Value),
                Type = match.Groups[3].Value,
                Name = match.Groups[4].Value,
                Number = int.Parse(match.Groups[5].Value)
            };

            fields.Add(field);
        }

        return fields;
    }

    private List<ProtoService> ParseServices(string content)
    {
        var services = new List<ProtoService>();

        // Match service blocks: service ServiceName { ... }
        var serviceRegex = new Regex(@"service\s+([a-zA-Z0-9_]+)\s*\{([^}]+)\}", RegexOptions.Singleline);
        var matches = serviceRegex.Matches(content);

        foreach (Match match in matches)
        {
            var serviceName = match.Groups[1].Value;
            var serviceBody = match.Groups[2].Value;

            var service = new ProtoService
            {
                Name = serviceName,
                Methods = ParseMethods(serviceBody)
            };

            services.Add(service);
        }

        return services;
    }

    private List<ProtoMethod> ParseMethods(string serviceBody)
    {
        var methods = new List<ProtoMethod>();

        // Match RPC definitions: rpc MethodName (stream? InputType) returns (stream? OutputType);
        var methodRegex =
            new Regex(
                @"rpc\s+([a-zA-Z0-9_]+)\s*\(\s*(stream\s+)?([a-zA-Z0-9_.]+)\s*\)\s*returns\s*\(\s*(stream\s+)?([a-zA-Z0-9_.]+)\s*\)");
        var matches = methodRegex.Matches(serviceBody);

        foreach (Match match in matches)
        {
            var method = new ProtoMethod
            {
                Name = match.Groups[1].Value,
                ClientStreaming = !string.IsNullOrEmpty(match.Groups[2].Value),
                InputType = match.Groups[3].Value,
                ServerStreaming = !string.IsNullOrEmpty(match.Groups[4].Value),
                OutputType = match.Groups[5].Value
            };

            methods.Add(method);
        }

        return methods;
    }

    /// <summary>
    ///     Generates a JSON shape from a proto message for LLM prompt
    /// </summary>
    public string GenerateJsonShape(ProtoMessage message, List<ProtoMessage> allMessages)
    {
        var fields = new List<string>();

        foreach (var field in message.Fields)
        {
            var fieldValue = GetFieldPlaceholder(field, allMessages);

            if (field.Repeated)
                fields.Add($"\"{field.Name}\": [{fieldValue}]");
            else
                fields.Add($"\"{field.Name}\": {fieldValue}");
        }

        return "{ " + string.Join(", ", fields) + " }";
    }

    private string GetFieldPlaceholder(ProtoField field, List<ProtoMessage> allMessages)
    {
        // Primitive types
        return field.Type switch
        {
            "string" => "\"string\"",
            "int32" or "int64" or "uint32" or "uint64" or "sint32" or "sint64"
                or "fixed32" or "fixed64" or "sfixed32" or "sfixed64" => "0",
            "bool" => "false",
            "float" or "double" => "0.0",
            "bytes" => "\"base64string\"",
            _ => GenerateNestedMessageShape(field.Type, allMessages)
        };
    }

    private string GenerateNestedMessageShape(string messageType, List<ProtoMessage> allMessages)
    {
        var nestedMessage = allMessages.FirstOrDefault(m => m.Name == messageType);
        if (nestedMessage != null) return GenerateJsonShape(nestedMessage, allMessages);
        return "{}"; // Unknown type
    }
}