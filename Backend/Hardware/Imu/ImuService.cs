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
    private byte[] _dataBuffer = new byte[8192]; // Fixed-size buffer with offset tracking
    private int _bufferOffset = 0; // Read position in buffer
    private int _bufferLength = 0; // Amount of valid data in buffer
    private readonly object _dataBufferLock = new();
    private DateTime _lastSignalRSent = DateTime.MinValue;
    private readonly TimeSpan _signalRThrottleInterval = TimeSpan.FromMilliseconds(1000); // 1Hz = 1000ms interval
    private readonly object _throttleLock = new object();
    private bool _headerWritten = false;

    // Reusable buffers to reduce allocations
    private readonly byte[] _packetBuffer = new byte[52];
    private readonly System.Text.StringBuilder _csvBuilder = new System.Text.StringBuilder(200);

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

        // Add new data to buffer using efficient Array.Copy
        lock (_dataBufferLock)
        {
            // Compact buffer if offset is too large (> 50% of buffer) OR if we need space
            int availableSpace = _dataBuffer.Length - (_bufferOffset + _bufferLength);
            if (availableSpace < length && _bufferOffset > 0)
            {
                // Try compacting first before resizing
                if (_bufferLength > 0)
                {
                    Array.Copy(_dataBuffer, _bufferOffset, _dataBuffer, 0, _bufferLength);
                }
                _bufferOffset = 0;
                availableSpace = _dataBuffer.Length - _bufferLength;
            }

            // Resize buffer if still not enough space after compaction
            if (availableSpace < length)
            {
                int newSize = Math.Max(_dataBuffer.Length * 2, _bufferLength + length);
                var newBuffer = new byte[newSize];
                if (_bufferLength > 0)
                {
                    Array.Copy(_dataBuffer, _bufferOffset, newBuffer, 0, _bufferLength);
                }
                _dataBuffer = newBuffer;
                _bufferOffset = 0;
            }

            // Copy new data to buffer (fast!)
            Array.Copy(newData, 0, _dataBuffer, _bufferOffset + _bufferLength, length);
            _bufferLength += length;
        }

        // Look for complete packets (limit iterations to prevent CPU spinning)
        const int maxIterations = 10;
        int iterations = 0;

        while (iterations < maxIterations)
        {
            iterations++;

            byte[]? packetData = null;

            lock (_dataBufferLock)
            {
                if (_bufferLength < 52) // MEMS packet size
                {
                    break;
                }

                int packetStart = FindPacketStart();

                if (packetStart == -1)
                {
                    // No valid packet header found
                    // Only scan first 100 bytes for 'f' to avoid CPU hogging
                    int searchLimit = Math.Min(_bufferLength, 100);
                    int nextF = -1;
                    for (int i = 1; i < searchLimit; i++)
                    {
                        if (_dataBuffer[_bufferOffset + i] == (byte)'f')
                        {
                            nextF = i;
                            break;
                        }
                    }

                    if (nextF > 0)
                    {
                        // Found 'f' somewhere ahead in first 100 bytes - jump to it
                        _bufferOffset += nextF;
                        _bufferLength -= nextF;
                        continue; // Try parsing from this position
                    }
                    else if (nextF == -1 && searchLimit >= 100)
                    {
                        // No 'f' found in first 100 bytes - discard 90 bytes and keep searching
                        int discardBytes = 90;
                        _bufferOffset += discardBytes;
                        _bufferLength -= discardBytes;
                        continue;
                    }
                    else
                    {
                        // Buffer too small or 'f' at start but invalid header - skip 1 byte and exit
                        _bufferOffset++;
                        _bufferLength--;
                        break; // Exit loop, wait for more data
                    }
                }

                // Skip bytes before packet start
                if (packetStart > 0)
                {
                    _bufferOffset += packetStart;
                    _bufferLength -= packetStart;
                }

                // Check if we have a complete packet
                if (_bufferLength >= 52)
                {
                    // Extract packet using reusable buffer to avoid allocation
                    Array.Copy(_dataBuffer, _bufferOffset, _packetBuffer, 0, 52);
                    packetData = _packetBuffer;
                    _bufferOffset += 52;
                    _bufferLength -= 52;
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

                    // Log data to file using StringBuilder to avoid allocations
                    _csvBuilder.Clear();
                    _csvBuilder.Append(imuData.SystemUptimeMs);
                    _csvBuilder.Append(',');
                    _csvBuilder.Append(imuData.Timestamp.ToString("F2"));
                    _csvBuilder.Append(',');
                    _csvBuilder.Append(imuData.Acceleration.X.ToString("F4"));
                    _csvBuilder.Append(',');
                    _csvBuilder.Append(imuData.Acceleration.Y.ToString("F4"));
                    _csvBuilder.Append(',');
                    _csvBuilder.Append(imuData.Acceleration.Z.ToString("F4"));
                    _csvBuilder.Append(',');
                    _csvBuilder.Append(imuData.Gyroscope.X.ToString("F4"));
                    _csvBuilder.Append(',');
                    _csvBuilder.Append(imuData.Gyroscope.Y.ToString("F4"));
                    _csvBuilder.Append(',');
                    _csvBuilder.Append(imuData.Gyroscope.Z.ToString("F4"));
                    _csvBuilder.Append(',');
                    _csvBuilder.Append(imuData.Magnetometer.X.ToString("F2"));
                    _csvBuilder.Append(',');
                    _csvBuilder.Append(imuData.Magnetometer.Y.ToString("F2"));
                    _csvBuilder.Append(',');
                    _csvBuilder.Append(imuData.Magnetometer.Z.ToString("F2"));

                    _dataFileWriter.WriteData(_csvBuilder.ToString());

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
            }
        }

        // Prevent buffer from growing too large
        lock (_dataBufferLock)
        {
            if (_bufferLength > 8192)
            {
                _logger.LogWarning("IMU data buffer too large ({Length} bytes), discarding oldest data", _bufferLength);
                // Keep only recent data
                int keepLength = Math.Min(_bufferLength, 2048);
                Array.Copy(_dataBuffer, _bufferOffset + (_bufferLength - keepLength), _dataBuffer, 0, keepLength);
                _bufferOffset = 0;
                _bufferLength = keepLength;
            }
        }
    }

    private int FindPacketStart()
    {
        // Look for "fmim" header (fmi + MEMS type 'm')
        // Must be called within _dataBufferLock
        if (_bufferLength < 4) return -1; // Need at least 4 bytes for header

        // Only search first 100 bytes to avoid CPU hogging with garbage data
        int searchLimit = Math.Min(_bufferLength - 4, 100);

        for (int i = 0; i <= searchLimit; i++)
        {
            int pos = _bufferOffset + i;

            // Bounds check for safety
            if (pos + 3 >= _dataBuffer.Length)
                break; // Can't read 4 bytes from this position

            if (_dataBuffer[pos] == (byte)'f' &&
                _dataBuffer[pos + 1] == (byte)'m' &&
                _dataBuffer[pos + 2] == (byte)'i' &&
                _dataBuffer[pos + 3] == (byte)'m') // MEMS type
            {
                return i;
            }
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