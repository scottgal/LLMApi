using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using mostlylucid.mockllmapi.Services;

namespace mostlylucid.mockllmapi.Hubs;

/// <summary>
/// SignalR hub for streaming LLM-generated mock data
/// Supports multiple contexts/topics (weather, cars, stocks, etc.)
/// </summary>
public class MockLlmHub : Hub
{
    private readonly ILogger<MockLlmHub> _logger;
    private readonly IOptions<LLMockApiOptions> _options;
    private readonly DynamicHubContextManager _contextManager;

    public MockLlmHub(ILogger<MockLlmHub> logger, IOptions<LLMockApiOptions> options, DynamicHubContextManager contextManager)
    {
        _logger = logger;
        _options = options;
        _contextManager = contextManager;
    }

    /// <summary>
    /// Subscribe to a specific context (e.g., "weather", "cars")
    /// </summary>
    public async Task SubscribeToContext(string context)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, context);
        _logger.LogInformation("Client {ConnectionId} subscribed to context: {Context}", Context.ConnectionId, context);

        // Track connection counts for dynamic contexts
        if (_contextManager.ContextExists(context))
        {
            _contextManager.IncrementConnectionCount(context);
        }

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

        if (_contextManager.ContextExists(context))
        {
            _contextManager.DecrementConnectionCount(context);
        }

        await Clients.Caller.SendAsync("Unsubscribed", new { context, message = $"Unsubscribed from {context}" });
    }

    /// <summary>
    /// Get list of available contexts (configured + dynamic)
    /// </summary>
    public async Task GetAvailableContexts()
    {
        var configured = _options.Value.HubContexts
            .Where(c => c.IsActive)
            .Select(c => c.Name);
        var dynamic = _contextManager.GetAllContexts()
            .Where(c => c.IsActive)
            .Select(c => c.Name);
        var contexts = configured.Concat(dynamic).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        await Clients.Caller.SendAsync("AvailableContexts", new { contexts });
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
