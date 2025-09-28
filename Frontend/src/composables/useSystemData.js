import { reactive, ref, computed, watch } from 'vue';

// Global reactive state for system and IMU data
const systemHealth = reactive({
  cpuUsage: null,
  memoryUsage: null,
  temperature: null,
  batteryLevel: null,
  batteryVoltage: null,
  isExternalPowerConnected: false,
  hostname: null
});

const powerStatus = reactive({
  batteryLevel: null,
  batteryVoltage: null,
  isExternalPowerConnected: false,
  powerConsumption: null,
  estimatedRuntime: null
});

const imuData = reactive({
  acceleration: { x: null, y: null, z: null },
  gyroscope: { x: null, y: null, z: null },
  magnetometer: { x: null, y: null, z: null }
});

const dataRates = reactive({
  gnssRate: null,
  imuRate: null,
  correctionRate: null,
  kbpsGnssIn: null,
  kbpsGnssOut: null,
  kbpsLoRaIn: null,
  kbpsLoRaOut: null,
  kbpsImu: null
});

const cameraData = reactive({
  timestamp: null,
  imageBase64: '',
  imageSizeBytes: 0,
  imageWidth: 0,
  imageHeight: 0,
  format: 'JPEG',
  captureTimeMs: 0,
  encodingTimeMs: 0,
  isConnected: false
});

const fileLoggingStatus = reactive({
  driveAvailable: false,
  drivePath: null,
  currentSession: null,
  totalSpaceBytes: null,
  availableSpaceBytes: null,
  usedSpaceBytes: null,
  activeFiles: []
});

const encoderData = reactive({
  rawCount: null,
  direction: null,
  pulsesPerSecond: null
});

// Hostname management
const editedHostname = ref('');
const originalHostname = ref('');
const isUpdatingHostname = ref(false);
const hostnameError = ref('');
const hostnameSuccess = ref('');
const isHostnameModified = ref(false);
const hostnameValidationError = ref('');
const isHostnameValid = ref(true);

// Battery tracking for discharge rate calculation
const batteryHistory = ref([]);
const dischargeRate = ref(null);

// Color utility functions
const getBatteryColor = (level) => 
{
  if (level > 60) return 'text-green-500';
  if (level > 30) return 'text-yellow-500';
  return 'text-red-500';
};

const getUsageColor = (usage) => 
{
  if (usage < 50) return 'text-green-500';
  if (usage < 80) return 'text-yellow-500';
  return 'text-red-500';
};

// Hostname validation function
const validateHostname = (hostname) => 
{
  if (!hostname || typeof hostname !== 'string') 
  {
    return 'Hostname is required';
  }

  const trimmedHostname = hostname.trim();

  // Check length (2-63 characters for practical use)
  if (trimmedHostname.length === 0) 
  {
    return 'Hostname cannot be empty';
  }
  if (trimmedHostname.length === 1) 
  {
    return 'Hostname must be at least 2 characters long';
  }
  if (trimmedHostname.length > 63) 
  {
    return 'Hostname must be 63 characters or less';
  }

  // Check format: only letters, digits, and hyphens
  const validCharsRegex = /^[a-zA-Z0-9-]+$/;
  if (!validCharsRegex.test(trimmedHostname)) 
  {
    return 'Hostname can only contain letters, digits, and hyphens';
  }

  // Check start and end: cannot start or end with hyphen
  if (trimmedHostname.startsWith('-')) 
  {
    return 'Hostname cannot start with a hyphen';
  }
  if (trimmedHostname.endsWith('-')) 
  {
    return 'Hostname cannot end with a hyphen';
  }

  // Check for consecutive hyphens (optional - some systems allow this)
  if (trimmedHostname.includes('--')) 
  {
    return 'Hostname cannot contain consecutive hyphens';
  }

  return null; // Valid
};

// Initialize hostname watchers
let isWatchersInitialized = false;

const initializeWatchers = () => 
{
  if (isWatchersInitialized) return;
  isWatchersInitialized = true;

  // Watch for hostname changes from backend
  watch(() => systemHealth.hostname, (newHostname) => 
  {
    if (newHostname && !isHostnameModified.value) 
    {
      originalHostname.value = newHostname;
      editedHostname.value = newHostname;
    }
    else if (!newHostname && !editedHostname.value) 
    {
      // Initialize with empty string if no hostname available yet
      originalHostname.value = '';
      editedHostname.value = '';
    }
  }, { immediate: true });

  // Watch for user edits to hostname input
  watch(editedHostname, (newValue) => 
  {
    isHostnameModified.value = newValue !== originalHostname.value && newValue !== null && newValue !== undefined;

    // Validate hostname in real-time
    if (newValue) 
    {
      const validationError = validateHostname(newValue);
      hostnameValidationError.value = validationError || '';
      isHostnameValid.value = !validationError;
    }
    else 
    {
      hostnameValidationError.value = '';
      isHostnameValid.value = true;
    }
  });

  // Battery discharge rate tracking
  watch(() => powerStatus.batteryLevel, (newLevel) => 
  {
    if (newLevel !== null) 
    {
      const now = Date.now();
      batteryHistory.value.push({ level: newLevel, timestamp: now });

      // Keep only last 2 minutes
      const twoMinutesAgo = now - (1 * 60 * 1000);
      batteryHistory.value = batteryHistory.value.filter(entry => entry.timestamp > twoMinutesAgo);

      // Calculate rate as soon as we have 2 data points
      if (batteryHistory.value.length >= 2) 
      {
        const oldest = batteryHistory.value[0];
        const newest = batteryHistory.value[batteryHistory.value.length - 1];
        const levelDiff = oldest.level - newest.level;
        const timeDiff = (newest.timestamp - oldest.timestamp) / (60 * 1000); // minutes

        if (timeDiff > 0) 
        {
          dischargeRate.value = levelDiff / timeDiff; // %/minute
        }
      }
    }
  });
};

// Hostname management functions
const saveHostname = async (connection) => 
{
  if (!connection || !editedHostname.value || !editedHostname.value.trim()) return;

  // Check validation before saving
  if (!isHostnameValid.value) 
  {
    hostnameError.value = hostnameValidationError.value || 'Invalid hostname format';
    return;
  }

  isUpdatingHostname.value = true;
  hostnameError.value = '';
  hostnameSuccess.value = '';

  try 
  {
    const result = await connection.invoke('UpdateHostname', editedHostname.value.trim());

    if (result.success) 
    {
      hostnameSuccess.value = result.message;
      originalHostname.value = editedHostname.value;
      isHostnameModified.value = false;
    }
    else 
    {
      hostnameError.value = result.message;
    }
  }
  catch (error) 
  {
    console.error('Failed to update hostname:', error);
    hostnameError.value = 'Failed to update hostname: ' + error.message;
  }
  finally 
  {
    isUpdatingHostname.value = false;

    // Clear messages after 5 seconds
    setTimeout(() => 
    {
      hostnameError.value = '';
      hostnameSuccess.value = '';
    }, 5000);
  }
};

const cancelHostnameEdit = () => 
{
  editedHostname.value = originalHostname.value;
  isHostnameModified.value = false;
  hostnameError.value = '';
  hostnameValidationError.value = '';
  isHostnameValid.value = true;
};

// SignalR event handlers for system data
export function registerSystemEvents(connection) 
{
  connection.on("ImuUpdate", (data) => 
  {
    imuData.acceleration.x = data.acceleration.x;
    imuData.acceleration.y = data.acceleration.y;
    imuData.acceleration.z = data.acceleration.z;
    imuData.gyroscope.x = data.gyroscope.x;
    imuData.gyroscope.y = data.gyroscope.y;
    imuData.gyroscope.z = data.gyroscope.z;
    imuData.magnetometer.x = data.magnetometer.x;
    imuData.magnetometer.y = data.magnetometer.y;
    imuData.magnetometer.z = data.magnetometer.z;
    dataRates.kbpsImu = data.kbps;
  });

  connection.on("SystemHealthUpdate", (data) => 
  {
    systemHealth.cpuUsage = data.cpuUsage;
    systemHealth.memoryUsage = data.memoryUsage;
    systemHealth.temperature = data.temperature;
    systemHealth.batteryLevel = data.batteryLevel;
    systemHealth.batteryVoltage = data.batteryVoltage;
    systemHealth.isExternalPowerConnected = data.isExternalPowerConnected;
    systemHealth.hostname = data.hostname;

    // Update powerStatus with battery data for the SystemPanel
    powerStatus.batteryLevel = data.batteryLevel;
    powerStatus.batteryVoltage = data.batteryVoltage;
    powerStatus.isExternalPowerConnected = data.isExternalPowerConnected;
  });

  connection.on("DataRatesUpdate", (data) => 
  {
    dataRates.kbpsGnssIn = data.kbpsGnssIn;
    dataRates.kbpsGnssOut = data.kbpsGnssOut;
    dataRates.kbpsLoRaIn = data.kbpsLoRaIn;
    dataRates.kbpsLoRaOut = data.kbpsLoRaOut;
  });

  connection.on("FileLoggingStatusUpdate", (data) => 
  {
    fileLoggingStatus.driveAvailable = data.driveAvailable;
    fileLoggingStatus.drivePath = data.drivePath;
    fileLoggingStatus.currentSession = data.currentSession;
    fileLoggingStatus.totalSpaceBytes = data.totalSpaceBytes;
    fileLoggingStatus.availableSpaceBytes = data.availableSpaceBytes;
    fileLoggingStatus.usedSpaceBytes = data.usedSpaceBytes;
    fileLoggingStatus.activeFiles = data.activeFiles || [];
  });

  connection.on("CameraUpdate", (data) => 
  {
    cameraData.timestamp = data.timestamp;
    cameraData.imageBase64 = data.imageBase64;
    cameraData.imageSizeBytes = data.imageSizeBytes;
    cameraData.imageWidth = data.imageWidth;
    cameraData.imageHeight = data.imageHeight;
    cameraData.format = data.format;
    cameraData.captureTimeMs = data.captureTimeMs;
    cameraData.encodingTimeMs = data.encodingTimeMs;
    cameraData.isConnected = data.isConnected;

    // Log frame size received
    if (data.isConnected && data.imageSizeBytes > 0) 
    {
      const sizeKb = (data.imageSizeBytes / 1024).toFixed(1);
      console.log(`Camera frame received: ${data.imageWidth}x${data.imageHeight}, ${sizeKb} KB (${data.format})`);
    }
    else if (!data.isConnected) 
    {
      console.log('Camera disconnected');
    }
  });

  connection.on("HostnameUpdated", (data) => 
  {
    console.log(`Hostname updated to: ${data.hostname}`);
    systemHealth.hostname = data.hostname;

    // Optional: Show a toast or notification
    console.log(`Hostname update message: ${data.message}`);
  });
}

// Create a single reactive state object
const state = reactive({
  systemHealth,
  powerStatus,
  imuData,
  dataRates,
  cameraData,
  fileLoggingStatus,
  encoderData,
  editedHostname,
  originalHostname,
  isUpdatingHostname,
  hostnameError,
  hostnameSuccess,
  isHostnameModified,
  hostnameValidationError,
  isHostnameValid,
  batteryHistory,
  dischargeRate
});

export function useSystemData() 
{
  // Initialize watchers the first time this composable is used
  initializeWatchers();

  return {
    // Single state object
    state,
    // Individual methods and utilities
    getBatteryColor,
    getUsageColor,
    validateHostname,
    saveHostname,
    cancelHostnameEdit,
    registerSystemEvents
  };
}