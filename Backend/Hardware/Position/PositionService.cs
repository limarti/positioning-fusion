using Backend.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Backend.Hardware.Position;

public class PositionService : BackgroundService
{
    private readonly IHubContext<DataHub> _hubContext;
    private readonly ILogger<PositionService> _logger;
    
    // Base position (from frontend mock data)
    private double _baseLat = 45.4234567;
    private double _baseLng = -75.6987654;
    
    // Current position with small random variations
    private double _currentLat;
    private double _currentLng;
    private readonly Random _random = new();

    public PositionService(IHubContext<DataHub> hubContext, ILogger<PositionService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
        _currentLat = _baseLat;
        _currentLng = _baseLng;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Position Service started - broadcasting at 5Hz");
        
        while (!stoppingToken.IsCancellationRequested)
        {
            // Generate small random position changes (simulate real movement)
            var latChange = (_random.NextDouble() - 0.5) * 0.0001; // ~11m max change
            var lngChange = (_random.NextDouble() - 0.5) * 0.0001;
            
            _currentLat += latChange;
            _currentLng += lngChange;
            
            // Keep within reasonable bounds of base position
            _currentLat = Math.Max(_baseLat - 0.001, Math.Min(_baseLat + 0.001, _currentLat));
            _currentLng = Math.Max(_baseLng - 0.001, Math.Min(_baseLng + 0.001, _currentLng));
            
            // Broadcast position update
            await _hubContext.Clients.All.SendAsync("PositionUpdate", new { 
                latitude = _currentLat, 
                longitude = _currentLng 
            }, stoppingToken);
            
            // _logger.LogDebug("Position update sent: {Lat:F7}, {Lng:F7}", _currentLat, _currentLng);
            
            // Wait 200ms for 5Hz update rate
            await Task.Delay(200, stoppingToken);
        }
    }
}