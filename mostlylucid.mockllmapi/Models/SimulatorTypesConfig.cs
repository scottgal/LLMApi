namespace mostlylucid.mockllmapi.Models;

/// <summary>
///     Configuration for enabling/disabling different types of simulators and endpoints.
///     Allows fine-grained control over which API simulation features are active.
/// </summary>
public class SimulatorTypesConfig
{
    /// <summary>
    ///     Enable basic REST API mock endpoints (default: true)
    ///     When enabled, registers /api/mock/** and /api/mock/stream/** endpoints
    ///     These endpoints use shape-based mock generation
    /// </summary>
    public bool EnableRest { get; set; } = true;

    /// <summary>
    ///     Enable GraphQL mock endpoints (default: true)
    ///     When enabled, registers /api/graphql endpoint
    ///     Supports GraphQL queries with schema-based response generation
    /// </summary>
    public bool EnableGraphQL { get; set; } = true;

    /// <summary>
    ///     Enable gRPC mock services (default: true)
    ///     When enabled, registers gRPC services defined via proto files
    ///     Supports unary, server streaming, client streaming, and bidirectional streaming
    /// </summary>
    public bool EnableGrpc { get; set; } = true;

    /// <summary>
    ///     Enable SignalR hub endpoints (default: true)
    ///     When enabled, registers SignalR hubs configured via HubContexts
    ///     Provides real-time push notifications with continuous data generation
    /// </summary>
    public bool EnableSignalR { get; set; } = true;

    /// <summary>
    ///     Enable OpenAPI dynamic endpoints (default: true)
    ///     When enabled, allows loading OpenAPI/Swagger specs to create mock endpoints
    ///     Provides /api/openapi/* management endpoints and dynamic route registration
    /// </summary>
    public bool EnableOpenApi { get; set; } = true;

    /// <summary>
    ///     Enable pre-configured REST APIs (default: true)
    ///     When enabled, registers endpoints from RestApis configuration
    ///     Provides /api/configured/{name} endpoints with pre-defined settings
    /// </summary>
    public bool EnableConfiguredApis { get; set; } = true;

    /// <summary>
    ///     Enable management/admin endpoints (default: true)
    ///     When enabled, provides endpoints for:
    ///     - /api/contexts - View and manage API contexts
    ///     - /api/cache/stats - View cache statistics
    ///     - /api/openapi/specs - Manage OpenAPI specs
    ///     - /api/signalr/contexts - Manage SignalR contexts
    ///     - /api/grpc-protos - Manage gRPC proto definitions
    ///     Set to false for production-like scenarios where management endpoints should be hidden
    /// </summary>
    public bool EnableManagementEndpoints { get; set; } = true;
}