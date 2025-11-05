using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace mostlylucid.mockllmapi.Hubs;

/// <summary>
/// SignalR hub for OpenAPI spec management real-time updates
/// </summary>
public class OpenApiHub : Hub
{
    private readonly ILogger<OpenApiHub> _logger;

    public OpenApiHub(ILogger<OpenApiHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Broadcasts spec loaded event to all clients
    /// </summary>
    public async Task NotifySpecLoaded(string specName, int endpointCount)
    {
        await Clients.All.SendAsync("SpecLoaded", new
        {
            specName,
            endpointCount,
            timestamp = DateTimeOffset.UtcNow
        });
    }

    /// <summary>
    /// Broadcasts spec deleted event to all clients
    /// </summary>
    public async Task NotifySpecDeleted(string specName)
    {
        await Clients.All.SendAsync("SpecDeleted", new
        {
            specName,
            timestamp = DateTimeOffset.UtcNow
        });
    }

    /// <summary>
    /// Broadcasts spec reloaded event to all clients
    /// </summary>
    public async Task NotifySpecReloaded(string specName, int endpointCount)
    {
        await Clients.All.SendAsync("SpecReloaded", new
        {
            specName,
            endpointCount,
            timestamp = DateTimeOffset.UtcNow
        });
    }

    /// <summary>
    /// Broadcasts endpoint test result to all clients
    /// </summary>
    public async Task NotifyEndpointTested(string specName, string path, string method, bool success)
    {
        await Clients.All.SendAsync("EndpointTested", new
        {
            specName,
            path,
            method,
            success,
            timestamp = DateTimeOffset.UtcNow
        });
    }
}
