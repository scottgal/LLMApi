namespace mostlylucid.mockllmapi.Models;

/// <summary>
///     Represents a complete parsed .proto file
/// </summary>
public class ProtoDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Package { get; set; } = string.Empty;
    public List<ProtoService> Services { get; set; } = new();
    public List<ProtoMessage> Messages { get; set; } = new();
    public string Syntax { get; set; } = "proto3"; // proto2 or proto3
    public string RawContent { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
///     Represents a gRPC service definition
/// </summary>
public class ProtoService
{
    public string Name { get; set; } = string.Empty;
    public List<ProtoMethod> Methods { get; set; } = new();
}

/// <summary>
///     Represents a gRPC method (RPC)
/// </summary>
public class ProtoMethod
{
    public string Name { get; set; } = string.Empty;
    public string InputType { get; set; } = string.Empty;
    public string OutputType { get; set; } = string.Empty;
    public bool ClientStreaming { get; set; }
    public bool ServerStreaming { get; set; }

    public MethodType GetMethodType()
    {
        if (!ClientStreaming && !ServerStreaming)
            return MethodType.Unary;
        if (!ClientStreaming && ServerStreaming)
            return MethodType.ServerStreaming;
        if (ClientStreaming && !ServerStreaming)
            return MethodType.ClientStreaming;
        return MethodType.BidirectionalStreaming;
    }
}

/// <summary>
///     Represents a protobuf message definition
/// </summary>
public class ProtoMessage
{
    public string Name { get; set; } = string.Empty;
    public List<ProtoField> Fields { get; set; } = new();
}

/// <summary>
///     Represents a field in a protobuf message
/// </summary>
public class ProtoField
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int Number { get; set; }
    public bool Repeated { get; set; }
    public bool Optional { get; set; }
}

/// <summary>
///     gRPC method types
/// </summary>
public enum MethodType
{
    Unary,
    ServerStreaming,
    ClientStreaming,
    BidirectionalStreaming
}