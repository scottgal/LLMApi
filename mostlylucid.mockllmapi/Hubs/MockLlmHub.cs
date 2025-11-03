using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace mostlylucid.mockllmapi.Hubs;

/// <summary>
/// SignalR hub for streaming LLM-generated mock data
/// Supports multiple contexts/topics (weather, cars, stocks, etc.)
/// </summary>
public class MockLlmHub : Hub
{
    private readonly ILogger<MockLlmHub> _logger;

    public MockLlmHub(ILogger<MockLlmHub> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Subscribe to a specific context (e.g., "weather", "cars")
    /// </summary>
    public async Task SubscribeToContext(string context)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, context);
        _logger.LogInformation("Client {ConnectionId} subscribed to context: {Context}", Context.ConnectionId, context);

        // Send immediate confirmation
        await Clients.Caller.SendAsync("Subscribed", new { context, message = $"Subscribed to {context}" });
    }

    /// <summary>
    /// Unsubscribe from a specific context
    /// </summary>
    public async Task UnsubscribeFromContext(string context)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, context);
        _logger.LogInformation("Client {ConnectionId} unsubscribed from context: {Context}", Context.ConnectionId, context);

        await Clients.Caller.SendAsync("Unsubscribed", new { context, message = $"Unsubscribed from {context}" });
    }

    /// <summary>
    /// Get list of available contexts
    /// </summary>
    public async Task GetAvailableContexts()
    {
        // This will be populated from configuration
        await Clients.Caller.SendAsync("AvailableContexts", new { contexts = new[] { "default" } });
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();

        // Send welcome message
        await Clients.Caller.SendAsync("Connected", new
        {
            connectionId = Context.ConnectionId,
            message = "Connected to MockLLM Hub. Use SubscribeToContext(contextName) to receive data."
        });
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
