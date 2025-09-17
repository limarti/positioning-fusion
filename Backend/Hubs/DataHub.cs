using Microsoft.AspNetCore.SignalR;
using Backend.Hardware.Imu;
using Backend.Hardware.Position;
using Backend.System;

namespace Backend.Hubs;

public class DataHub : Hub
{
    public async Task SendPositionUpdate(double latitude, double longitude)
    {
        await Clients.All.SendAsync("PositionUpdate", new PositionUpdate { Latitude = latitude, Longitude = longitude });
    }

    public async Task SendImuUpdate(ImuData imuData)
    {
        await Clients.All.SendAsync("ImuUpdate", new ImuUpdate
        {
            Timestamp = imuData.Timestamp,
            Acceleration = new Vector3Update { X = imuData.Acceleration.X, Y = imuData.Acceleration.Y, Z = imuData.Acceleration.Z },
            Gyroscope = new Vector3Update { X = imuData.Gyroscope.X, Y = imuData.Gyroscope.Y, Z = imuData.Gyroscope.Z },
            Magnetometer = new Vector3Update { X = imuData.Magnetometer.X, Y = imuData.Magnetometer.Y, Z = imuData.Magnetometer.Z }
        });
    }

    public async Task SendSystemHealthUpdate(SystemHealth systemHealth)
    {
        await Clients.All.SendAsync("SystemHealthUpdate", new SystemHealthUpdate
        {
            CpuUsage = systemHealth.CpuUsage,
            MemoryUsage = systemHealth.MemoryUsage,
            Temperature = systemHealth.Temperature
        });
    }
}