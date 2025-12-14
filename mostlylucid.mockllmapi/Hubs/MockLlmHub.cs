using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using mostlylucid.mockllmapi.Services;

namespace mostlylucid.mockllmapi.Hubs;

/// <summary>
///     SignalR hub for streaming LLM-generated mock data
///     Supports multiple contexts/topics (weather, cars, stocks, etc.)
/// </summary>
public class MockLlmHub : Hub
{
    private readonly DynamicHubContextManager _contextManager;
    private readonly ILogger<MockLlmHub> _logger;

    public MockLlmHub(ILogger<MockLlmHub> logger, DynamicHubContextManager contextManager)
    {
        _logger = logger;
        _contextManager = contextManager;
    }

    /// <summary>
    ///     Subscribe to a specific context (e.g., "weather", "cars")
    /// </summary>
    public async Task SubscribeToContext(string context)
    {
        _logger.LogInformation("SubscribeToContext called: ConnectionId={ConnectionId}, Context={Context}",
            Context.ConnectionId, context);

        await Groups.AddToGroupAsync(Context.ConnectionId, context);
        _logger.LogInformation("Added to SignalR group: {Context}", context);

        _contextManager.IncrementConnectionCount(context);
        _logger.LogInformation("Incremented connection count for context: {Context}", context);

        var contextInfo = _contextManager.GetContext(context);
        _logger.LogInformation("Context info after increment: Name={Name}, ConnectionCount={Count}, IsActive={Active}",
            contextInfo?.Name, contextInfo?.ConnectionCount, contextInfo?.IsActive);

        // Send immediate confirmation
        await Clients.Caller.SendAsync("Subscribed", new { context, message = $"Subscribed to {context}" });
        _logger.LogInformation("Sent Subscribed confirmation to client for context: {Context}", context);
    }

    /// <summary>
    ///     Unsubscribe from a specific context
    /// </summary>
    public async Task UnsubscribeFromContext(string context)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, context);
        _contextManager.DecrementConnectionCount(context);
        _logger.LogInformation("Client {ConnectionId} unsubscribed from context: {Context}", Context.ConnectionId,
            context);

        await Clients.Caller.SendAsync("Unsubscribed", new { context, message = $"Unsubscribed from {context}" });
    }

    /// <summary>
    ///     Get list of available contexts
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