using System.IO.Ports;

namespace Backend.Services;

public class ImuInitializer
{
    private readonly ILogger<ImuInitializer> _logger;
    private SerialPort? _serialPort;
    
    private const string DefaultPortName = "/dev/ttyAMA2";
    private const int DefaultBaudRate = 115200;
    private const Parity DefaultParity = Parity.None;
    private const int DefaultDataBits = 8;
    private const StopBits DefaultStopBits = StopBits.One;
    private const int InitializationTimeoutMs = 5000;

    public ImuInitializer(ILogger<ImuInitializer> logger)
    {
        _logger = logger;
    }

    public async Task<bool> InitializeAsync(string portName = DefaultPortName, int baudRate = DefaultBaudRate)
    {
        try
        {
            _logger.LogInformation("Initializing IM19 IMU on port {PortName} at {BaudRate} baud", portName, baudRate);

            _serialPort = new SerialPort(portName, baudRate, DefaultParity, DefaultDataBits, DefaultStopBits)
            {
                ReadTimeout = 1000,
                WriteTimeout = 1000,
                RtsEnable = true,
                DtrEnable = true
            };

            _serialPort.Open();
            _logger.LogInformation("Serial port {PortName} opened successfully", portName);

            await Task.Delay(500);

            await ConfigureImuOutputAsync();
            if (await VerifyImuCommunicationAsync())
            {
                _logger.LogInformation("IM19 IMU initialized successfully and ready to receive data");
                return true;
            }
            else
            {
                _logger.LogError("Failed to verify IMU communication after configuration");
                _serialPort?.Close();
                _serialPort?.Dispose();
                _serialPort = null;
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize IM19 IMU on port {PortName}", portName);
            _serialPort?.Close();
            _serialPort?.Dispose();
            _serialPort = null;
            return false;
        }
    }

    private async Task<bool> VerifyImuCommunicationAsync()
    {
        if (_serialPort == null || !_serialPort.IsOpen)
            return false;

        _logger.LogDebug("Verifying IM19 IMU communication...");
        
        _serialPort.DiscardInBuffer();
        _serialPort.DiscardOutBuffer();
        var timeoutTask = Task.Delay(InitializationTimeoutMs);
        var dataReceiveTask = Task.Run(async () =>
        {
            while (_serialPort.IsOpen)
            {
                if (_serialPort.BytesToRead > 0)
                {
                    return true;
                }
                await Task.Delay(100);
            }
            return false;
        });

        var result = await Task.WhenAny(timeoutTask, dataReceiveTask);
        
        if (result == dataReceiveTask && await dataReceiveTask)
        {
            _logger.LogDebug("IM19 IMU communication verified - receiving data");
            return true;
        }
        else
        {
            _logger.LogWarning("IM19 IMU communication verification timed out - no data received");
            return false;
        }
    }

    private async Task ConfigureImuOutputAsync()
    {
        if (_serialPort == null || !_serialPort.IsOpen)
            return;

        _logger.LogDebug("Configuring IM19 IMU for continuous data output...");

        const string atCommand = "AT+MEMS_OUTPUT=UART1,ON";
        _logger.LogDebug("Sending AT command: {Command}", atCommand);
        
        _serialPort.DiscardInBuffer();
        _serialPort.WriteLine(atCommand);
        var response = await WaitForAtResponseAsync();
        
        if (response.Contains("OK"))
        {
            _logger.LogInformation("IM19 IMU MEMS output enabled successfully");
        }
        else if (response.Contains("ERROR"))
        {
            _logger.LogError("IM19 IMU AT command failed with response: {Response}", response);
            throw new InvalidOperationException($"AT command failed: {response}");
        }
        else
        {
            _logger.LogWarning("IM19 IMU AT command response unclear: {Response}", response);
        }
        
        _logger.LogDebug("IM19 IMU configuration completed");
    }

    private async Task<string> WaitForAtResponseAsync(int timeoutMs = 3000)
    {
        var response = string.Empty;
        var endTime = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        
        while (DateTime.UtcNow < endTime)
        {
            if (_serialPort?.BytesToRead > 0)
            {
                var data = _serialPort.ReadExisting();
                response += data;
                
                if (response.Contains("OK") || response.Contains("ERROR"))
                {
                    _logger.LogDebug("AT command response received: {Response}", response.Trim());
                    return response.Trim();
                }
            }
            
            await Task.Delay(50);
        }
        
        _logger.LogWarning("AT command response timeout after {TimeoutMs}ms", timeoutMs);
        return response.Trim();
    }

    public SerialPort? GetSerialPort()
    {
        return _serialPort;
    }

    public void Dispose()
    {
        try
        {
            _serialPort?.Close();
            _serialPort?.Dispose();
            _serialPort = null;
            _logger.LogInformation("IM19 IMU serial connection disposed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing IM19 IMU serial connection");
        }
    }
}