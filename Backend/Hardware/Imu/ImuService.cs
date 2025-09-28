using Backend.Hubs;
using Backend.Hardware.Common;
using Backend.Storage;
using Microsoft.AspNetCore.SignalR;
using System.IO.Ports;

namespace Backend.Hardware.Imu;

public class ImuService : BackgroundService
{
    private readonly IHubContext<DataHub> _hubContext;
    private readonly ILogger<ImuService> _logger;
    private readonly ImuInitializer _imuInitializer;
    private readonly ImuParser _imuParser;
    private readonly DataFileWriter _dataFileWriter;
    private SerialPortManager? _serialPortManager;
    private readonly byte[] _buffer = new byte[1024];
    private readonly List<byte> _dataBuffer = new();
    private readonly object _dataBufferLock = new();
    private DateTime _lastSignalRSent = DateTime.MinValue;
    private readonly TimeSpan _signalRThrottleInterval = TimeSpan.FromMilliseconds(1000); // 1Hz = 1000ms interval
    private readonly object _throttleLock = new object();
    private bool _headerWritten = false;
    
    // Kbps tracking
    private long _totalBytesReceived = 0;
    private DateTime _kbpsStartTime = DateTime.UtcNow;
    private readonly object _kbpsLock = new object();

    public ImuService(
        IHubContext<DataHub> hubContext,
        ILogger<ImuService> logger,
        ImuInitializer imuInitializer,
        ILoggerFactory loggerFactory)
    {
        _hubContext = hubContext;
        _logger = logger;
        _imuInitializer = imuInitializer;
        _imuParser = new ImuParser(loggerFactory.CreateLogger<ImuParser>());
        _dataFileWriter = new DataFileWriter("IMU.txt", loggerFactory.CreateLogger<DataFileWriter>());

        // SerialPortManager will be obtained from ImuInitializer after initialization
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("IMU Service started");

        // Start the data file writer
        _ = Task.Run(() => _dataFileWriter.StartAsync(stoppingToken), stoppingToken);

        // Get the initialized SerialPortManager
        _serialPortManager = _imuInitializer.GetSerialPortManager();

        if (_serialPortManager == null || !_serialPortManager.IsConnected)
        {
            _logger.LogWarning("IMU not available - IMU service will not start");
            return;
        }

        _logger.LogInformation("IMU Service using pre-initialized SerialPortManager - listening for MEMS data at 50Hz, sending SignalR updates at 1Hz");

        // Subscribe to the already-started SerialPortManager
        _serialPortManager.DataReceived += OnSerialDataReceived;
        _logger.LogInformation("🔗 IMU SerialPortManager subscription completed");
        
        // Keep the service running until cancellation
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
        
        _logger.LogInformation("IMU Service stopped");
    }


    private void OnSerialDataReceived(object? sender, byte[] data)
    {
        try
        {
            ProcessIncomingData(data, data.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in IMU SerialPortManager data received handler");
        }
    }

    private void ProcessIncomingData(byte[] newData, int length)
    {
        // Track bytes for kbps calculation
        lock (_kbpsLock)
        {
            _totalBytesReceived += length;
        }
        
        // Add new data to buffer
        lock (_dataBufferLock)
        {
            for (int i = 0; i < length; i++)
            {
                _dataBuffer.Add(newData[i]);
            }
        }

        // Look for complete packets
        while (true)
        {
            byte[]? packetData = null;
            
            lock (_dataBufferLock)
            {
                if (_dataBuffer.Count < 52) // MEMS packet size
                {
                    break;
                }
                int packetStart = FindPacketStart();
                
                if (packetStart == -1)
                {
                    // No packet header found, remove first byte and continue
                    _dataBuffer.RemoveAt(0);
                    continue;
                }
                

                // Remove bytes before packet start
                if (packetStart > 0)
                {
                    _dataBuffer.RemoveRange(0, packetStart);
                }

                // Check if we have a complete packet
                if (_dataBuffer.Count >= 52)
                {
                    packetData = _dataBuffer.Take(52).ToArray();
                    _dataBuffer.RemoveRange(0, 52);
                }
                else
                {
                    // Not enough data for complete packet yet
                    break;
                }
            }
            
            if (packetData != null)
            {

                // Try to parse the packet
                var imuData = _imuParser.ParseMemsPacket(packetData);
                if (imuData != null)
                {
                    // Set system uptime timestamp
                    imuData.SystemUptimeMs = Environment.TickCount64;
                    // Write CSV header if this is the first data
                    if (!_headerWritten)
                    {
                        var csvHeader = "system_uptime_ms,timestamp,accel_x,accel_y,accel_z,gyro_x,gyro_y,gyro_z,mag_x,mag_y,mag_z";
                        _dataFileWriter.WriteData(csvHeader);
                        _headerWritten = true;
                    }

                    // Log data to file
                    var csvLine = $"{imuData.SystemUptimeMs},{imuData.Timestamp:F2},{imuData.Acceleration.X:F4},{imuData.Acceleration.Y:F4},{imuData.Acceleration.Z:F4},{imuData.Gyroscope.X:F4},{imuData.Gyroscope.Y:F4},{imuData.Gyroscope.Z:F4},{imuData.Magnetometer.X:F2},{imuData.Magnetometer.Y:F2},{imuData.Magnetometer.Z:F2}";
                    _dataFileWriter.WriteData(csvLine);

                    // Send data via SignalR with throttling to 1Hz
                    bool shouldSend = false;
                    lock (_throttleLock)
                    {
                        var now = DateTime.UtcNow;
                        if (now - _lastSignalRSent >= _signalRThrottleInterval)
                        {
                            _lastSignalRSent = now;
                            shouldSend = true;
                        }
                    }

                    if (shouldSend)
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var kbps = CalculateKbps();
                                await _hubContext.Clients.All.SendAsync("ImuUpdate", new ImuUpdate
                                {
                                    Timestamp = imuData.Timestamp,
                                    Acceleration = new Vector3Update { X = imuData.Acceleration.X, Y = imuData.Acceleration.Y, Z = imuData.Acceleration.Z },
                                    Gyroscope = new Vector3Update { X = imuData.Gyroscope.X, Y = imuData.Gyroscope.Y, Z = imuData.Gyroscope.Z },
                                    Magnetometer = new Vector3Update { X = imuData.Magnetometer.X, Y = imuData.Magnetometer.Y, Z = imuData.Magnetometer.Z },
                                    Kbps = kbps
                                });
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to send IMU update via SignalR");
                            }
                        });
                    }
                }
                else
                {
                }
            }
        }

        // Prevent buffer from growing too large
        lock (_dataBufferLock)
        {
            if (_dataBuffer.Count > 1024)
            {
                _logger.LogWarning("IMU data buffer too large, clearing buffer");
                _dataBuffer.Clear();
            }
        }
    }

    private int FindPacketStart()
    {
        // Look for "fmi" header
        for (int i = 0; i <= _dataBuffer.Count - 4; i++)
        {
            if (_dataBuffer[i] == (byte)'f' && 
                _dataBuffer[i + 1] == (byte)'m' && 
                _dataBuffer[i + 2] == (byte)'i' &&
                _dataBuffer[i + 3] == (byte)'m') // MEMS type
            {
                return i;
            }
        }
        
        // Log what we actually found at the start of buffer for debugging
        if (_dataBuffer.Count >= 4)
        {
            var firstFour = string.Join("", _dataBuffer.Take(4).Select(b => (char)b));
            var firstFourHex = string.Join(" ", _dataBuffer.Take(4).Select(b => $"{b:X2}"));
        }
        return -1;
    }

    private double CalculateKbps()
    {
        lock (_kbpsLock)
        {
            var elapsed = DateTime.UtcNow - _kbpsStartTime;
            if (elapsed.TotalSeconds < 1.0) // Avoid division by zero for very short periods
            {
                return 0.0;
            }
            
            var kbps = (_totalBytesReceived * 8.0) / 1000.0 / elapsed.TotalSeconds;
            return Math.Round(kbps, 2);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping IMU Service");

        // Unsubscribe from SerialPortManager (don't stop it - ImuInitializer owns it)
        if (_serialPortManager != null)
        {
            _serialPortManager.DataReceived -= OnSerialDataReceived;
            _logger.LogInformation("📡 IMU SerialPortManager unsubscribed");
        }

        await _dataFileWriter.StopAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _logger.LogInformation("IMU Service disposing");
        // Don't dispose SerialPortManager - ImuInitializer owns it
        _dataFileWriter.Dispose();
        base.Dispose();
    }
}