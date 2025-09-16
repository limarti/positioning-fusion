using Microsoft.AspNetCore.SignalR;

namespace Backend.Hubs;

public class DataHub : Hub
{
    public async Task SendPositionUpdate(double latitude, double longitude)
    {
        await Clients.All.SendAsync("PositionUpdate", new { latitude, longitude });
    }
}