using System.Text.Json;
using Google.Protobuf;
using mostlylucid.mockllmapi.Models;

namespace mostlylucid.mockllmapi.Services;

/// <summary>
///     Handles dynamic Protobuf serialization/deserialization for runtime proto definitions
/// </summary>
public class DynamicProtobufHandler
{
    /// <summary>
    ///     Deserializes a Protobuf binary message to JSON
    /// </summary>
    public string DeserializeToJson(byte[] protobufData, ProtoMessage messageDefinition, List<ProtoMessage> allMessages)
    {
        // For now, return a simple JSON representation
        // In a full implementation, this would parse the binary Protobuf data
        // using the message definition to extract field values

        // This is a simplified placeholder - proper implementation would use
        // Google.Protobuf's DynamicMessage or custom binary parser
        return "{}";
    }

    /// <summary>
    ///     Serializes JSON to Protobuf binary format
    /// </summary>
    public byte[] SerializeFromJson(string json, ProtoMessage messageDefinition, List<ProtoMessage> allMessages)
    {
        try
        {
            var jsonDoc = JsonDocument.Parse(json);
            using var stream = new MemoryStream();
            using var writer = new CodedOutputStream(stream);

            // Write each field according to the proto definition
            foreach (var field in messageDefinition.Fields.OrderBy(f => f.Number))
            {
                if (!jsonDoc.RootElement.TryGetProperty(field.Name,
                        out var fieldValue)) continue; // Skip missing fields

                WriteField(writer, field, fieldValue, allMessages);
            }

            writer.Flush();
            return stream.ToArray();
        }
        catch
        {
            // If serialization fails, return empty message
            return Array.Empty<byte>();
        }
    }

    private void WriteField(CodedOutputStream writer, ProtoField field, JsonElement value,
        List<ProtoMessage> allMessages)
    {
        var fieldNumber = field.Number;
        var wireType = GetWireType(field.Type);

        if (field.Repeated)
        {
            if (value.ValueKind != JsonValueKind.Array) return;

            foreach (var item in value.EnumerateArray())
            {
                WriteTag(writer, fieldNumber, wireType);
                WriteValue(writer, field.Type, item, allMessages);
            }
        }
        else
        {
            WriteTag(writer, fieldNumber, wireType);
            WriteValue(writer, field.Type, value, allMessages);
        }
    }

    private void WriteTag(CodedOutputStream writer, int fieldNumber, WireFormat.WireType wireType)
    {
        writer.WriteTag(fieldNumber, wireType);
    }

    private void WriteValue(CodedOutputStream writer, string fieldType, JsonElement value,
        List<ProtoMessage> allMessages)
    {
        switch (fieldType)
        {
            case "string":
                writer.WriteString(value.GetString() ?? "");
                break;

            case "int32":
            case "sint32":
            case "sfixed32":
                writer.WriteInt32(value.TryGetInt32(out var i32) ? i32 : 0);
                break;

            case "int64":
            case "sint64":
            case "sfixed64":
                writer.WriteInt64(value.TryGetInt64(out var i64) ? i64 : 0);
                break;

            case "uint32":
            case "fixed32":
                writer.WriteUInt32(value.TryGetUInt32(out var u32) ? u32 : 0);
                break;

            case "uint64":
            case "fixed64":
                writer.WriteUInt64(value.TryGetUInt64(out var u64) ? u64 : 0);
                break;

            case "float":
                writer.WriteFloat(value.TryGetSingle(out var f) ? f : 0f);
                break;

            case "double":
                writer.WriteDouble(value.TryGetDouble(out var d) ? d : 0.0);
                break;

            case "bool":
                writer.WriteBool(value.ValueKind == JsonValueKind.True);
                break;

            case "bytes":
                var bytesStr = value.GetString() ?? "";
                var bytes = Convert.FromBase64String(bytesStr);
                writer.WriteBytes(ByteString.CopyFrom(bytes));
                break;

            default:
                // Complex/nested message type
                var nestedMessage = allMessages.FirstOrDefault(m => m.Name == fieldType);
                if (nestedMessage != null && value.ValueKind == JsonValueKind.Object)
                {
                    var nestedJson = value.GetRawText();
                    var nestedBytes = SerializeFromJson(nestedJson, nestedMessage, allMessages);
                    writer.WriteBytes(ByteString.CopyFrom(nestedBytes));
                }

                break;
        }
    }

    private WireFormat.WireType GetWireType(string fieldType)
    {
        return fieldType switch
        {
            "string" or "bytes" => WireFormat.WireType.LengthDelimited,
            "int32" or "int64" or "uint32" or "uint64" or "sint32" or "sint64" or "bool" => WireFormat.WireType.Varint,
            "fixed32" or "sfixed32" or "float" => WireFormat.WireType.Fixed32,
            "fixed64" or "sfixed64" or "double" => WireFormat.WireType.Fixed64,
            _ => WireFormat.WireType.LengthDelimited // Complex types
        };
    }

    /// <summary>
    ///     Creates a JSON template from a Protobuf message definition
    ///     This is used to show what the binary message contains
    /// </summary>
    public string CreateJsonTemplate(ProtoMessage messageDefinition, List<ProtoMessage> allMessages)
    {
        var fields = new Dictionary<string, object?>();

        foreach (var field in messageDefinition.Fields)
        {
            var value = GetDefaultValue(field, allMessages);
            fields[field.Name] = field.Repeated ? new[] { value } : value;
        }

        return JsonSerializer.Serialize(fields, new JsonSerializerOptions { WriteIndented = true });
    }

    private object? GetDefaultValue(ProtoField field, List<ProtoMessage> allMessages)
    {
        return field.Type switch
        {
            "string" => "string",
            "int32" or "int64" or "uint32" or "uint64" or "sint32" or "sint64"
                or "fixed32" or "fixed64" or "sfixed32" or "sfixed64" => 0,
            "bool" => false,
            "float" or "double" => 0.0,
            "bytes" => "base64string",
            _ => CreateNestedTemplate(field.Type, allMessages)
        };
    }

    private object? CreateNestedTemplate(string messageType, List<ProtoMessage> allMessages)
    {
        var nestedMessage = allMessages.FirstOrDefault(m => m.Name == messageType);
        if (nestedMessage == null) return null;

        var fields = new Dictionary<string, object?>();
        foreach (var field in nestedMessage.Fields) fields[field.Name] = GetDefaultValue(field, allMessages);
        return fields;
    }
}