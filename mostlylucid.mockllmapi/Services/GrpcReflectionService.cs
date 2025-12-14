using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using mostlylucid.mockllmapi.Models;

namespace mostlylucid.mockllmapi.Services;

/// <summary>
///     Provides gRPC server reflection information for uploaded proto files
///     Note: This is a simplified implementation for tracking protos.
///     Full gRPC reflection protocol (grpc.reflection.v1alpha.ServerReflection) is not yet implemented.
/// </summary>
public class GrpcReflectionService
{
    private readonly ILogger<GrpcReflectionService> _logger;
    private readonly ProtoDefinitionManager _protoManager;
    private readonly ConcurrentDictionary<string, ProtoReflectionInfo> _reflectionInfo = new();

    public GrpcReflectionService(
        ProtoDefinitionManager protoManager,
        ILogger<GrpcReflectionService> logger)
    {
        _protoManager = protoManager;
        _logger = logger;
    }

    /// <summary>
    ///     Records that a proto file was uploaded and parsed
    /// </summary>
    public void RegisterProto(string protoName, ProtoDefinition definition)
    {
        var info = new ProtoReflectionInfo
        {
            Name = protoName,
            Package = definition.Package,
            Services = definition.Services.Select(s => s.Name).ToList(),
            UploadedAt = definition.UploadedAt
        };

        _reflectionInfo[protoName] = info;
        _logger.LogInformation("Registered proto for reflection: {ProtoName} with {ServiceCount} services",
            protoName, info.Services.Count);
    }

    /// <summary>
    ///     Get all service names from registered protos
    /// </summary>
    public IEnumerable<string> GetServiceNames()
    {
        var serviceNames = new List<string>();

        foreach (var info in _reflectionInfo.Values)
        foreach (var serviceName in info.Services)
        {
            // Format as fully qualified name if package exists
            var fullName = string.IsNullOrEmpty(info.Package)
                ? serviceName
                : $"{info.Package}.{serviceName}";

            serviceNames.Add(fullName);
        }

        return serviceNames;
    }

    /// <summary>
    ///     Get reflection info for a specific proto
    /// </summary>
    public ProtoReflectionInfo? GetProtoInfo(string protoName)
    {
        _reflectionInfo.TryGetValue(protoName, out var info);
        return info;
    }

    /// <summary>
    ///     Remove proto reflection info when proto is deleted
    /// </summary>
    public void RemoveProto(string protoName)
    {
        _reflectionInfo.TryRemove(protoName, out _);
        _logger.LogInformation("Removed proto from reflection: {ProtoName}", protoName);
    }

    /// <summary>
    ///     Clear all reflection info
    /// </summary>
    public void ClearAll()
    {
        _reflectionInfo.Clear();
        _logger.LogInformation("Cleared all proto reflection info");
    }
}

/// <summary>
///     Stores reflection information for a proto file
/// </summary>
public class ProtoReflectionInfo
{
    public required string Name { get; init; }
    public string Package { get; init; } = string.Empty;
    public List<string> Services { get; init; } = new();
    public DateTime UploadedAt { get; init; }
}