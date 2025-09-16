using Microsoft.AspNetCore.SignalR;
using Backend.Hardware.Imu;
using Backend.System;

namespace Backend.Hubs;

public class DataHub : Hub
{
    public async Task SendPositionUpdate(double latitude, double longitude)
    {
        await Clients.All.SendAsync("PositionUpdate", new { latitude, longitude });
    }

    public async Task SendImuUpdate(ImuData imuData)
    {
        await Clients.All.SendAsync("ImuUpdate", new
        {
            timestamp = imuData.Timestamp,
            acceleration = new { x = imuData.Acceleration.X, y = imuData.Acceleration.Y, z = imuData.Acceleration.Z },
            gyroscope = new { x = imuData.Gyroscope.X, y = imuData.Gyroscope.Y, z = imuData.Gyroscope.Z },
            magnetometer = new { x = imuData.Magnetometer.X, y = imuData.Magnetometer.Y, z = imuData.Magnetometer.Z }
        });
    }

    public async Task SendSystemHealthUpdate(SystemHealth systemHealth)
    {
        await Clients.All.SendAsync("SystemHealthUpdate", new
        {
            cpuUsage = systemHealth.CpuUsage,
            memoryUsage = systemHealth.MemoryUsage,
            temperature = systemHealth.Temperature
        });
    }
}