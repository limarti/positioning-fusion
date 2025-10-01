using Tmds.DBus;

namespace Backend.Hardware.Bluetooth;

/// <summary>
/// Manages SPP (Serial Port Profile) registration with BlueZ via D-Bus.
/// Provides proper Bluetooth Classic serial port service advertisement.
/// </summary>
public class SppProfileManager : IDisposable
{
    private readonly ILogger<SppProfileManager> _logger;
    private IConnection? _connection;
    private SppProfile? _profile;
    private bool _disposed = false;

    private const string BLUEZ_SERVICE = "org.bluez";
    private const string PROFILE_MANAGER_PATH = "/org/bluez";
    private const string PROFILE_PATH = "/org/bluez/spp_profile";

    // SPP UUID (Serial Port Profile)
    private const string SPP_UUID = "00001101-0000-1000-8000-00805f9b34fb";
    private const ushort SPP_CHANNEL = 1;

    public bool IsRegistered { get; private set; } = false;

    public SppProfileManager(ILogger<SppProfileManager> logger)
    {
        _logger = logger;
    }

    public async Task<bool> RegisterProfileAsync()
    {
        try
        {
            _logger.LogInformation("Registering SPP profile via D-Bus...");

            // Connect to system D-Bus
            _connection = new Connection(Address.System!);
            await _connection.ConnectAsync();
            _logger.LogDebug("Connected to D-Bus system bus");

            // Create SPP profile object
            _profile = new SppProfile(_logger);
            await _connection.RegisterObjectAsync(_profile);
            _logger.LogDebug("Registered SPP profile object at {Path}", PROFILE_PATH);

            // Get ProfileManager interface
            var profileManager = _connection.CreateProxy<IProfileManager>(BLUEZ_SERVICE, PROFILE_MANAGER_PATH);

            // Profile options
            var options = new Dictionary<string, object>
            {
                { "Name", "Serial Port" },
                { "Role", "server" },
                { "Channel", SPP_CHANNEL },
                { "AutoConnect", true }
            };

            // Register profile with BlueZ
            await profileManager.RegisterProfileAsync(
                new ObjectPath(PROFILE_PATH),
                SPP_UUID,
                options
            );

            _logger.LogInformation("✅ SPP Profile registered successfully - UUID: {UUID}, Channel: {Channel}",
                SPP_UUID, SPP_CHANNEL);

            IsRegistered = true;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register SPP profile via D-Bus");
            IsRegistered = false;
            return false;
        }
    }

    public async Task UnregisterProfileAsync()
    {
        if (!IsRegistered || _connection == null)
            return;

        try
        {
            _logger.LogInformation("Unregistering SPP profile...");

            var profileManager = _connection.CreateProxy<IProfileManager>(BLUEZ_SERVICE, PROFILE_MANAGER_PATH);
            await profileManager.UnregisterProfileAsync(new ObjectPath(PROFILE_PATH));

            _logger.LogInformation("SPP profile unregistered");
            IsRegistered = false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error unregistering SPP profile");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (IsRegistered)
        {
            // Synchronous unregister on dispose
            try
            {
                UnregisterProfileAsync().Wait(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during SPP profile cleanup");
            }
        }

        _connection?.Dispose();
        _logger.LogInformation("SppProfileManager disposed");
    }

    /// <summary>
    /// SPP Profile D-Bus object implementation
    /// </summary>
    [DBusInterface("org.bluez.Profile1")]
    private class SppProfile : IDBusObject
    {
        private readonly ILogger _logger;

        public ObjectPath ObjectPath => new ObjectPath(PROFILE_PATH);

        public SppProfile(ILogger logger)
        {
            _logger = logger;
        }

        public Task ReleaseAsync()
        {
            _logger.LogInformation("SPP Profile Release called");
            return Task.CompletedTask;
        }

        public Task NewConnectionAsync(ObjectPath device, object fd, IDictionary<string, object> properties)
        {
            _logger.LogInformation("📱 New SPP connection from device: {Device}", device);
            _logger.LogDebug("Connection properties: {Properties}", string.Join(", ", properties.Select(p => $"{p.Key}={p.Value}")));
            // rfcomm listen will handle the actual connection on the serial port side
            return Task.CompletedTask;
        }

        public Task RequestDisconnectionAsync(ObjectPath device)
        {
            _logger.LogInformation("📱 Disconnection requested for device: {Device}", device);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// BlueZ ProfileManager1 D-Bus interface
    /// </summary>
    [DBusInterface("org.bluez.ProfileManager1")]
    public interface IProfileManager : IDBusObject
    {
        Task RegisterProfileAsync(ObjectPath profile, string uuid, IDictionary<string, object> options);
        Task UnregisterProfileAsync(ObjectPath profile);
    }
}
