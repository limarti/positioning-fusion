using Backend.Hardware.Gnss.Models;

namespace Backend.Hardware.Gnss;

/// <summary>
/// Centralized static store for all parsed GNSS message data.
/// Each parser updates its corresponding property after parsing a message.
/// Provides a single source of truth for the latest state of all GNSS data.
/// </summary>
public static class GnssDataStore
{
    private static readonly object _lock = new object();

    // Correction-related messages
    public static RxmCorData? LastRxmCor { get; private set; }
    public static NavSatData? LastNavSat { get; private set; }
    public static NavPvtData? LastNavPvt { get; private set; }

    // Update methods with automatic timestamping
    public static void UpdateRxmCor(RxmCorData data)
    {
        lock (_lock)
        {
            data.ReceivedAt = DateTime.UtcNow;
            LastRxmCor = data;
        }
    }

    public static void UpdateNavSat(NavSatData data)
    {
        lock (_lock)
        {
            data.ReceivedAt = DateTime.UtcNow;
            LastNavSat = data;
        }
    }

    public static void UpdateNavPvt(NavPvtData data)
    {
        lock (_lock)
        {
            data.ReceivedAt = DateTime.UtcNow;
            LastNavPvt = data;
        }
    }

    // Thread-safe read methods
    public static (RxmCorData? rxmCor, NavSatData? navSat, NavPvtData? navPvt) GetCorrectionSources()
    {
        lock (_lock)
        {
            return (LastRxmCor, LastNavSat, LastNavPvt);
        }
    }

    // Clear all data (useful for testing or reset)
    public static void Clear()
    {
        lock (_lock)
        {
            LastRxmCor = null;
            LastNavSat = null;
            LastNavPvt = null;
        }
    }
}
