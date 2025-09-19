<script setup>
import { ref, onMounted, onUnmounted } from 'vue'
import { HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr'
import GnssPanel from './components/GnssPanel.vue'
import ImuPanel from './components/ImuPanel.vue'
import CameraPanel from './components/CameraPanel.vue'
import EncoderPanel from './components/EncoderPanel.vue'
import SystemPanel from './components/SystemPanel.vue'
import FileLoggingPanel from './components/FileLoggingPanel.vue'
import ConnectionOverlay from './components/ConnectionOverlay.vue'

// SignalR connection
let connection = null

// Connection status tracking
const connectionStatus = ref('Disconnected')
const retryAttempt = ref(0)
const nextRetryIn = ref(0)
let retryTimer = null
let retryCountdown = null

// Comprehensive GNSS data model
const gnssData = ref({
  // Connection status
  connected: false,

  // Fix status
  fixType: null,
  rtkMode: null,

  // Position (will be updated by real GNSS data)
  latitude: null,
  longitude: null,
  altitude: null,
  
  // Accuracy estimates
  hAcc: null,
  vAcc: null,
  
  // Dilution of Precision
  hdop: null,
  vdop: null,
  pdop: null,
  tdop: null,
  
  // Satellite counts
  satellitesUsed: null,
  satellitesTracked: null,
  
  // Constellation breakdown
  constellations: {
    gps: { used: null, tracked: null },
    glonass: { used: null, tracked: null },
    galileo: { used: null, tracked: null },
    beidou: { used: null, tracked: null }
  },
  
  // Per-satellite data
  satellites: [],
  
  // RTK-specific metrics
  rtk: {
    active: false,
    arRatio: null,
    correctionAge: null,
    baselineLength: null,
    relativeAccuracy: { north: null, east: null, down: null }
  },

  // Survey-In status (for base station mode)
  surveyIn: {
    active: false,
    valid: false,
    duration: null,
    observations: null,
    accuracyMm: null,
    position: { x: null, y: null, z: null }
  },

  // Corrections mode and status
  corrections: {
    mode: 'Disabled', // 'Disabled', 'Receive', 'Send'
    active: false
  },
  
})

const loraData = ref({
  mode: null,
  signalStrength: null,
  correctionRate: null,
  packetsReceived: null,
  packetsSent: null
})

const imuData = ref({
  acceleration: { x: null, y: null, z: null },
  gyroscope: { x: null, y: null, z: null },
  magnetometer: { x: null, y: null, z: null }
})

const systemHealth = ref({
  cpuUsage: null,
  memoryUsage: null,
  temperature: null,
  batteryLevel: null,
  batteryVoltage: null,
  isExternalPowerConnected: false
})

const powerStatus = ref({
  batteryLevel: null,
  batteryVoltage: null,
  isExternalPowerConnected: false,
  powerConsumption: null,
  estimatedRuntime: null
})

const dataRates = ref({
  gnssRate: null,
  imuRate: null,
  correctionRate: null,
  kbpsGnssIn: null,
  kbpsGnssOut: null,
  kbpsLoRaIn: null,
  kbpsLoRaOut: null
})

const messageRates = ref({
  messageRates: {},
  timestamp: null
})

const encoderData = ref({
  rawCount: null,
  direction: null,
  pulsesPerSecond: null
})

const fileLoggingStatus = ref({
  driveAvailable: false,
  drivePath: null,
  currentSession: null,
  totalSpaceBytes: null,
  availableSpaceBytes: null,
  usedSpaceBytes: null,
  activeFiles: []
})

// Helper functions (moved to components where needed)
const getSignalColor = (strength) => {
  if (strength > -70) return 'text-green-500'
  if (strength > -85) return 'text-yellow-500'
  return 'text-red-500'
}

// Connection management functions
const updateConnectionStatus = () => {
  if (!connection) {
    connectionStatus.value = 'Disconnected'
    return
  }

  switch (connection.state) {
    case HubConnectionState.Connected:
      connectionStatus.value = 'Connected'
      retryAttempt.value = 0
      clearRetryTimer()
      break
    case HubConnectionState.Connecting:
      connectionStatus.value = 'Connecting'
      break
    case HubConnectionState.Disconnected:
      connectionStatus.value = 'Disconnected'
      // Only start our custom retry after SignalR has given up
      scheduleRetry()
      break
  }
}

const clearRetryTimer = () => {
  if (retryTimer) {
    clearTimeout(retryTimer)
    retryTimer = null
  }
  if (retryCountdown) {
    clearInterval(retryCountdown)
    retryCountdown = null
  }
  nextRetryIn.value = 0
}

const scheduleRetry = () => {
  if (retryTimer) return // Already scheduled

  retryAttempt.value++
  const delay = 5000 // Fixed 5 second retry interval
  nextRetryIn.value = Math.ceil(delay / 1000)

  // Countdown timer
  retryCountdown = setInterval(() => {
    nextRetryIn.value--
    if (nextRetryIn.value <= 0) {
      clearInterval(retryCountdown)
      retryCountdown = null
    }
  }, 1000)

  // Actual retry
  retryTimer = setTimeout(async () => {
    retryTimer = null
    if (connection && connection.state === HubConnectionState.Disconnected) {
      try {
        connectionStatus.value = 'Connecting'
        await connection.start()
        updateConnectionStatus() // Update status after successful connection
      } catch (err) {
        console.error(`SignalR Retry attempt ${retryAttempt.value} failed:`, err)
        updateConnectionStatus()
      }
    }
  }, delay)
}

// SignalR connection setup
onMounted(async () => {
  connection = new HubConnectionBuilder()
    //.withUrl("http://localhost:5312/datahub")
      .withUrl("http://raspberrypi-base:5312/datahub")
    // Remove automatic reconnect - we'll handle it ourselves with 5s intervals
    .build()

  // Connection state change handlers
  connection.onclose((error) => {
    console.log('SignalR connection closed', error)
    updateConnectionStatus()
  })

  // No need for onreconnecting/onreconnected since we disabled automatic reconnect

  // PositionUpdate removed - now using PvtUpdate for real GNSS position data

  connection.on("ImuUpdate", (data) => {
    imuData.value.acceleration.x = data.acceleration.x
    imuData.value.acceleration.y = data.acceleration.y
    imuData.value.acceleration.z = data.acceleration.z
    imuData.value.gyroscope.x = data.gyroscope.x
    imuData.value.gyroscope.y = data.gyroscope.y
    imuData.value.gyroscope.z = data.gyroscope.z
    imuData.value.magnetometer.x = data.magnetometer.x
    imuData.value.magnetometer.y = data.magnetometer.y
    imuData.value.magnetometer.z = data.magnetometer.z
  })

  connection.on("SystemHealthUpdate", (data) => {
    systemHealth.value.cpuUsage = data.cpuUsage
    systemHealth.value.memoryUsage = data.memoryUsage
    systemHealth.value.temperature = data.temperature
    systemHealth.value.batteryLevel = data.batteryLevel
    systemHealth.value.batteryVoltage = data.batteryVoltage
    systemHealth.value.isExternalPowerConnected = data.isExternalPowerConnected
    
    // Update powerStatus with battery data for the SystemPanel
    powerStatus.value.batteryLevel = data.batteryLevel
    powerStatus.value.batteryVoltage = data.batteryVoltage
    powerStatus.value.isExternalPowerConnected = data.isExternalPowerConnected
  })

  connection.on("SatelliteUpdate", (data) => {

    // Update connection status
    gnssData.value.connected = data.connected ?? false

    // Update satellite data from NAV-SAT messages
    gnssData.value.satellitesTracked = data.numSatellites
    gnssData.value.satellites = data.satellites.map(sat => ({
      svid: sat.svId,
      constellation: sat.gnssName,
      used: sat.svUsed,
      cn0: sat.cno,
      elevation: sat.elevation,
      azimuth: sat.azimuth,
      health: sat.health,
      qualityIndicator: sat.qualityIndicator,
      pseudorangeResidual: sat.pseudorangeResidual,
      differentialCorrection: sat.differentialCorrection,
      smoothed: sat.smoothed
    }))

    // Update constellation breakdown
    const constellations = { gps: {used: 0, tracked: 0}, glonass: {used: 0, tracked: 0}, galileo: {used: 0, tracked: 0}, beidou: {used: 0, tracked: 0} }

    data.satellites.forEach(sat => {

      switch(sat.gnssName.toLowerCase()) {
        case 'gps':
          constellations.gps.tracked++
          if (sat.svUsed) constellations.gps.used++
          break
        case 'glonass':
          constellations.glonass.tracked++
          if (sat.svUsed) constellations.glonass.used++
          break
        case 'galileo':
          constellations.galileo.tracked++
          if (sat.svUsed) constellations.galileo.used++
          break
        case 'beidou':
          constellations.beidou.tracked++
          if (sat.svUsed) constellations.beidou.used++
          break
      }
    })

    gnssData.value.constellations = constellations
  })

  connection.on("PvtUpdate", (data) => {

    // Update position and navigation data from NAV-PVT messages
    gnssData.value.latitude = data.latitude
    gnssData.value.longitude = data.longitude
    gnssData.value.altitude = data.heightMSL / 1000 // Convert mm to m
    gnssData.value.hAcc = data.horizontalAccuracy / 1000 // Convert mm to m
    gnssData.value.vAcc = data.verticalAccuracy / 1000 // Convert mm to m

    // Update RTK information based on carrier solution - RTK takes priority
    if (data.carrierSolution === 2) {
      gnssData.value.fixType = 'RTK Fixed'
      gnssData.value.rtkMode = 'Fixed'
      gnssData.value.rtk.active = true
    } else if (data.carrierSolution === 1) {
      gnssData.value.fixType = 'RTK Float'
      gnssData.value.rtkMode = 'Float'
      gnssData.value.rtk.active = true
    } else {
      // Only show basic status for non-RTK
      if (data.fixType === 0) {
        gnssData.value.fixType = 'No Fix'
      } else if (data.numSatellites >= 4) {
        gnssData.value.fixType = 'GNSS Fixed'
      } else {
        gnssData.value.fixType = 'Acquiring'
      }
      gnssData.value.rtk.active = false
    }

    gnssData.value.satellitesUsed = data.numSatellites
  })

  connection.on("DataRatesUpdate", (data) => {
    dataRates.value.kbpsGnssIn = data.kbpsGnssIn
    dataRates.value.kbpsGnssOut = data.kbpsGnssOut
    dataRates.value.kbpsLoRaIn = data.kbpsLoRaIn
    dataRates.value.kbpsLoRaOut = data.kbpsLoRaOut
  })

  connection.on("MessageRatesUpdate", (data) => {
    messageRates.value.messageRates = data.messageRates
    messageRates.value.timestamp = data.timestamp
  })

  connection.on("FileLoggingStatusUpdate", (data) => {
    fileLoggingStatus.value.driveAvailable = data.driveAvailable
    fileLoggingStatus.value.drivePath = data.drivePath
    fileLoggingStatus.value.currentSession = data.currentSession
    fileLoggingStatus.value.totalSpaceBytes = data.totalSpaceBytes
    fileLoggingStatus.value.availableSpaceBytes = data.availableSpaceBytes
    fileLoggingStatus.value.usedSpaceBytes = data.usedSpaceBytes
    fileLoggingStatus.value.activeFiles = data.activeFiles || []
  })

  connection.on("SurveyInStatus", (data) => {
    gnssData.value.surveyIn.active = data.active
    gnssData.value.surveyIn.valid = data.valid
    gnssData.value.surveyIn.duration = data.duration
    gnssData.value.surveyIn.observations = data.observations
    gnssData.value.surveyIn.accuracyMm = data.accuracyMm
    gnssData.value.surveyIn.position = data.position
  })

  connection.on("CorrectionsStatusUpdate", (data) => {
    gnssData.value.corrections.mode = data.mode
    gnssData.value.corrections.active = data.active
  })

  connection.on("DopUpdate", (data) => {
    gnssData.value.hdop = data.horizontalDop
    gnssData.value.vdop = data.verticalDop
    gnssData.value.pdop = data.positionDop
    gnssData.value.tdop = data.timeDop
  })

  try {
    connectionStatus.value = 'Connecting'
    await connection.start()
    console.log("SignalR Connected successfully!")
    updateConnectionStatus()
  } catch (err) {
    console.error("SignalR Connection Error: ", err)
    updateConnectionStatus()
  }
})

onUnmounted(async () => {
  clearRetryTimer()
  if (connection) {
    await connection.stop()
  }
})
</script>

<template>
  <div class="min-h-screen bg-gradient-to-br from-slate-100 to-slate-200">
    <!-- Header -->
    <header class="bg-gradient-to-r from-slate-800 to-slate-900 text-white border-b-2 border-slate-700">
      <div class="p-4">
        <div class="flex items-center justify-between">
          <div class="flex items-start space-x-4">
            <div class="flex flex-col items-start">
              <img src="/src/assets/logo.svg" alt="Sierra Logo" class="h-12 w-auto">
              <p class="text-slate-300">Positioning Data Collection System</p>
            </div>
          </div>
          <div class="flex items-center space-x-4">
            <!-- Connection status removed from header -->
          </div>
        </div>
      </div>
    </header>

    <!-- Main Content -->
    <main class="px-4 py-6 max-w-7xl mx-auto">
      <!-- GNSS System - Aligned with columns below -->
      <div class="mb-6 max-w-7xl mx-auto">
        <GnssPanel :gnssData="gnssData" :dataRates="dataRates" :messageRates="messageRates" />
      </div>
      
      <!-- Other Panels - Centered Two Column Masonry Layout -->
      <div class="columns-1 lg:columns-2 gap-6 space-y-6 max-w-7xl mx-auto">
        <!-- IMU Panel -->
        <div class="break-inside-avoid mb-6">
          <ImuPanel :imuData="imuData" :dataRates="dataRates" />
        </div>
        
        <!-- Camera Panel -->
        <div class="break-inside-avoid mb-6">
          <CameraPanel />
        </div>
        
        <!-- Encoder Panel -->
        <div class="break-inside-avoid mb-6">
          <EncoderPanel :encoderData="encoderData" />
        </div>
        
        <!-- File Logging Panel -->
        <div class="break-inside-avoid mb-6">
          <FileLoggingPanel :fileLoggingStatus="fileLoggingStatus" />
        </div>
        
        <!-- System Panel -->
        <div class="break-inside-avoid mb-6">
          <SystemPanel :systemHealth="systemHealth" :powerStatus="powerStatus" />
        </div>
      </div>
    </main>

    <!-- Connection Overlay -->
    <ConnectionOverlay 
      :connection-status="connectionStatus"
      :retry-attempt="retryAttempt"
      :next-retry-in="nextRetryIn"
    />
  </div>
</template>

<style scoped></style>
