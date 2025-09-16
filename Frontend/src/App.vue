<script setup>
import { ref, onMounted, onUnmounted } from 'vue'
import { HubConnectionBuilder } from '@microsoft/signalr'

// SignalR connection
let connection = null

// Comprehensive GNSS data model
const gnssData = ref({
  // Fix status
  fixType: 'RTK Fixed',
  rtkMode: 'Fixed', // Float, Fixed
  
  // Position
  latitude: 45.4234567,
  longitude: -75.6987654,
  altitude: 145.23,
  
  // Accuracy estimates
  hAcc: 0.014, // Horizontal accuracy (m)
  vAcc: 0.023, // Vertical accuracy (m)
  
  // Dilution of Precision
  hdop: 0.92,
  vdop: 1.15,
  pdop: 1.48,
  tdop: 0.87,
  
  // Satellite counts
  satellitesUsed: 12,
  satellitesTracked: 18,
  
  // Constellation breakdown
  constellations: {
    gps: { used: 7, tracked: 9 },
    glonass: { used: 3, tracked: 5 },
    galileo: { used: 2, tracked: 3 },
    beidou: { used: 0, tracked: 1 }
  },
  
  // Per-satellite data
  satellites: [
    { svid: 2, constellation: 'GPS', used: true, cn0: 45, elevation: 67, azimuth: 45, quality: 'Good' },
    { svid: 5, constellation: 'GPS', used: true, cn0: 42, elevation: 45, azimuth: 120, quality: 'Good' },
    { svid: 12, constellation: 'GPS', used: true, cn0: 38, elevation: 23, azimuth: 280, quality: 'Fair' },
    { svid: 15, constellation: 'GPS', used: true, cn0: 47, elevation: 78, azimuth: 15, quality: 'Excellent' },
    { svid: 18, constellation: 'GPS', used: true, cn0: 41, elevation: 34, azimuth: 190, quality: 'Good' },
    { svid: 21, constellation: 'GPS', used: true, cn0: 39, elevation: 28, azimuth: 310, quality: 'Fair' },
    { svid: 25, constellation: 'GPS', used: true, cn0: 44, elevation: 55, azimuth: 85, quality: 'Good' },
    { svid: 29, constellation: 'GPS', used: false, cn0: 32, elevation: 12, azimuth: 220, quality: 'Poor' },
    { svid: 31, constellation: 'GPS', used: false, cn0: 35, elevation: 18, azimuth: 160, quality: 'Fair' },
    { svid: 1, constellation: 'GLONASS', used: true, cn0: 43, elevation: 42, azimuth: 75, quality: 'Good' },
    { svid: 2, constellation: 'GLONASS', used: true, cn0: 40, elevation: 38, azimuth: 205, quality: 'Good' },
    { svid: 11, constellation: 'GLONASS', used: true, cn0: 37, elevation: 25, azimuth: 340, quality: 'Fair' },
    { svid: 12, constellation: 'GLONASS', used: false, cn0: 33, elevation: 15, azimuth: 130, quality: 'Poor' },
    { svid: 24, constellation: 'GLONASS', used: false, cn0: 36, elevation: 20, azimuth: 260, quality: 'Fair' },
    { svid: 7, constellation: 'Galileo', used: true, cn0: 46, elevation: 61, azimuth: 95, quality: 'Excellent' },
    { svid: 12, constellation: 'Galileo', used: true, cn0: 43, elevation: 39, azimuth: 175, quality: 'Good' },
    { svid: 19, constellation: 'Galileo', used: false, cn0: 34, elevation: 16, azimuth: 295, quality: 'Poor' },
    { svid: 3, constellation: 'BeiDou', used: false, cn0: 31, elevation: 11, azimuth: 240, quality: 'Poor' }
  ],
  
  // RTK-specific metrics
  rtk: {
    active: true,
    arRatio: 999.9, // Ambiguity resolution ratio
    correctionAge: 1.2, // seconds
    baselineLength: 1247.5, // meters
    relativeAccuracy: { north: 0.008, east: 0.012, down: 0.019 } // meters
  },
  
  // Timing and integrity
  tAcc: 15, // Time accuracy (ns)
  solutionLatency: 145, // ms
  
  // Hardware/environment diagnostics
  antenna: { status: 'OK', shortDetected: false, openDetected: false },
  jamming: { detected: false, indicator: 0 }, // 0-255 scale
  agc: { level: 142 }, // AGC level
  rfNoise: { level: -98 }, // dBm
  temperature: 42.5 // Celsius
})

const loraData = ref({
  mode: 'Base Station',
  signalStrength: -85,
  correctionRate: '1.2 Hz',
  packetsReceived: 1247,
  packetsSent: 1892
})

const imuData = ref({
  acceleration: { x: 0.12, y: -0.05, z: 9.81 },
  gyroscope: { x: 0.001, y: -0.003, z: 0.002 },
  magnetometer: { x: 25.4, y: -18.2, z: 42.1 }
})

const systemHealth = ref({
  cpuUsage: 23,
  memoryUsage: 67,
  temperature: 52,
  storageUsed: 45
})

const powerStatus = ref({
  batteryLevel: 78,
  isCharging: false,
  powerConsumption: 12.4,
  estimatedRuntime: '6h 24m'
})

const dataRates = ref({
  gnssRate: '5.2 Hz',
  imuRate: '100 Hz',
  correctionRate: '1.2 Hz'
})

const fileManagement = ref({
  currentSession: '2024-01-15_session_003',
  filesCreated: 47,
  totalSize: '2.3 GB',
  availableSpace: '125.7 GB'
})

const encoderData = ref({
  rawCount: 0,
  direction: 'CW',
  pulsesPerSecond: 0
})

// Helper functions
const getBatteryColor = (level) => {
  if (level > 60) return 'text-green-500'
  if (level > 30) return 'text-yellow-500'
  return 'text-red-500'
}

const getSignalColor = (strength) => {
  if (strength > -70) return 'text-green-500'
  if (strength > -85) return 'text-yellow-500'
  return 'text-red-500'
}

const getUsageColor = (usage) => {
  if (usage < 50) return 'text-green-500'
  if (usage < 80) return 'text-yellow-500'
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
      <div class="mb-6">
        <!-- GNSS Section Header -->
        <div class="bg-gradient-to-r from-slate-800 to-slate-900 text-white rounded-t-2xl p-6 border-2 border-slate-600">
          <div class="flex items-center space-x-4">
            <div class="w-12 h-12 bg-gradient-to-br from-emerald-400 to-teal-500 rounded-xl flex items-center justify-center border-2 border-emerald-300">
              <svg class="w-6 h-6 text-white" fill="currentColor" viewBox="0 0 24 24">
                <path d="M12 2L13.09 8.26L22 9L13.09 9.74L12 16L10.91 9.74L2 9L10.91 8.26L12 2Z"/>
              </svg>
            </div>
            <div>
              <h1 class="text-2xl font-bold">GNSS System</h1>
              <p class="text-slate-300 text-sm">Global Navigation Satellite System</p>
            </div>
          </div>
        </div>
        
        <!-- GNSS Status Summary -->
        <div class="bg-gradient-to-r from-slate-700 to-slate-800 text-white px-6 py-4 border-x-2 border-slate-600">
          <div class="flex items-center justify-between mb-4">
            <div class="flex items-center space-x-4">
              <div>
                <div class="text-sm text-slate-300">Current Position</div>
                <div class="text-lg font-mono">{{ gnssData.latitude.toFixed(7) }}°, {{ gnssData.longitude.toFixed(7) }}°</div>
              </div>
            </div>
            <div class="flex items-center space-x-3">
              <div class="w-3 h-3 rounded-full animate-pulse" :class="gnssData.fixType === 'RTK Fixed' ? 'bg-emerald-400' : gnssData.fixType === 'RTK Float' ? 'bg-yellow-400' : 'bg-red-400'"></div>
              <span class="text-lg font-bold px-4 py-2 rounded-xl" :class="gnssData.fixType === 'RTK Fixed' ? 'bg-emerald-500/20 text-emerald-300' : gnssData.fixType === 'RTK Float' ? 'bg-yellow-500/20 text-yellow-300' : 'bg-red-500/20 text-red-300'">{{ gnssData.fixType }}</span>
            </div>
          </div>
          
          <!-- Core Health Summary -->
          <div class="grid grid-cols-2 md:grid-cols-6 gap-4 text-center">
            <div>
              <div class="text-xs text-slate-400 mb-1">hAcc</div>
              <div class="text-lg font-bold text-emerald-300">{{ (gnssData.hAcc * 1000).toFixed(0) }}mm</div>
            </div>
            <div>
              <div class="text-xs text-slate-400 mb-1">vAcc</div>
              <div class="text-lg font-bold text-emerald-300">{{ (gnssData.vAcc * 1000).toFixed(0) }}mm</div>
            </div>
            <div>
              <div class="text-xs text-slate-400 mb-1">HDOP</div>
              <div class="text-lg font-bold text-amber-300">{{ gnssData.hdop.toFixed(2) }}</div>
            </div>
            <div>
              <div class="text-xs text-slate-400 mb-1">VDOP</div>
              <div class="text-lg font-bold text-amber-300">{{ gnssData.vdop.toFixed(2) }}</div>
            </div>
            <div>
              <div class="text-xs text-slate-400 mb-1">PDOP</div>
              <div class="text-lg font-bold text-amber-300">{{ gnssData.pdop.toFixed(2) }}</div>
            </div>
            <div>
              <div class="text-xs text-slate-400 mb-1">Satellites</div>
              <div class="text-lg font-bold text-blue-300">{{ gnssData.satellitesUsed }}/{{ gnssData.satellitesTracked }}</div>
            </div>
          </div>
        </div>
        
        <!-- GNSS Subsections -->
        <div class="bg-white rounded-b-2xl border-2 border-slate-200 p-4">
          <div class="grid grid-cols-1 lg:grid-cols-2 gap-4 mb-6">
            <!-- Satellite Health Subsection -->
          <div class="bg-white/90 backdrop-blur-sm rounded-2xl border-2 border-slate-200 p-4">
            <div class="flex items-center space-x-3 mb-4">
              <svg class="w-6 h-6 text-blue-600" fill="currentColor" viewBox="0 0 24 24">
                <path d="M12 2L13.09 8.26L22 9L13.09 9.74L12 16L10.91 9.74L2 9L10.91 8.26L12 2Z"/>
              </svg>
              <h2 class="text-xl font-bold text-slate-800">Satellite Health</h2>
              <div class="ml-auto text-sm text-emerald-600 font-semibold">{{ dataRates.gnssRate }}</div>
            </div>
            
            <!-- Constellation Summary -->
            <div class="grid grid-cols-4 gap-3 mb-6">
              <div class="text-center p-3 bg-blue-50 rounded-xl">
                <div class="text-xs text-slate-600 mb-1">GPS</div>
                <div class="text-lg font-bold text-blue-700">{{ gnssData.constellations.gps.used }}/{{ gnssData.constellations.gps.tracked }}</div>
              </div>
              <div class="text-center p-3 bg-red-50 rounded-xl">
                <div class="text-xs text-slate-600 mb-1">GLONASS</div>
                <div class="text-lg font-bold text-red-700">{{ gnssData.constellations.glonass.used }}/{{ gnssData.constellations.glonass.tracked }}</div>
              </div>
              <div class="text-center p-3 bg-purple-50 rounded-xl">
                <div class="text-xs text-slate-600 mb-1">Galileo</div>
                <div class="text-lg font-bold text-purple-700">{{ gnssData.constellations.galileo.used }}/{{ gnssData.constellations.galileo.tracked }}</div>
              </div>
              <div class="text-center p-3 bg-yellow-50 rounded-xl">
                <div class="text-xs text-slate-600 mb-1">BeiDou</div>
                <div class="text-lg font-bold text-yellow-700">{{ gnssData.constellations.beidou.used }}/{{ gnssData.constellations.beidou.tracked }}</div>
              </div>
            </div>
            
            <!-- Per-Satellite Table -->
            <div class="bg-slate-50 rounded-xl overflow-hidden">
              <div class="overflow-x-auto">
                <table class="w-full text-sm">
                  <thead class="bg-slate-100">
                    <tr class="text-xs font-semibold text-slate-600 uppercase">
                      <th class="px-3 py-2 text-left">ID</th>
                      <th class="px-3 py-2 text-left">System</th>
                      <th class="px-3 py-2 text-center">Used</th>
                      <th class="px-3 py-2 text-right">C/N0</th>
                      <th class="px-3 py-2 text-right">Elev</th>
                      <th class="px-3 py-2 text-right">Az</th>
                    </tr>
                  </thead>
                  <tbody class="divide-y divide-slate-200">
                    <tr v-for="sat in gnssData.satellites.slice(0, 8)" :key="sat.svid + sat.constellation" 
                        class="hover:bg-white transition-colors" 
                        :class="sat.used ? 'bg-emerald-50/50' : ''">
                      <td class="px-3 py-2 font-mono font-semibold">{{ sat.svid }}</td>
                      <td class="px-3 py-2">
                        <span class="text-xs px-2 py-1 rounded font-medium"
                              :class="sat.constellation === 'GPS' ? 'bg-blue-100 text-blue-700' : 
                                     sat.constellation === 'GLONASS' ? 'bg-red-100 text-red-700' :
                                     sat.constellation === 'Galileo' ? 'bg-purple-100 text-purple-700' :
                                     'bg-yellow-100 text-yellow-700'">
                          {{ sat.constellation.substring(0,3) }}
                        </span>
                      </td>
                      <td class="px-3 py-2 text-center">
                        <div class="w-2 h-2 rounded-full mx-auto" 
                             :class="sat.used ? 'bg-emerald-500' : 'bg-slate-300'"></div>
                      </td>
                      <td class="px-3 py-2 text-right font-mono" 
                          :class="sat.cn0 >= 40 ? 'text-emerald-600 font-semibold' : 
                                 sat.cn0 >= 35 ? 'text-yellow-600' : 'text-red-600'">
                        {{ sat.cn0 }}
                      </td>
                      <td class="px-3 py-2 text-right font-mono text-xs text-slate-600">{{ sat.elevation }}°</td>
                      <td class="px-3 py-2 text-right font-mono text-xs text-slate-600">{{ sat.azimuth }}°</td>
                    </tr>
                  </tbody>
                </table>
              </div>
            </div>
          </div>

            <!-- RTK Quality Subsection (Conditional) -->
          <div v-if="gnssData.rtk.active" class="bg-white/90 backdrop-blur-sm rounded-2xl border-2 border-slate-200 p-4">
            <div class="flex items-center justify-between mb-4">
              <div class="flex items-center space-x-3">
                <svg class="w-6 h-6 text-emerald-600" fill="currentColor" viewBox="0 0 24 24">
                  <path d="M12,2A3,3 0 0,1 15,5V11A3,3 0 0,1 12,14A3,3 0 0,1 9,11V5A3,3 0 0,1 12,2M19,11C19,14.53 16.39,17.44 13,17.93V21H11V17.93C7.61,17.44 5,14.53 5,11H7A5,5 0 0,0 12,16A5,5 0 0,0 17,11H19Z"/>
                </svg>
                <h2 class="text-xl font-bold text-slate-800">RTK Quality</h2>
                <div class="text-sm text-emerald-600 font-semibold">{{ dataRates.correctionRate }}</div>
              </div>
              <span class="text-sm font-bold px-3 py-1 rounded-lg" 
                    :class="gnssData.rtkMode === 'Fixed' ? 'bg-emerald-100 text-emerald-700' : 'bg-yellow-100 text-yellow-700'">
                {{ gnssData.rtkMode }}
              </span>
            </div>
            
            <div class="grid grid-cols-2 gap-4 mb-6">
              <div class="text-center p-4 bg-emerald-50 rounded-xl">
                <div class="text-xs text-slate-600 mb-1">AR Ratio</div>
                <div class="text-2xl font-bold text-emerald-700">{{ gnssData.rtk.arRatio.toFixed(1) }}</div>
              </div>
              <div class="text-center p-4 bg-blue-50 rounded-xl">
                <div class="text-xs text-slate-600 mb-1">Correction Age</div>
                <div class="text-2xl font-bold text-blue-700">{{ gnssData.rtk.correctionAge.toFixed(1) }}s</div>
              </div>
            </div>
            
            <div class="space-y-2">
              <div class="flex justify-between p-3 bg-slate-50 rounded-lg">
                <span class="text-slate-600">Baseline Length:</span>
                <span class="font-bold">{{ gnssData.rtk.baselineLength.toFixed(0) }}m</span>
              </div>
              <div class="p-3 bg-slate-50 rounded-lg">
                <div class="text-xs text-slate-600 mb-2">Relative Accuracy</div>
                <div class="grid grid-cols-3 gap-2 text-xs">
                  <div class="text-center">
                    <div class="font-mono font-semibold">{{ gnssData.rtk.relativeAccuracy.north.toFixed(3) }}m</div>
                    <div class="text-slate-500">North</div>
                  </div>
                  <div class="text-center">
                    <div class="font-mono font-semibold">{{ gnssData.rtk.relativeAccuracy.east.toFixed(3) }}m</div>
                    <div class="text-slate-500">East</div>
                  </div>
                  <div class="text-center">
                    <div class="font-mono font-semibold">{{ gnssData.rtk.relativeAccuracy.down.toFixed(3) }}m</div>
                    <div class="text-slate-500">Down</div>
                  </div>
                </div>
              </div>
            </div>
          </div>
          
            <!-- If RTK not active, show basic status in its place -->
            <div v-else class="bg-slate-50 rounded-2xl border-2 border-slate-300 p-4">
              <div class="flex items-center space-x-3 mb-4">
                <svg class="w-6 h-6 text-slate-600" fill="currentColor" viewBox="0 0 24 24">
                  <path d="M12 2L13.09 8.26L22 9L13.09 9.74L12 16L10.91 9.74L2 9L10.91 8.26L12 2Z"/>
                </svg>
                <h2 class="text-xl font-bold text-slate-800">Standard GNSS</h2>
              </div>
              
              <div class="grid grid-cols-1 gap-4">
                <div class="flex justify-between p-3 bg-white rounded-lg border">
                  <span class="text-slate-600">Fix Type:</span>
                  <span class="font-bold text-slate-800">{{ gnssData.fixType }}</span>
                </div>
                <div class="flex justify-between p-3 bg-white rounded-lg border">
                  <span class="text-slate-600">Altitude:</span>
                  <span class="font-bold font-mono text-slate-800">{{ gnssData.altitude.toFixed(2) }}m</span>
                </div>
              </div>
            </div>
          </div>
          
          <!-- Additional GNSS Subsections -->
          <div class="grid grid-cols-1 lg:grid-cols-2 gap-4 mt-6">
            <!-- Timing & Integrity Subsection -->
            <div class="bg-slate-50 rounded-2xl border-2 border-slate-300 p-4">
              <div class="flex items-center space-x-3 mb-4">
                <svg class="w-6 h-6 text-slate-600" fill="currentColor" viewBox="0 0 24 24">
                  <path d="M12,20A7,7 0 0,1 5,13A7,7 0 0,1 12,6A7,7 0 0,1 19,13A7,7 0 0,1 12,20M19.03,7.39L20.45,5.97C20,5.46 19.55,5 19.04,4.56L17.62,6C16.07,4.74 14.12,4 12,4A9,9 0 0,0 3,13A9,9 0 0,0 12,22C17,22 21,17.97 21,13C21,10.88 20.26,8.93 19.03,7.39M11,14H13V8H11M15,1H9V3H15V1Z"/>
                </svg>
                <h3 class="text-xl font-bold text-slate-800">Timing & Integrity</h3>
              </div>
              
              <div class="grid grid-cols-1 gap-4">
                <div class="flex justify-between p-3 bg-white rounded-lg border">
                  <span class="text-slate-600">Time Accuracy:</span>
                  <span class="font-bold font-mono text-slate-800">{{ gnssData.tAcc }}ns</span>
                </div>
                <div class="flex justify-between p-3 bg-white rounded-lg border">
                  <span class="text-slate-600">Solution Latency:</span>
                  <span class="font-bold font-mono text-slate-800">{{ gnssData.solutionLatency }}ms</span>
                </div>
              </div>
            </div>
            
            <!-- Hardware & Environment Subsection -->
            <div class="bg-slate-50 rounded-2xl border-2 border-slate-300 p-4">
              <div class="flex items-center space-x-3 mb-4">
                <svg class="w-6 h-6 text-slate-600" fill="currentColor" viewBox="0 0 24 24">
                  <path d="M11,15H13V17H11V15M11,7H13V13H11V7M12,2C6.47,2 2,6.5 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M12,20A8,8 0 0,1 4,12A8,8 0 0,1 12,4A8,8 0 0,1 20,12A8,8 0 0,1 12,20Z"/>
                </svg>
                <h3 class="text-xl font-bold text-slate-800">Hardware & Environment</h3>
              </div>
              
              <div class="grid grid-cols-2 gap-3 mb-4">
                <div class="text-center p-3 rounded-xl" :class="gnssData.antenna.status === 'OK' ? 'bg-emerald-100 border border-emerald-200' : 'bg-red-100 border border-red-200'">
                  <div class="text-xs text-slate-600 mb-1">Antenna</div>
                  <div class="text-sm font-bold" :class="gnssData.antenna.status === 'OK' ? 'text-emerald-700' : 'text-red-700'">
                    {{ gnssData.antenna.status }}
                  </div>
                </div>
                <div class="text-center p-3 rounded-xl" :class="gnssData.jamming.detected ? 'bg-red-100 border border-red-200' : 'bg-emerald-100 border border-emerald-200'">
                  <div class="text-xs text-slate-600 mb-1">Jamming</div>
                  <div class="text-sm font-bold" :class="gnssData.jamming.detected ? 'text-red-700' : 'text-emerald-700'">
                    {{ gnssData.jamming.detected ? 'DETECTED' : 'NONE' }}
                  </div>
                </div>
              </div>
              <div class="grid grid-cols-3 gap-3">
                <div class="text-center p-3 bg-white rounded-lg border">
                  <div class="text-xs text-slate-600 mb-1">AGC</div>
                  <div class="text-sm font-bold text-slate-700">{{ gnssData.agc.level }}</div>
                </div>
                <div class="text-center p-3 bg-white rounded-lg border">
                  <div class="text-xs text-slate-600 mb-1">RF Noise</div>
                  <div class="text-sm font-bold text-slate-700">{{ gnssData.rfNoise.level }}dBm</div>
                </div>
                <div class="text-center p-3 bg-white rounded-lg border">
                  <div class="text-xs text-slate-600 mb-1">Temp</div>
                  <div class="text-sm font-bold text-slate-700">{{ gnssData.temperature.toFixed(1) }}°C</div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
      
      <!-- Sensor Data Row -->
      <div class="mb-6">
        <div class="grid gap-4 grid-cols-1 md:grid-cols-3">
          
          <!-- IMU -->
          <div class="bg-white rounded-xl border border-slate-200 p-4">
            <div class="flex items-center space-x-2 mb-3">
              <div class="w-6 h-6 bg-purple-500 rounded-lg flex items-center justify-center">
                <svg class="w-4 h-4 text-white" fill="currentColor" viewBox="0 0 24 24">
                  <path d="M12 2L15.5 8.5L22 12L15.5 15.5L12 22L8.5 15.5L2 12L8.5 8.5L12 2Z"/>
                </svg>
              </div>
              <div>
                <h3 class="font-bold text-slate-800">IMU</h3>
                <div class="text-xs text-emerald-600">{{ dataRates.imuRate }}</div>
              </div>
            </div>
            <div class="grid grid-cols-3 gap-2 text-xs">
              <div class="text-center">
                <div class="text-slate-500">Accel</div>
                <div class="font-mono text-red-600">{{ imuData.acceleration.x.toFixed(1) }}</div>
                <div class="font-mono text-red-600">{{ imuData.acceleration.y.toFixed(1) }}</div>
                <div class="font-mono text-red-600">{{ imuData.acceleration.z.toFixed(1) }}</div>
              </div>
              <div class="text-center">
                <div class="text-slate-500">Gyro</div>
                <div class="font-mono text-blue-600">{{ imuData.gyroscope.x.toFixed(2) }}</div>
                <div class="font-mono text-blue-600">{{ imuData.gyroscope.y.toFixed(2) }}</div>
                <div class="font-mono text-blue-600">{{ imuData.gyroscope.z.toFixed(2) }}</div>
              </div>
              <div class="text-center">
                <div class="text-slate-500">Mag</div>
                <div class="font-mono text-green-600">{{ imuData.magnetometer.x.toFixed(0) }}</div>
                <div class="font-mono text-green-600">{{ imuData.magnetometer.y.toFixed(0) }}</div>
                <div class="font-mono text-green-600">{{ imuData.magnetometer.z.toFixed(0) }}</div>
              </div>
            </div>
          </div>

          <!-- Camera -->
          <div class="bg-white rounded-xl border border-slate-200 p-4">
            <div class="flex items-center space-x-2 mb-3">
              <div class="w-6 h-6 bg-cyan-500 rounded-lg flex items-center justify-center">
                <svg class="w-4 h-4 text-white" fill="currentColor" viewBox="0 0 24 24">
                  <path d="M12,15A2,2 0 0,1 10,13A2,2 0 0,1 12,11A2,2 0 0,1 14,13A2,2 0 0,1 12,15M22,6H19L17.83,4.5C17.42,3.87 16.75,3.5 16,3.5H8C7.25,3.5 6.58,3.87 6.17,4.5L5,6H2A2,2 0 0,0 0,8V18A2,2 0 0,0 2,20H22A2,2 0 0,0 24,18V8A2,2 0 0,0 22,6M12,17A4,4 0 0,0 16,13A4,4 0 0,0 12,9A4,4 0 0,0 8,13A4,4 0 0,0 12,17Z"/>
                </svg>
              </div>
              <div>
                <h3 class="font-bold text-slate-800">Camera</h3>
                <div class="text-xs text-red-500 animate-pulse">Recording</div>
              </div>
            </div>
            <div class="space-y-2 text-xs">
              <div class="flex justify-between">
                <span class="text-slate-500">Duration:</span>
                <span class="font-mono">00:45:23</span>
              </div>
              <div class="flex justify-between">
                <span class="text-slate-500">Size:</span>
                <span class="font-mono">2.1 GB</span>
              </div>
              <div class="flex justify-between">
                <span class="text-slate-500">Format:</span>
                <span>1080p@30fps</span>
              </div>
            </div>
          </div>

          <!-- Encoder -->
          <div class="bg-white rounded-xl border border-slate-200 p-4">
            <div class="flex items-center space-x-2 mb-3">
              <div class="w-6 h-6 bg-orange-500 rounded-lg flex items-center justify-center">
                <svg class="w-4 h-4 text-white" fill="currentColor" viewBox="0 0 24 24">
                  <path d="M12,2A10,10 0 0,1 22,12A10,10 0 0,1 12,22A10,10 0 0,1 2,12A10,10 0 0,1 12,2M12,4A8,8 0 0,0 4,12A8,8 0 0,0 12,20A8,8 0 0,0 20,12A8,8 0 0,0 12,4Z"/>
                </svg>
              </div>
              <div>
                <h3 class="font-bold text-slate-800">Encoder</h3>
                <div class="text-xs text-emerald-600">Active</div>
              </div>
            </div>
            <div class="space-y-2 text-xs">
              <div class="flex justify-between">
                <span class="text-slate-500">Count:</span>
                <span class="font-mono">{{ encoderData.rawCount }}</span>
              </div>
              <div class="flex justify-between">
                <span class="text-slate-500">Direction:</span>
                <span>{{ encoderData.direction }}</span>
              </div>
              <div class="flex justify-between">
                <span class="text-slate-500">Rate:</span>
                <span class="font-mono">{{ encoderData.pulsesPerSecond }}/s</span>
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- System Status Row -->
      <div class="mb-6">
        <div class="grid gap-4 grid-cols-1 md:grid-cols-2">
          
          <!-- LoRa -->
          <div class="bg-white rounded-xl border border-slate-200 p-4">
            <div class="flex items-center space-x-2 mb-3">
              <div class="w-6 h-6 bg-blue-500 rounded-lg flex items-center justify-center">
                <svg class="w-4 h-4 text-white" fill="currentColor" viewBox="0 0 24 24">
                  <path d="M12 1L21.5 6.5L12 12L2.5 6.5L12 1ZM12 23L21.5 17.5L12 12L2.5 17.5L12 23Z"/>
                </svg>
              </div>
              <div>
                <h3 class="font-bold text-slate-800">LoRa</h3>
                <div class="text-xs text-blue-600">{{ loraData.mode }}</div>
              </div>
            </div>
            <div class="space-y-2 text-xs">
              <div class="flex justify-between">
                <span class="text-slate-500">Signal:</span>
                <span class="font-mono" :class="getSignalColor(loraData.signalStrength)">{{ loraData.signalStrength }}dBm</span>
              </div>
              <div class="flex justify-between">
                <span class="text-slate-500">Rate:</span>
                <span>{{ loraData.correctionRate }}</span>
              </div>
              <div class="grid grid-cols-2 gap-1 pt-1">
                <div class="text-center">
                  <div class="font-bold text-emerald-600">{{ (loraData.packetsReceived/1000).toFixed(1) }}k</div>
                  <div class="text-slate-500">RX</div>
                </div>
                <div class="text-center">
                  <div class="font-bold text-blue-600">{{ (loraData.packetsSent/1000).toFixed(1) }}k</div>
                  <div class="text-slate-500">TX</div>
                </div>
              </div>
            </div>
          </div>

          <!-- System Info (Combined) -->
          <div class="bg-white rounded-xl border border-slate-200 p-4">
            <div class="flex items-center space-x-2 mb-3">
              <div class="w-6 h-6 bg-emerald-500 rounded-lg flex items-center justify-center">
                <svg class="w-4 h-4 text-white" fill="currentColor" viewBox="0 0 24 24">
                  <path d="M12 2L13.09 8.26L22 9L13.09 9.74L12 16L10.91 9.74L2 9L10.91 8.26L12 2Z"/>
                </svg>
              </div>
              <div>
                <h3 class="font-bold text-slate-800">System</h3>
                <div class="text-xs text-emerald-600">Healthy • Recording</div>
              </div>
            </div>
            
            <!-- Combined system data in compact grid -->
            <div class="grid grid-cols-2 gap-3 text-xs">
              <!-- Power section -->
              <div class="space-y-1">
                <div class="flex justify-between">
                  <span class="text-slate-500">Battery:</span>
                  <span :class="getBatteryColor(powerStatus.batteryLevel)">{{ powerStatus.batteryLevel }}%</span>
                </div>
                <div class="w-full bg-slate-200 rounded-full h-1 mb-1">
                  <div class="h-1 rounded-full transition-all" 
                       :class="powerStatus.batteryLevel > 60 ? 'bg-emerald-500' : powerStatus.batteryLevel > 30 ? 'bg-yellow-500' : 'bg-red-500'"
                       :style="`width: ${powerStatus.batteryLevel}%`"></div>
                </div>
                <div class="flex justify-between">
                  <span class="text-slate-500">Draw:</span>
                  <span class="font-mono">{{ powerStatus.powerConsumption }}W</span>
                </div>
                <div class="flex justify-between">
                  <span class="text-slate-500">Runtime:</span>
                  <span>{{ powerStatus.estimatedRuntime }}</span>
                </div>
              </div>
              
              <!-- System + Files section -->
              <div class="space-y-1">
                <div class="flex justify-between">
                  <span class="text-slate-500">CPU:</span>
                  <span :class="getUsageColor(systemHealth.cpuUsage)">{{ systemHealth.cpuUsage.toFixed(1) }}%</span>
                </div>
                <div class="flex justify-between">
                  <span class="text-slate-500">RAM:</span>
                  <span :class="getUsageColor(systemHealth.memoryUsage)">{{ systemHealth.memoryUsage.toFixed(1) }}%</span>
                </div>
                <div class="flex justify-between">
                  <span class="text-slate-500">Storage:</span>
                  <span :class="getUsageColor(systemHealth.storageUsed)">{{ systemHealth.storageUsed }}%</span>
                </div>
                <div class="flex justify-between">
                  <span class="text-slate-500">Temp:</span>
                  <span class="text-orange-600">{{ systemHealth.temperature }}°C</span>
                </div>
              </div>
            </div>
            
            <!-- File Management section -->
            <div class="mt-3 pt-2 border-t border-slate-100">
              <div class="grid grid-cols-2 gap-3 text-xs">
                <div class="space-y-1">
                  <div class="flex justify-between">
                    <span class="text-slate-500">Session:</span>
                    <span class="font-mono">{{ fileManagement.currentSession.split('_')[2] }}</span>
                  </div>
                  <div class="flex justify-between">
                    <span class="text-slate-500">Files:</span>
                    <span>{{ fileManagement.filesCreated }}</span>
                  </div>
                </div>
                <div class="space-y-1">
                  <div class="flex justify-between">
                    <span class="text-slate-500">Size:</span>
                    <span>{{ fileManagement.totalSize }}</span>
                  </div>
                  <div class="flex justify-between">
                    <span class="text-slate-500">Available:</span>
                    <span>{{ fileManagement.availableSpace }}</span>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
      


    </main>
  </div>
</template>

<style scoped></style>
