<script setup>
import { ref, onMounted, onUnmounted } from 'vue'
import { HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr'
import GnssPanel from './components/GnssPanel.vue'
import ImuPanel from './components/ImuPanel.vue'
import CameraPanel from './components/CameraPanel.vue'
import EncoderPanel from './components/EncoderPanel.vue'
import SystemPanel from './components/SystemPanel.vue'
import ConnectionStatus from './components/ConnectionStatus.vue'

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
  
  // Timing and integrity
  tAcc: null,
  solutionLatency: null,
  
  // Hardware/environment diagnostics
  antenna: { status: null, shortDetected: false, openDetected: false },
  jamming: { detected: false, indicator: null },
  agc: { level: null },
  rfNoise: { level: null },
  temperature: null
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
  storageUsed: 45  // Keep storage as mock since not implemented
})

const powerStatus = ref({
  batteryLevel: null,
  isCharging: false,
  powerConsumption: null,
  estimatedRuntime: null
})

const dataRates = ref({
  gnssRate: null,
  imuRate: null,
  correctionRate: null
})

const fileManagement = ref({
  currentSession: null,
  filesCreated: null,
  totalSize: null,
  availableSpace: null
})

const encoderData = ref({
  rawCount: null,
  direction: null,
  pulsesPerSecond: null
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
      .withUrl("http://raspberrypi-rover.local:5312/datahub")
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

    // Update fix type based on UBX fix type values
    const fixTypes = {
      0: 'No Fix',
      1: 'Dead Reckoning',
      2: '2D Fix',
      3: '3D Fix',
      4: 'GNSS+DR',
      5: 'Time Only'
    }
    gnssData.value.fixType = fixTypes[data.fixType] || 'Unknown'

    // Update RTK information based on carrier solution
    if (data.carrierSolution === 2) {
      gnssData.value.fixType = 'RTK Fixed'
      gnssData.value.rtkMode = 'Fixed'
      gnssData.value.rtk.active = true
    } else if (data.carrierSolution === 1) {
      gnssData.value.fixType = 'RTK Float'
      gnssData.value.rtkMode = 'Float'
      gnssData.value.rtk.active = true
    } else {
      gnssData.value.rtk.active = false
    }

    gnssData.value.satellitesUsed = data.numSatellites
    gnssData.value.tAcc = data.timeAccuracy
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
  <div class="min-h-screen bg-gradient-to-br from-slate-50 to-slate-100">
    <!-- Header -->
    <header class="bg-gradient-to-r from-slate-800 to-slate-900 text-white border-b-2 border-slate-700">
      <div class="px-4 py-6">
        <div class="flex items-center justify-between">
          <div class="flex items-center space-x-3">
            <div class="w-12 h-12 bg-gradient-to-br from-teal-400 to-blue-500 rounded-xl flex items-center justify-center">
              <svg class="w-7 h-7 text-white" fill="currentColor" viewBox="0 0 24 24">
                <path d="M12 2L13.09 8.26L22 9L13.09 9.74L12 16L10.91 9.74L2 9L10.91 8.26L12 2Z"/>
              </svg>
            </div>
            <div>
              <h1 class="text-2xl md:text-3xl font-bold bg-gradient-to-r from-white to-slate-200 bg-clip-text text-transparent">
                GNSS Data Collection System
              </h1>
              <p class="text-slate-300 font-medium">Real-time monitoring and administration</p>
            </div>
          </div>
          <div class="flex items-center space-x-4">
            <div class="bg-slate-700/50 backdrop-blur-sm rounded-lg px-4 py-2 border border-slate-600">
              <ConnectionStatus
                :connection-status="connectionStatus"
                :retry-attempt="retryAttempt"
                :next-retry-in="nextRetryIn"
              />
            </div>
          </div>
        </div>
      </div>
    </header>

    <!-- Main Content -->
    <main class="px-4 py-6 max-w-7xl mx-auto">
      <!-- GNSS System -->
      <GnssPanel :gnssData="gnssData" :dataRates="dataRates" />
      
      <!-- Sensor Data Row -->
      <div class="mb-6">
        <div class="grid gap-4 grid-cols-1 md:grid-cols-3">
          <ImuPanel :imuData="imuData" :dataRates="dataRates" />
          <CameraPanel />
          <EncoderPanel :encoderData="encoderData" />
        </div>
      </div>

      <!-- System Status Row -->
      <div class="mb-6">
        <div class="grid gap-4 grid-cols-1">
          <SystemPanel :systemHealth="systemHealth" :powerStatus="powerStatus" :fileManagement="fileManagement" />
        </div>
      </div>
      


    </main>
  </div>
</template>

<style scoped></style>
