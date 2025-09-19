using Backend.Hubs;
using Backend.Configuration;
using Microsoft.AspNetCore.SignalR;
using System.Globalization;
using System.Device.I2c;
using System.Device.Gpio;

namespace Backend.GnssSystem;

public class SystemMonitoringService : BackgroundService
{
    private readonly IHubContext<DataHub> _hubContext;
    private readonly ILogger<SystemMonitoringService> _logger;

    // Track previous totals for /proc/stat deltas
    private long _prevIdleAll = 0;   // idle + iowait
    private long _prevTotal = 0;   // idleAll + nonIdle

    // MAX17040/MAX17041 I2C constants
    private const byte MAX17040_ADDRESS = 0x36;
    private const byte VCELL_REG = 0x02;
    private const byte SOC_REG = 0x04;
    private const byte CONFIG_REG = 0x0C;
    private I2cDevice? _i2cDevice;

    // GPIO6 Power Loss Detect (PLD) pin - HIGH = power connected, LOW = battery
    private const int POWER_LOSS_DETECT_PIN = 6;
    private GpioController? _gpioController;

    public SystemMonitoringService(IHubContext<DataHub> hubContext, ILogger<SystemMonitoringService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
        InitializeI2c();
        InitializeGpio();
    }

    private void InitializeI2c()
    {
        try
        {
            var settings = new I2cConnectionSettings(1, MAX17040_ADDRESS);
            _i2cDevice = I2cDevice.Create(settings);
            _logger.LogInformation("MAX17040 I2C device initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize MAX17040 I2C device - battery monitoring will be unavailable");
        }
    }

    private void InitializeGpio()
    {
        try
        {
            _gpioController = new GpioController();
            
            // Check if pin is available before opening
            if (!_gpioController.IsPinOpen(POWER_LOSS_DETECT_PIN))
            {
                _gpioController.OpenPin(POWER_LOSS_DETECT_PIN, PinMode.Input);
                _logger.LogInformation("GPIO{Pin} Power Loss Detect pin initialized successfully", POWER_LOSS_DETECT_PIN);
                
                // Test initial read to verify functionality
                var testValue = _gpioController.Read(POWER_LOSS_DETECT_PIN);
                _logger.LogDebug("Initial GPIO{Pin} test read: {Value} ({State})", 
                    POWER_LOSS_DETECT_PIN, testValue, testValue == PinValue.Low ? "LOW" : "HIGH");
            }
            else
            {
                _logger.LogWarning("GPIO{Pin} is already open - this may indicate a hardware or driver issue", POWER_LOSS_DETECT_PIN);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Access denied to GPIO{Pin} - check permissions and hardware availability", POWER_LOSS_DETECT_PIN);
            _gpioController?.Dispose();
            _gpioController = null;
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "GPIO{Pin} is not available on this hardware platform", POWER_LOSS_DETECT_PIN);
            _gpioController?.Dispose();
            _gpioController = null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize GPIO{Pin} Power Loss Detect pin - power status detection will be unavailable", POWER_LOSS_DETECT_PIN);
            _gpioController?.Dispose();
            _gpioController = null;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("System Monitoring Service started - broadcasting at 1Hz");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var systemHealth = await GatherSystemHealth();

                await _hubContext.Clients.All.SendAsync("SystemHealthUpdate", new SystemHealthUpdate
                {
                    CpuUsage = systemHealth.CpuUsage,
                    MemoryUsage = systemHealth.MemoryUsage,
                    Temperature = systemHealth.Temperature,
                    BatteryLevel = systemHealth.BatteryLevel,
                    BatteryVoltage = systemHealth.BatteryVoltage,
                    IsExternalPowerConnected = systemHealth.IsExternalPowerConnected
                }, stoppingToken);

                // Send corrections status update
                await _hubContext.Clients.All.SendAsync("CorrectionsStatusUpdate", new CorrectionsStatusUpdate
                {
                    Mode = SystemConfiguration.CorrectionsOperation.ToString(),
                    Active = SystemConfiguration.CorrectionsOperation != SystemConfiguration.CorrectionsMode.Disabled
                }, stoppingToken);

                _logger.LogDebug("System health update sent: CPU={CpuUsage:F1}%, Memory={MemoryUsage:F1}%, Temp={Temperature:F1}°C, Battery={BatteryLevel:F1}%, Voltage={BatteryVoltage:F2}V, ExternalPower={IsExternalPowerConnected}",
                    systemHealth.CpuUsage, systemHealth.MemoryUsage, systemHealth.Temperature, systemHealth.BatteryLevel, systemHealth.BatteryVoltage, systemHealth.IsExternalPowerConnected);
                
                // Enhanced power detection diagnostics (every 10 seconds to avoid spam)
                if (DateTime.UtcNow.Second % 10 == 0)
                {
                    LogPowerDetectionDiagnostics(systemHealth);
                }
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
        var batteryData = GetBatteryData();

        return new SystemHealth
        {
            CpuUsage = cpuUsage,
            MemoryUsage = memoryUsage,
            Temperature = temperature,
            BatteryLevel = batteryData.Level,
            BatteryVoltage = batteryData.Voltage,
            IsExternalPowerConnected = batteryData.IsExternalPowerConnected
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

    private bool ReadPowerLossDetect()
    {
        if (_gpioController == null)
        {
            _logger.LogDebug("GPIO controller not initialized - defaulting to battery power");
            return false; // Assume power lost if GPIO not available
        }

        try
        {
            var pinValue = _gpioController.Read(POWER_LOSS_DETECT_PIN);
            
            // GPIO6 PLD: HIGH = power connected, LOW = battery power
            var isExternalPowerConnected = pinValue == PinValue.High;
            
            _logger.LogDebug("GPIO{Pin} Power Loss Detect: {PinValue} ({PinState}) -> External Power: {ExternalPower}", 
                POWER_LOSS_DETECT_PIN, pinValue, pinValue == PinValue.Low ? "LOW" : "HIGH", isExternalPowerConnected);
            
            return isExternalPowerConnected;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read GPIO{Pin} Power Loss Detect pin", POWER_LOSS_DETECT_PIN);
            return false; // Assume power lost on error
        }
    }

    private (double Level, double Voltage, bool IsExternalPowerConnected) GetBatteryData()
    {
        // Read external power status from GPIO PLD
        var isExternalPowerConnected = ReadPowerLossDetect();

        if (_i2cDevice == null)
        {
            return (0.0, 0.0, isExternalPowerConnected);
        }

        try
        {
            // Read battery voltage (VCELL register)
            var voltageBytes = new byte[2];
            _i2cDevice.WriteRead(new byte[] { VCELL_REG }, voltageBytes);
            var voltageRaw = (voltageBytes[0] << 8) | voltageBytes[1];
            var voltage = (voltageRaw >> 4) * 0.00125; // 1.25mV per bit

            // Read State of Charge (SOC register)
            var socBytes = new byte[2];
            _i2cDevice.WriteRead(new byte[] { SOC_REG }, socBytes);
            var socRaw = (socBytes[0] << 8) | socBytes[1];
            var batteryLevel = socRaw / 256.0; // 1/256% per bit


            return (Math.Clamp(batteryLevel, 0.0, 100.0), voltage, isExternalPowerConnected);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read battery data from MAX17040");
            return (0.0, 0.0, isExternalPowerConnected);
        }
    }


    /// <summary>
    /// Logs detailed power detection diagnostics for troubleshooting
    /// </summary>
    private void LogPowerDetectionDiagnostics(SystemHealth health)
    {
        var diagnostics = new List<string>();
        
        // Hardware status
        diagnostics.Add($"GPIO Controller: {(_gpioController != null ? "Available" : "NULL")}");
        diagnostics.Add($"I2C Device: {(_i2cDevice != null ? "Available" : "NULL")}");
        
        // Current readings
        diagnostics.Add($"Battery Voltage: {health.BatteryVoltage:F3}V");
        diagnostics.Add($"External Power: {health.IsExternalPowerConnected}");
        
        // GPIO pin state if available
        if (_gpioController != null)
        {
            try
            {
                var pinValue = _gpioController.Read(POWER_LOSS_DETECT_PIN);
                diagnostics.Add($"GPIO{POWER_LOSS_DETECT_PIN} Raw State: {pinValue} ({(pinValue == PinValue.Low ? "LOW" : "HIGH")})");
            }
            catch (Exception ex)
            {
                diagnostics.Add($"GPIO{POWER_LOSS_DETECT_PIN} Read Error: {ex.Message}");
            }
        }
        
        //_logger.LogInformation("Power Detection Diagnostics: {Diagnostics}", string.Join(" | ", diagnostics));
    }

    public override void Dispose()
    {
        _i2cDevice?.Dispose();
        _gpioController?.Dispose();
        base.Dispose();
    }
}

public class SystemHealth
{
    public double CpuUsage { get; set; }
    public double MemoryUsage { get; set; }
    public double Temperature { get; set; }
    public double BatteryLevel { get; set; }
    public double BatteryVoltage { get; set; }
    public bool IsExternalPowerConnected { get; set; }
}
