# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a GNSS Data Collection System for Raspberry Pi 5 that collects real-time GNSS data from UBlox receivers, processes corrections, captures IMU data, and records video streams for surveying and mapping applications. The system operates in both Base Station and Rover modes for RTK processing.

## IMPORTANT: Claude Code Usage Restrictions

**NEVER RUN THE APPLICATION** - This system is designed to run on a Raspberry Pi 5 with specific hardware (UBlox GNSS, IMU sensors, cameras). Claude Code runs on a development machine without this hardware and will fail or behave unpredictably.

**Only provide build/run instructions to the user when they need to deploy or test on the target hardware.**

## Development Commands

### Building Only (Safe for Claude Code)
```bash
# Build the backend to check for compilation errors
cd Backend
dotnet build
```

### Running on Raspberry Pi (User Instructions Only)
```bash
# These commands should ONLY be run by the user on the Raspberry Pi:

# Run in development mode  
dotnet run --project Backend

# Run with specific profile
dotnet run --project Backend --launch-profile http
```

### Target Hardware Requirements
- Backend runs on `http://0.0.0.0:5312` in development
- SignalR hub available at `/datahub`
- Requires actual hardware: UBlox ZED-F9P (ZED-XP) GNSS receiver, IM19 IMU, USB camera, LoRa radio
- Serial ports `/dev/ttyAMA0` and `/dev/ttyAMA2` must be available

## Architecture Overview

### Core Components

**Backend (.NET 8 Web API + SignalR)**
- `Program.cs` - Application entry point, service registration, hardware initialization
- `Hubs/DataHub.cs` - SignalR hub for real-time data broadcasting to frontend

**Hardware Services (Background Services)**
- `Hardware/Position/PositionService.cs` - Position data service (currently simulated, 5Hz updates)
- `Hardware/Imu/ImuService.cs` - IMU data collection via serial (`/dev/ttyAMA2`, 50Hz collection, 1Hz SignalR)
- `System/SystemMonitoringService.cs` - System health monitoring (CPU, memory, temperature, 1Hz)

**Hardware Initializers**
- `Hardware/Imu/ImuInitializer.cs` - IM19 IMU hardware initialization
- `Hardware/Gnss/GnssInitializer.cs` - GNSS hardware initialization  

**Data Processing**
- `Hardware/Imu/ImuParser.cs` - Parses IMU MEMS packets (52-byte packets with "fmi" header)
- `Hardware/Gnss/UbxCommunication.cs` - UBlox communication handling

### Hardware Configuration
- **UBlox ZED-F9P (ZED-XP) GNSS**: `/dev/ttyAMA0` (bidirectional - data input and RTCM3 correction injection)
- **IMU Sensor**: `/dev/ttyAMA2` (IM19 IMU data input)
- **USB Camera**: Video capture for recording
- **LoRa Radio**: RTCM3 correction transmission (Base mode) or reception (Rover mode)

### Data Flow
1. **Real-time Data Collection**: GNSS, IMU, and video data captured simultaneously
2. **Signal Processing**: Data parsed and processed in real-time background services
3. **SignalR Broadcasting**: Live data streamed to frontend via SignalR at controlled rates
4. **File Storage**: Organized daily folder structure on USB drive (`/data/YYYY-MM-DD/`)

### Key Dependencies
- `Microsoft.AspNetCore.OpenApi` - API documentation
- `Swashbuckle.AspNetCore` - Swagger integration  
- `System.IO.Ports` - Serial port communication for hardware interfaces

## Development Notes

### Service Architecture
- All hardware services inherit from `BackgroundService` and run continuously
- Services use dependency injection for SignalR hub context and logging
- Hardware initialization occurs at startup in `Program.cs`
- Failed hardware initialization logs warnings but allows application to continue

### Serial Communication
- IMU service processes 52-byte MEMS packets with "fmi" header at 50Hz
- Data buffering and packet synchronization implemented for reliable parsing
- SignalR updates throttled to prevent overwhelming frontend

### System Monitoring
- CPU usage calculated from `/proc/stat` deltas
- Memory usage from `/proc/meminfo` (MemTotal - MemAvailable)
- Temperature from `/sys/class/thermal/thermal_zone0/temp`

### CORS Configuration
- Configured to allow any origin with credentials for frontend communication
- SignalR hub enables real-time bidirectional communication
- Never use fallbacks for values that are supposed to be populated. show errors, throw expections, etc