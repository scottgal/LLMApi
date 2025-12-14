using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using mostlylucid.mockllmapi.Models;

namespace mostlylucid.mockllmapi.Services;

/// <summary>
///     Manages proto file definitions and provides lookups for gRPC services
/// </summary>
public class ProtoDefinitionManager
{
    private readonly ConcurrentDictionary<string, ProtoDefinition> _definitions = new();
    private readonly ILogger<ProtoDefinitionManager> _logger;
    private readonly ProtoParser _parser = new();
    private GrpcReflectionService? _reflectionService;

    public ProtoDefinitionManager(ILogger<ProtoDefinitionManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    ///     Sets the reflection service (called by DI after both services are created)
    /// </summary>
    public void SetReflectionService(GrpcReflectionService reflectionService)
    {
        _reflectionService = reflectionService;
    }

    /// <summary>
    ///     Adds a proto definition from raw proto content
    /// </summary>
    public ProtoDefinition AddProtoDefinition(string protoContent, string fileName = "uploaded.proto")
    {
        var definition = _parser.Parse(protoContent, fileName);
        _definitions[definition.Name] = definition;

        // Register with gRPC reflection service if available
        if (_reflectionService != null) _reflectionService.RegisterProto(definition.Name, definition);

        return definition;
    }

    /// <summary>
    ///     Gets all registered proto definitions
    /// </summary>
    public List<ProtoDefinition> GetAllDefinitions()
    {
        return _definitions.Values.ToList();
    }

    /// <summary>
    ///     Gets a specific proto definition by name
    /// </summary>
    public ProtoDefinition? GetDefinition(string name)
    {
        _definitions.TryGetValue(name, out var definition);
        return definition;
    }

    /// <summary>
    ///     Finds a service across all definitions
    /// </summary>
    public (ProtoDefinition? Definition, ProtoService? Service) FindService(string serviceName)
    {
        foreach (var definition in _definitions.Values)
        {
            var service = definition.Services.FirstOrDefault(s =>
                s.Name.Equals(serviceName, StringComparison.OrdinalIgnoreCase));

            if (service != null) return (definition, service);
        }

        return (null, null);
    }

    /// <summary>
    ///     Finds a method in a service
    /// </summary>
    public (ProtoDefinition? Definition, ProtoService? Service, ProtoMethod? Method)
        FindMethod(string serviceName, string methodName)
    {
        var (definition, service) = FindService(serviceName);

        if (service == null) return (null, null, null);

        var method = service.Methods.FirstOrDefault(m =>
            m.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase));

        return (definition, service, method);
    }

    /// <summary>
    ///     Gets a message definition by name from a specific proto definition
    /// </summary>
    public ProtoMessage? GetMessage(ProtoDefinition definition, string messageName)
    {
        return definition.Messages.FirstOrDefault(m =>
            m.Name.Equals(messageName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     Removes a proto definition
    /// </summary>
    public bool RemoveDefinition(string name)
    {
        var removed = _definitions.TryRemove(name, out _);

        if (removed && _reflectionService != null) _reflectionService.RemoveProto(name);

        return removed;
    }

    /// <summary>
    ///     Clears all definitions
    /// </summary>
    public void ClearAll()
    {
        _definitions.Clear();

        if (_reflectionService != null) _reflectionService.ClearAll();
    }

    /// <summary>
    ///     Gets parser instance for generating JSON shapes
    /// </summary>
    public ProtoParser GetParser()
    {
        return _parser;
    }
}