using Backend.Hubs;
using Microsoft.AspNetCore.SignalR;
using System.Globalization;

namespace Backend.Services;

public class SystemMonitoringService : BackgroundService
{
    private readonly IHubContext<DataHub> _hubContext;
    private readonly ILogger<SystemMonitoringService> _logger;

    // Track previous totals for /proc/stat deltas
    private long _prevIdleAll = 0;   // idle + iowait
    private long _prevTotal = 0;   // idleAll + nonIdle

    public SystemMonitoringService(IHubContext<DataHub> hubContext, ILogger<SystemMonitoringService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("System Monitoring Service started - broadcasting at 1Hz");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var systemHealth = await GatherSystemHealth();

                await _hubContext.Clients.All.SendAsync("SystemHealthUpdate", new
                {
                    cpuUsage = systemHealth.CpuUsage,
                    memoryUsage = systemHealth.MemoryUsage,
                    temperature = systemHealth.Temperature
                }, stoppingToken);

                _logger.LogDebug("System health update sent: CPU={CpuUsage:F1}%, Memory={MemoryUsage:F1}%, Temp={Temperature:F1}°C",
                    systemHealth.CpuUsage, systemHealth.MemoryUsage, systemHealth.Temperature);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error gathering system health data");
            }

            await Task.Delay(1000, stoppingToken); // ~1 Hz
        }
    }

    private async Task<SystemHealth> GatherSystemHealth()
    {
        var cpuUsage = await GetCpuUsage();
        var memoryUsage = await GetMemoryUsage();
        var temperature = await GetTemperature();

        return new SystemHealth
        {
            CpuUsage = cpuUsage,
            MemoryUsage = memoryUsage,
            Temperature = temperature
        };
    }

    /// <summary>
    /// CPU usage from /proc/stat deltas.
    /// 100% = all cores busy (no idle or iowait).
    /// </summary>
    private async Task<double> GetCpuUsage()
    {
        try
        {
            // /proc/stat first line: cpu  user nice system idle iowait irq softirq steal guest guest_nice
            var stat = await File.ReadAllTextAsync("/proc/stat");
            var firstLine = stat.Split('\n').FirstOrDefault(l => l.StartsWith("cpu "));
            if (firstLine is null)
            {
                _logger.LogWarning("Could not find aggregate 'cpu' line in /proc/stat");
                return 0.0;
            }

            var parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // parts[0] = "cpu"
            if (parts.Length < 8) // need at least up to softirq
            {
                _logger.LogWarning("Unexpected /proc/stat cpu format: {Line}", firstLine);
                return 0.0;
            }

            long user = long.Parse(parts[1], CultureInfo.InvariantCulture);
            long nice = long.Parse(parts[2], CultureInfo.InvariantCulture);
            long system = long.Parse(parts[3], CultureInfo.InvariantCulture);
            long idle = long.Parse(parts[4], CultureInfo.InvariantCulture);
            long iowait = long.Parse(parts[5], CultureInfo.InvariantCulture);
            long irq = long.Parse(parts[6], CultureInfo.InvariantCulture);
            long softirq = long.Parse(parts[7], CultureInfo.InvariantCulture);
            long steal = parts.Length > 8 ? long.Parse(parts[8], CultureInfo.InvariantCulture) : 0;

            long idleAll = idle + iowait;
            long nonIdle = user + nice + system + irq + softirq + steal;
            long total = idleAll + nonIdle;

            if (_prevTotal == 0 && _prevIdleAll == 0)
            {
                // First sample: seed and return 0 for this tick; next tick will be accurate
                _prevTotal = total;
                _prevIdleAll = idleAll;
                _logger.LogDebug("Seeded /proc/stat counters; first sample returns 0%");
                return 0.0;
            }

            long totalDelta = total - _prevTotal;
            long idleDelta = idleAll - _prevIdleAll;

            _prevTotal = total;
            _prevIdleAll = idleAll;

            if (totalDelta <= 0)
            {
                _logger.LogDebug("Non-positive totalDelta ({TotalDelta}); returning 0%", totalDelta);
                return 0.0;
            }

            // Busy% = 1 - (idle time share)
            double usage = (1.0 - (double)idleDelta / totalDelta) * 100.0;
            usage = Math.Clamp(usage, 0.0, 100.0);

            _logger.LogDebug("CPU usage from /proc/stat: {Usage:F1}% (idle share {IdleShare:P1})",
                usage, (double)idleDelta / totalDelta);

            return usage;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get CPU usage from /proc/stat");
            return 0.0;
        }
    }

    private async Task<double> GetMemoryUsage()
    {
        try
        {
            var memInfo = await File.ReadAllTextAsync("/proc/meminfo");
            var lines = memInfo.Split('\n');

            long memTotal = 0;
            long memAvailable = 0;

            foreach (var line in lines)
            {
                if (line.StartsWith("MemTotal:"))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2) memTotal = long.Parse(parts[1], CultureInfo.InvariantCulture);
                }
                else if (line.StartsWith("MemAvailable:"))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2) memAvailable = long.Parse(parts[1], CultureInfo.InvariantCulture);
                }
            }

            if (memTotal == 0) return 0.0;

            var memUsed = memTotal - memAvailable;
            var memUsagePercent = (double)memUsed / memTotal * 100.0;

            return Math.Clamp(memUsagePercent, 0.0, 100.0);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read memory usage");
            return 0.0;
        }
    }

    private async Task<double> GetTemperature()
    {
        try
        {
            var tempString = await File.ReadAllTextAsync("/sys/class/thermal/thermal_zone0/temp");
            var tempMillicelsius = long.Parse(tempString.Trim(), CultureInfo.InvariantCulture);
            return tempMillicelsius / 1000.0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read CPU temperature");
            return 0.0;
        }
    }
}

public class SystemHealth
{
    public double CpuUsage { get; set; }
    public double MemoryUsage { get; set; }
    public double Temperature { get; set; }
}
