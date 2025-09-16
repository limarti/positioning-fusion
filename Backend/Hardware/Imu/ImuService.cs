using Backend.Hubs;
using Microsoft.AspNetCore.SignalR;
using System.IO.Ports;

namespace Backend.Hardware.Imu;

public class ImuService : BackgroundService
{
    private readonly IHubContext<DataHub> _hubContext;
    private readonly ILogger<ImuService> _logger;
    private readonly ImuInitializer _imuInitializer;
    private readonly ImuParser _imuParser;
    private SerialPort? _serialPort;
    private readonly byte[] _buffer = new byte[1024];
    private readonly List<byte> _dataBuffer = new();
    private DateTime _lastSignalRSent = DateTime.MinValue;
    private readonly TimeSpan _signalRThrottleInterval = TimeSpan.FromMilliseconds(1000); // 1Hz = 1000ms interval
    private readonly object _throttleLock = new object();

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
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("IMU Service started");

        // Get the initialized serial port
        _serialPort = _imuInitializer.GetSerialPort();
        
        if (_serialPort == null || !_serialPort.IsOpen)
        {
            _logger.LogWarning("IMU serial port not available - IMU service will not start");
            return;
        }

        _logger.LogInformation("IMU Service connected to serial port - listening for MEMS data at 50Hz, sending SignalR updates at 1Hz");

        try
        {
            while (!stoppingToken.IsCancellationRequested && _serialPort.IsOpen)
            {
                if (_serialPort.BytesToRead > 0)
                {
                    int bytesRead = _serialPort.Read(_buffer, 0, _buffer.Length);
                    ProcessIncomingData(_buffer, bytesRead);
                }
                
                // Small delay to prevent excessive CPU usage
                await Task.Delay(10, stoppingToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in IMU service execution");
        }
        
        _logger.LogInformation("IMU Service stopped");
    }

    private void ProcessIncomingData(byte[] newData, int length)
    {
        // Add new data to buffer
        for (int i = 0; i < length; i++)
        {
            _dataBuffer.Add(newData[i]);
        }

        // Look for complete packets
        while (_dataBuffer.Count >= 52) // MEMS packet size
        {
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
                var packetData = _dataBuffer.Take(52).ToArray();
                _dataBuffer.RemoveRange(0, 52);

                // Try to parse the packet
                var imuData = _imuParser.ParseMemsPacket(packetData);
                if (imuData != null)
                {
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
                                await _hubContext.Clients.All.SendAsync("ImuUpdate", new
                                {
                                    timestamp = imuData.Timestamp,
                                    acceleration = new { x = imuData.Acceleration.X, y = imuData.Acceleration.Y, z = imuData.Acceleration.Z },
                                    gyroscope = new { x = imuData.Gyroscope.X, y = imuData.Gyroscope.Y, z = imuData.Gyroscope.Z },
                                    magnetometer = new { x = imuData.Magnetometer.X, y = imuData.Magnetometer.Y, z = imuData.Magnetometer.Z }
                                });
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to send IMU update via SignalR");
                            }
                        });
                    }
                }
            }
            else
            {
                // Not enough data for complete packet yet
                break;
            }
        }

        // Prevent buffer from growing too large
        if (_dataBuffer.Count > 1024)
        {
            _logger.LogWarning("IMU data buffer too large, clearing buffer");
            _dataBuffer.Clear();
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
        return -1;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping IMU Service");
        await base.StopAsync(cancellationToken);
    }
}