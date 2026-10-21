using Microsoft.AspNetCore.SignalR;

namespace Africanideasfaceapi.Hubs;

/// <summary>
/// SignalR Hub for broadcasting real-time analytics updates
/// </summary>
public class AnalyticsHub : Hub
{
    private readonly ILogger<AnalyticsHub> _logger;

    public AnalyticsHub(ILogger<AnalyticsHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected to AnalyticsHub: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected from AnalyticsHub: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Broadcast analytics refresh event to all connected clients
    /// </summary>
    public async Task BroadcastAnalyticsUpdate(string eventType, string message)
    {
        await Clients.All.SendAsync("AnalyticsUpdated", eventType, message);
    }
}
