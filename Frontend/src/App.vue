<script setup>
import { ref, onMounted, onUnmounted } from 'vue'
import { HubConnectionBuilder } from '@microsoft/signalr'
import GnssPanel from './components/GnssPanel.vue'
import ImuPanel from './components/ImuPanel.vue'
import CameraPanel from './components/CameraPanel.vue'
import EncoderPanel from './components/EncoderPanel.vue'
import SystemPanel from './components/SystemPanel.vue'

// SignalR connection
let connection = null

// Comprehensive GNSS data model
const gnssData = ref({
  // Fix status
  fixType: null,
  rtkMode: null,
  
  // Position
  latitude: 45.4234567, // Keep for position service
  longitude: -75.6987654, // Keep for position service
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

// SignalR connection setup
onMounted(async () => {
  connection = new HubConnectionBuilder()
    //.withUrl("http://localhost:5312/datahub")
      .withUrl("http://raspberrypi-rover.local:5312/datahub")
    .withAutomaticReconnect()
    .build()

  connection.on("PositionUpdate", (data) => {
    gnssData.value.latitude = data.latitude
    gnssData.value.longitude = data.longitude
  })

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

  try {
    await connection.start()
    console.log("SignalR Connected successfully!")
  } catch (err) {
    console.error("SignalR Connection Error: ", err)
  }
})

onUnmounted(async () => {
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
