# GNSS Data Collection System

A high-performance data collection system designed for Raspberry Pi 5 that collects GNSS data from UBlox receivers, processes corrections, captures IMU data, and records video streams for surveying and mapping applications.

## System Overview

This application consists of two main components:
- **Backend**: .NET Core service handling real-time data collection, processing, and storage
- **Frontend**: Vue.js web interface for system monitoring and administration

### Key Features

- **Real-time GNSS Processing**: Receives raw GNSS data from UBlox chip via serial
- **RTCM3 Corrections**: Generates and transmits RTCM3 correction messages
- **Multi-sensor Data Collection**: Simultaneous GNSS, IMU, and video capture
- **Automated Storage**: Daily organized file storage with rotation management
- **Web Interface**: Real-time monitoring and system administration
- **Offline Operation**: Fully functional without internet connectivity

## Hardware Requirements

### Raspberry Pi 5 Setup
- Raspberry Pi 5 (4GB+ recommended)
- MicroSD card (32GB+ Class 10)
- USB 3.0 flash drive for data storage
- Power supply (27W USB-C recommended)

### Connected Hardware
- UBlox GNSS receiver (serial interface)
- IMU sensor (serial interface) 
- USB camera for video capture
- Serial-to-USB adapters (if needed)

### Port Configuration
- **UBlox Input/Output**: `/dev/ttyAMA0` - GNSS data input and RTCM3 correction injection (bidirectional)
- **LoRa Radio**: (TBD) - RTCM3 correction transmission (Base mode) or reception (Rover mode)
- **IMU Input**: `/dev/ttyAMA2` - IMU data input
- **USB Ports**: Camera and storage drive

## Software Architecture

```
┌─────────────────┐    ┌──────────────────┐    ┌─────────────────┐
│   UBlox GNSS    │◄──▶│                  │◄──▶│   LoRa Radio    │
│  (ttyAMA0)      │    │                  │    │ Base/Rover Mode │
└─────────────────┘    │                  │    └─────────────────┘
                       │                  │
┌─────────────────┐    │   .NET Backend   │    ┌─────────────────┐
│   IMU Sensor    │───▶│     Service      │───▶│  File Storage   │
│  (ttyAMA2)      │    │                  │    │  (USB Drive)    │
└─────────────────┘    │                  │    └─────────────────┘
                       │                  │
┌─────────────────┐    │                  │    ┌─────────────────┐
│   USB Camera    │───▶│                  │───▶│  Vue.js Admin   │
│   (Video In)    │    │                  │    │     Panel       │
└─────────────────┘    └──────────────────┘    └─────────────────┘
```

## Data Flow

### Base Station Mode
1. **GNSS Data Processing**:
   - Raw GNSS data received from UBlox via `/dev/ttyAMA0`
   - Data parsed and processed in real-time
   - RTCM3 corrections generated and transmitted via LoRa radio
   - Raw observations saved to daily folders

### Rover Mode
1. **GNSS Data Processing**:
   - Raw GNSS data received from UBlox via `/dev/ttyAMA0`
   - RTCM3 corrections received from LoRa radio
   - Corrections injected to UBlox chip via `/dev/ttyAMA0` for RTK processing
   - Enhanced position data saved to daily folders

### Common Operations (Both Modes)
2. **IMU Data Collection**:
   - Continuous IMU data capture via `/dev/ttyAMA2`
   - Data timestamped and stored with GNSS data

3. **Video Recording**:
   - USB camera stream captured and saved
   - Video files organized by date/time

4. **Storage Management**:
   - Files organized in daily folders (YYYY-MM-DD)
   - Automatic rotation when storage reaches capacity
   - Raw data preserved for post-processing

## File Structure

```
/data/
├── 2024-01-15/
│   ├── gnss/
│   │   ├── raw_obs_001.ubx
│   │   ├── raw_obs_002.ubx
│   │   └── corrections_001.rtcm3
│   ├── imu/
│   │   ├── imu_data_001.csv
│   │   └── imu_data_002.csv
│   └── video/
│       ├── recording_001.mp4
│       └── recording_002.mp4
└── 2024-01-16/
    └── ...
```

## Monitoring

The admin panel provides real-time monitoring of:

- **GNSS Status**: Satellite count, fix quality, position accuracy, RTK status
- **LoRa Radio**: Operating mode (Base/Rover), signal strength, correction data rates
- **IMU Status**: Acceleration, gyroscope, magnetometer readings
- **System Health**: CPU usage, memory, storage space
- **Data Rates**: Incoming data rates, correction message frequency
- **File Management**: Storage usage, current recording files

## Performance Considerations

- **Real-time Processing**: System optimized for low-latency correction relay
- **Storage I/O**: Buffered writing to minimize SD card wear
- **Memory Usage**: Efficient data structures for continuous operation
- **CPU Usage**: Optimized parsing and processing algorithms

## License

[License information to be added]

## Support

For issues and support:
- Check troubleshooting section above
- Review log files for error details
- Ensure all hardware connections are secure
- Verify serial port permissions and configurations