<script setup>
import { ref, onMounted, onUnmounted, provide } from 'vue'
import { HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr'
import Layout from './components/layout/Layout.vue'
import GnssStatus from './components/gnss/GnssStatus.vue'
import SatelliteHealthPanel from './components/gnss/SatelliteHealthPanel.vue'
import RtkPanel from './components/gnss/RtkPanel.vue'
import PositionScatterPlot from './components/gnss/PositionScatterPlot.vue'
import MessageRatesPanel from './components/MessageRatesPanel.vue'
import ImuPanel from './components/ImuPanel.vue'
import CameraPanel from './components/CameraPanel.vue'
import EncoderPanel from './components/EncoderPanel.vue'
import SystemPanel from './components/SystemPanel.vue'
import FileLoggingPanel from './components/FileLoggingPanel.vue'
import ConnectionOverlay from './components/ConnectionOverlay.vue'
import WiFiPanel from './components/WiFiPanel.vue'

// SignalR connection
let connection = null
const signalrConnection = ref(null)

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
    mode: 'Disabled' // 'Disabled', 'Receive', 'Send'
  },

  // Reference station position (for base station mode)
  referenceStation: {
    stationId: null,
    latitude: null,
    longitude: null,
    altitude: null
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
  isExternalPowerConnected: false,
  hostname: null
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
  kbpsLoRaOut: null,
  kbpsImu: null
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

const cameraData = ref({
  timestamp: null,
  imageBase64: '',
  imageSizeBytes: 0,
  imageWidth: 0,
  imageHeight: 0,
  format: 'JPEG',
  captureTimeMs: 0,
  encodingTimeMs: 0,
  isConnected: false
})

const currentMode = ref('Disabled')

// Navigation state
const activeSection = ref('gnss')

// Component refs
const cameraPanelRef = ref(null)

// Mode change handler
const handleModeChanged = (newMode) => {
  console.log(`App.vue handleModeChanged called with: ${newMode}`)
  console.log(`Updating currentMode from ${currentMode.value} to ${newMode}`)
  currentMode.value = newMode
}

// Section navigation handler
const handleSectionChanged = (sectionId) => {
  activeSection.value = sectionId
}

// Provide SignalR connection to child components
provide('signalrConnection', signalrConnection)

// Helper functions (moved to components where needed)
const getSignalColor = (strength) => {
  if (strength > -70) return 'text-green-500'
  if (strength > -85) return 'text-yellow-500'
  return 'text-red-500'
}

// Connection management functions
const updateConnectionStatus = () => {
  if (!connection) {
    connectionStatus.value = 'Reconnecting'
    return
  }

  switch (connection.state) {
    case HubConnectionState.Connected:
      connectionStatus.value = 'Connected'
      retryAttempt.value = 0
      clearRetryTimer()
      break
    case HubConnectionState.Connecting:
    case HubConnectionState.Disconnected:
      connectionStatus.value = 'Reconnecting'
      // Only start our custom retry after SignalR has given up
      if (connection.state === HubConnectionState.Disconnected) {
        scheduleRetry()
      }
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
        connectionStatus.value = 'Reconnecting'
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
    .withUrl("http://rover.local/datahub")  // Use this for Raspberry Pi deployment
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
    dataRates.value.kbpsImu = data.kbps
  })

  connection.on("SystemHealthUpdate", (data) => {
    systemHealth.value.cpuUsage = data.cpuUsage
    systemHealth.value.memoryUsage = data.memoryUsage
    systemHealth.value.temperature = data.temperature
    systemHealth.value.batteryLevel = data.batteryLevel
    systemHealth.value.batteryVoltage = data.batteryVoltage
    systemHealth.value.isExternalPowerConnected = data.isExternalPowerConnected
    systemHealth.value.hostname = data.hostname

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

    // Use the enhanced fix type string from backend instead of hardcoded logic
    gnssData.value.fixType = data.fixTypeString || 'No Fix'
    
    // Update RTK mode based on fix type string
    if (data.fixTypeString && data.fixTypeString.includes('RTK')) {
      gnssData.value.rtkMode = data.fixTypeString.includes('Fixed') ? 'Fixed' : 'Float'
    } else {
      gnssData.value.rtkMode = null
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
  })

  connection.on("ReferenceStationPosition", (data) => {
    gnssData.value.referenceStation.stationId = data.stationId
    gnssData.value.referenceStation.latitude = data.latitude
    gnssData.value.referenceStation.longitude = data.longitude
    gnssData.value.referenceStation.altitude = data.altitude
  })

  connection.on("DopUpdate", (data) => {
    gnssData.value.hdop = data.horizontalDop
    gnssData.value.vdop = data.verticalDop
    gnssData.value.pdop = data.positionDop
    gnssData.value.tdop = data.timeDop
  })

  connection.on("CameraUpdate", (data) => {
    cameraData.value.timestamp = data.timestamp
    cameraData.value.imageBase64 = data.imageBase64
    cameraData.value.imageSizeBytes = data.imageSizeBytes
    cameraData.value.imageWidth = data.imageWidth
    cameraData.value.imageHeight = data.imageHeight
    cameraData.value.format = data.format
    cameraData.value.captureTimeMs = data.captureTimeMs
    cameraData.value.encodingTimeMs = data.encodingTimeMs
    cameraData.value.isConnected = data.isConnected

    // Log frame size received
    if (data.isConnected && data.imageSizeBytes > 0) {
      const sizeKb = (data.imageSizeBytes / 1024).toFixed(1)
      console.log(`Camera frame received: ${data.imageWidth}x${data.imageHeight}, ${sizeKb} KB (${data.format})`)
    } else if (!data.isConnected) {
      console.log('Camera disconnected')
    }

    // Call the CameraPanel's handler method
    if (cameraPanelRef.value && cameraPanelRef.value.handleCameraUpdate) {
      cameraPanelRef.value.handleCameraUpdate(data)
    }
  })

  connection.on("ModeChanged", (data) => {
    console.log(`SignalR ModeChanged event received:`, data)
    console.log(`Setting current mode from ${currentMode.value} to: ${data.Mode}`)
    currentMode.value = data.Mode
  })

  connection.on("HostnameUpdated", (data) => {
    console.log(`Hostname updated to: ${data.hostname}`)
    systemHealth.value.hostname = data.hostname

    // Optional: Show a toast or notification
    console.log(`Hostname update message: ${data.message}`)
  })

  // Set the connection ref for child components immediately
  signalrConnection.value = connection
  console.log("SignalR connection object set for child components")

  try {
    connectionStatus.value = 'Reconnecting'
    await connection.start()
    console.log("SignalR Connected successfully!")

    // Get initial mode
    try {
      console.log('Requesting initial mode from server...')
      const initialMode = await connection.invoke('GetCurrentMode')
      console.log(`Initial mode retrieved: ${initialMode}`)
      currentMode.value = initialMode
      console.log(`Current mode state updated to: ${currentMode.value}`)
    } catch (error) {
      console.error('Failed to get initial mode:', error)
    }

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
  <div class="h-screen">
    <Layout
      :active-section="activeSection"
      @section-changed="handleSectionChanged"
    >
      <template #header-actions>
        <!-- Battery Indicator -->
        <div class="flex items-center space-x-2 text-sm">
          <div class="flex items-center">
            <!-- Charging/Plugged icon (left of battery) -->
            <svg
              v-if="systemHealth.isExternalPowerConnected"
              xmlns="http://www.w3.org/2000/svg"
              viewBox="0 0 24 24"
              fill="currentColor"
              class="text-slate-600 w-3 h-3 mr-0.5"
            >
              <path
                fill-rule="evenodd"
                d="M14.615 1.595a.75.75 0 0 1 .359.852L12.982 9.75h7.268a.75.75 0 0 1 .548 1.262l-10.5 11.25a.75.75 0 0 1-1.272-.71l1.992-7.302H3.75a.75.75 0 0 1-.548-1.262l10.5-11.25a.75.75 0 0 1 .913-.143Z"
                clip-rule="evenodd"
              />
            </svg>
            <!-- Battery Icon with overlaid percentage -->
            <div class="relative">
              <svg
                xmlns="http://www.w3.org/2000/svg"
                fill="none"
                viewBox="0 0 24 24"
                stroke-width="1.5"
                stroke="currentColor"
                class="text-slate-600 w-8 h-8"
              >
                <path
                  stroke-linecap="round"
                  stroke-linejoin="round"
                  d="M21 10.5h.375c.621 0 1.125.504 1.125 1.125v2.25c0 .621-.504 1.125-1.125 1.125H21M3.75 18h15A2.25 2.25 0 0 0 21 15.75v-6a2.25 2.25 0 0 0-2.25-2.25h-15A2.25 2.25 0 0 0 1.5 9.75v6A2.25 2.25 0 0 0 3.75 18Z"
                />
              </svg>
              <!-- Battery percentage overlaid -->
              <div class="absolute inset-0 flex items-center justify-center">
                <span class="text-slate-600 text-[0.77rem] font-bold leading-none">
                  {{ systemHealth.batteryLevel !== null ? Math.round(systemHealth.batteryLevel) : '--' }}
                </span>
              </div>
            </div>
          </div>
        </div>
      </template>

      <!-- Dynamic Content Based on Active Section -->
      <div class="space-y-6">
        <!-- GNSS Section -->
        <div v-if="activeSection === 'gnss'" class="space-y-6">
          <!-- GNSS Status Summary -->
          <GnssStatus :gnssData="gnssData" />

          <!-- GNSS Subpanels -->
          <div class="columns-1 lg:columns-2 gap-6 space-y-6">
            <!-- Satellite Health Subsection -->
            <div class="break-inside-avoid mb-6">
              <SatelliteHealthPanel :gnssData="gnssData" />
            </div>

            <!-- Unified RTK Panel with Mode Selection -->
            <div class="break-inside-avoid mb-6">
              <RtkPanel :gnssData="gnssData" />
            </div>

            <!-- Position Scatter Plot -->
            <div class="break-inside-avoid mb-6">
              <PositionScatterPlot :gnssData="gnssData" />
            </div>

            <!-- UBX Message Rates Subsection -->
            <div class="break-inside-avoid mb-6">
              <MessageRatesPanel :messageRates="messageRates" />
            </div>
          </div>
        </div>

        <!-- Camera Section -->
        <div v-if="activeSection === 'camera'">
          <CameraPanel ref="cameraPanelRef" />
        </div>

        <!-- IMU Section -->
        <div v-if="activeSection === 'imu'">
          <ImuPanel :imuData="imuData" :dataRates="dataRates" />
        </div>

        <!-- Encoder Section -->
        <div v-if="activeSection === 'encoder'">
          <EncoderPanel :encoderData="encoderData" />
        </div>

        <!-- WiFi Section -->
        <div v-if="activeSection === 'wifi'">
          <WiFiPanel />
        </div>

        <!-- Logging Section -->
        <div v-if="activeSection === 'logging'">
          <FileLoggingPanel :fileLoggingStatus="fileLoggingStatus" />
        </div>

        <!-- System Section -->
        <div v-if="activeSection === 'system'">
          <SystemPanel :systemHealth="systemHealth" :powerStatus="powerStatus" :dataRates="dataRates" />
        </div>
      </div>
    </Layout>

    <!-- Connection Overlay -->
    <ConnectionOverlay
      :connection-status="connectionStatus"
      :retry-attempt="retryAttempt"
      :next-retry-in="nextRetryIn"
    />
  </div>
</template>

<style scoped></style>
