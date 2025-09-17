<script setup>
import SatelliteHealthChart from './SatelliteHealthChart.vue'

const props = defineProps({
  gnssData: {
    type: Object,
    required: true
  },
  dataRates: {
    type: Object,
    required: true
  }
})

const formatAccuracy = (meters) => {
  if (meters === null || meters === undefined) return '—'

  // Convert to millimeters
  const mm = meters * 1000

  // If >= 1000mm (1m), show in meters
  if (mm >= 1000) {
    return (mm / 1000).toFixed(1) + 'm'
  }
  // If >= 10mm (1cm), show in centimeters
  else if (mm >= 10) {
    return (mm / 10).toFixed(0) + 'cm'
  }
  // Otherwise show in millimeters
  else {
    return mm.toFixed(0) + 'mm'
  }
}
</script>

<template>
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
        <div class="flex-1">
          <h1 class="text-2xl font-bold">GNSS System</h1>
          <p class="text-slate-300 text-sm">Global Navigation Satellite System</p>
        </div>
        <!-- Connection Status Indicator -->
        <div class="flex items-center space-x-2">
          <div class="w-3 h-3 rounded-full" :class="gnssData.connected ? 'bg-green-400 animate-pulse' : 'bg-red-400'"></div>
          <span class="text-sm font-medium" :class="gnssData.connected ? 'text-green-300' : 'text-red-300'">
            {{ gnssData.connected ? 'Connected' : 'Offline' }}
          </span>
        </div>
      </div>
    </div>
    
    <!-- GNSS Status Summary -->
    <div class="bg-gradient-to-r from-slate-700 to-slate-800 text-white px-6 py-4 border-x-2 border-slate-600">
      <div class="flex items-center justify-between mb-4">
        <div class="flex items-center space-x-4">
          <div>
            <div class="text-sm text-slate-300">Current Position</div>
            <div class="text-lg font-mono">
              {{ gnssData.latitude !== null && gnssData.longitude !== null
                  ? `${gnssData.latitude.toFixed(7)}°, ${gnssData.longitude.toFixed(7)}°`
                  : 'Waiting for GNSS fix...' }}
            </div>
            <div v-if="gnssData.altitude !== null" class="text-sm text-slate-300 font-mono mt-1">
              Altitude: {{ gnssData.altitude.toFixed(2) }}m
            </div>
          </div>
        </div>
        <div class="flex items-center space-x-3">
          <div class="w-3 h-3 rounded-full animate-pulse" :class="gnssData.fixType === 'RTK Fixed' ? 'bg-emerald-400' : gnssData.fixType === 'RTK Float' ? 'bg-yellow-400' : 'bg-red-400'"></div>
          <span class="text-lg font-bold px-4 py-2 rounded-xl" :class="gnssData.fixType === 'RTK Fixed' ? 'bg-emerald-500/20 text-emerald-300' : gnssData.fixType === 'RTK Float' ? 'bg-yellow-500/20 text-yellow-300' : 'bg-red-500/20 text-red-300'">{{ gnssData.fixType }}</span>
        </div>
      </div>
      
      <!-- Core Health Summary -->
      <div class="grid grid-cols-2 md:grid-cols-8 gap-4 text-center">
        <div>
          <div class="text-xs text-slate-400 mb-1">hAcc</div>
          <div class="text-lg font-bold" :class="gnssData.hAcc !== null ? 'text-emerald-300' : 'text-slate-400'">{{ formatAccuracy(gnssData.hAcc) }}</div>
        </div>
        <div>
          <div class="text-xs text-slate-400 mb-1">vAcc</div>
          <div class="text-lg font-bold" :class="gnssData.vAcc !== null ? 'text-emerald-300' : 'text-slate-400'">{{ formatAccuracy(gnssData.vAcc) }}</div>
        </div>
        <div>
          <div class="text-xs text-slate-400 mb-1">HDOP</div>
          <div class="text-lg font-bold" :class="gnssData.hdop !== null ? 'text-amber-300' : 'text-slate-400'">{{ gnssData.hdop !== null ? gnssData.hdop.toFixed(2) : '—' }}</div>
        </div>
        <div>
          <div class="text-xs text-slate-400 mb-1">VDOP</div>
          <div class="text-lg font-bold" :class="gnssData.vdop !== null ? 'text-amber-300' : 'text-slate-400'">{{ gnssData.vdop !== null ? gnssData.vdop.toFixed(2) : '—' }}</div>
        </div>
        <div>
          <div class="text-xs text-slate-400 mb-1">PDOP</div>
          <div class="text-lg font-bold" :class="gnssData.pdop !== null ? 'text-amber-300' : 'text-slate-400'">{{ gnssData.pdop !== null ? gnssData.pdop.toFixed(2) : '—' }}</div>
        </div>
        <div>
          <div class="text-xs text-slate-400 mb-1">Satellites</div>
          <div class="text-lg font-bold" :class="gnssData.satellitesUsed !== null ? 'text-blue-300' : 'text-slate-400'">{{ gnssData.satellitesUsed !== null ? gnssData.satellitesUsed + '/' + gnssData.satellitesTracked : '—' }}</div>
        </div>
        <div>
          <div class="text-xs text-slate-400 mb-1 flex items-center justify-center">
            <svg class="w-3 h-3 mr-1" fill="currentColor" viewBox="0 0 24 24">
              <path d="M7,14L12,9L17,14H7Z"/>
            </svg>
            Data In
          </div>
          <div class="text-lg font-bold" :class="dataRates.kbpsGnssIn !== null && dataRates.kbpsGnssIn !== undefined ? 'text-blue-300' : 'text-slate-400'">{{ dataRates.kbpsGnssIn !== null && dataRates.kbpsGnssIn !== undefined ? dataRates.kbpsGnssIn.toFixed(1) + ' kbps' : '—' }}</div>
        </div>
        <div>
          <div class="text-xs text-slate-400 mb-1 flex items-center justify-center">
            <svg class="w-3 h-3 mr-1" fill="currentColor" viewBox="0 0 24 24">
              <path d="M7,10L12,15L17,10H7Z"/>
            </svg>
            Data Out
          </div>
          <div class="text-lg font-bold" :class="dataRates.kbpsGnssOut !== null && dataRates.kbpsGnssOut !== undefined ? 'text-blue-300' : 'text-slate-400'">{{ dataRates.kbpsGnssOut !== null && dataRates.kbpsGnssOut !== undefined ? dataRates.kbpsGnssOut.toFixed(1) + ' kbps' : '—' }}</div>
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
            <div class="ml-auto text-sm font-semibold" :class="dataRates.gnssRate !== null ? 'text-emerald-600' : 'text-slate-400'">{{ dataRates.gnssRate !== null ? dataRates.gnssRate : '—' }}</div>
          </div>

          <!-- Constellation Summary -->
          <div class="grid grid-cols-4 gap-3 mb-6">
            <div class="text-center p-3 bg-blue-50 rounded-xl">
              <div class="text-xs text-slate-600 mb-1">GPS</div>
              <div class="text-lg font-bold" :class="gnssData.constellations.gps.used !== null ? 'text-blue-700' : 'text-slate-400'">{{ gnssData.constellations.gps.used !== null ? gnssData.constellations.gps.used + '/' + gnssData.constellations.gps.tracked : '—' }}</div>
            </div>
            <div class="text-center p-3 bg-red-50 rounded-xl">
              <div class="text-xs text-slate-600 mb-1">GLONASS</div>
              <div class="text-lg font-bold" :class="gnssData.constellations.glonass.used !== null ? 'text-red-700' : 'text-slate-400'">{{ gnssData.constellations.glonass.used !== null ? gnssData.constellations.glonass.used + '/' + gnssData.constellations.glonass.tracked : '—' }}</div>
            </div>
            <div class="text-center p-3 bg-purple-50 rounded-xl">
              <div class="text-xs text-slate-600 mb-1">Galileo</div>
              <div class="text-lg font-bold" :class="gnssData.constellations.galileo.used !== null ? 'text-purple-700' : 'text-slate-400'">{{ gnssData.constellations.galileo.used !== null ? gnssData.constellations.galileo.used + '/' + gnssData.constellations.galileo.tracked : '—' }}</div>
            </div>
            <div class="text-center p-3 bg-yellow-50 rounded-xl">
              <div class="text-xs text-slate-600 mb-1">BeiDou</div>
              <div class="text-lg font-bold" :class="gnssData.constellations.beidou.used !== null ? 'text-yellow-700' : 'text-slate-400'">{{ gnssData.constellations.beidou.used !== null ? gnssData.constellations.beidou.used + '/' + gnssData.constellations.beidou.tracked : '—' }}</div>
            </div>
          </div>

          <!-- Satellite Health Chart -->
          <SatelliteHealthChart :satellites="gnssData.satellites" />
        </div>
        <!-- Corrections Subsection (Conditional) -->
        <div v-if="gnssData.corrections?.active" class="bg-white/90 backdrop-blur-sm rounded-2xl border-2 border-slate-200 p-4">
          <div class="flex items-center justify-between mb-4">
            <div class="flex items-center space-x-3">
              <svg class="w-6 h-6 text-purple-600" fill="currentColor" viewBox="0 0 24 24">
                <path d="M17.65,6.35C16.2,4.9 14.21,4 12,4A8,8 0 0,0 4,12A8,8 0 0,0 12,20C15.73,20 18.84,17.45 19.73,14H17.65C16.83,16.33 14.61,18 12,18A6,6 0 0,1 6,12A6,6 0 0,1 12,6C13.66,6 15.14,6.69 16.22,7.78L13,11H20V4L17.65,6.35Z"/>
              </svg>
              <h2 class="text-xl font-bold text-slate-800">Corrections</h2>
              <div class="text-sm font-semibold" :class="dataRates.correctionRate !== null ? 'text-purple-600' : 'text-slate-400'">{{ dataRates.correctionRate !== null ? dataRates.correctionRate : '—' }}</div>
            </div>
            <span class="text-sm font-bold px-3 py-1 rounded-lg"
                  :class="gnssData.corrections?.connected ? 'bg-emerald-100 text-emerald-700' : 'bg-red-100 text-red-700'">
              {{ gnssData.corrections?.connected ? 'ACTIVE' : 'OFFLINE' }}
            </span>
          </div>

          <div class="grid grid-cols-2 gap-4 mb-6">
            <div class="text-center p-4 bg-purple-50 rounded-xl">
              <div class="text-xs text-slate-600 mb-1">Source Type</div>
              <div class="text-lg font-bold" :class="gnssData.corrections?.sourceType !== null ? 'text-purple-700' : 'text-slate-400'">{{ gnssData.corrections?.sourceType !== null ? gnssData.corrections?.sourceType : '—' }}</div>
            </div>
            <div class="text-center p-4 bg-indigo-50 rounded-xl">
              <div class="text-xs text-slate-600 mb-1">Data Age</div>
              <div class="text-lg font-bold" :class="gnssData.corrections?.dataAge !== null ? 'text-indigo-700' : 'text-slate-400'">{{ gnssData.corrections?.dataAge !== null ? gnssData.corrections?.dataAge.toFixed(1) + 's' : '—' }}</div>
            </div>
          </div>

          <div class="space-y-2">
            <div class="flex justify-between p-3 bg-slate-50 rounded-lg">
              <span class="text-slate-600">Base Station ID:</span>
              <span class="font-bold font-mono" :class="gnssData.corrections?.baseStationId !== null ? '' : 'text-slate-400'">{{ gnssData.corrections?.baseStationId !== null ? gnssData.corrections?.baseStationId : '—' }}</span>
            </div>
            <div class="flex justify-between p-3 bg-slate-50 rounded-lg">
              <span class="text-slate-600">Message Types:</span>
              <span class="font-bold font-mono text-xs" :class="gnssData.corrections?.messageTypes !== null ? '' : 'text-slate-400'">{{ gnssData.corrections?.messageTypes !== null ? gnssData.corrections?.messageTypes.join(', ') : '—' }}</span>
            </div>
            <div class="p-3 bg-slate-50 rounded-lg">
              <div class="text-xs text-slate-600 mb-2">Signal Quality</div>
              <div class="grid grid-cols-2 gap-2 text-xs">
                <div class="text-center">
                  <div class="font-mono font-semibold" :class="gnssData.corrections?.signalStrength !== null ? '' : 'text-slate-400'">{{ gnssData.corrections?.signalStrength !== null ? gnssData.corrections?.signalStrength + 'dBm' : '—' }}</div>
                  <div class="text-slate-500">Signal</div>
                </div>
                <div class="text-center">
                  <div class="font-mono font-semibold" :class="gnssData.corrections?.throughput !== null ? '' : 'text-slate-400'">{{ gnssData.corrections?.throughput !== null ? gnssData.corrections?.throughput + ' bps' : '—' }}</div>
                  <div class="text-slate-500">Throughput</div>
                </div>
              </div>
            </div>
          </div>
        </div>

        <!-- If Corrections not active, show basic status in its place -->
        <div v-else class="bg-slate-50 rounded-2xl border-2 border-slate-300 p-4">
          <div class="flex items-center space-x-3 mb-4">
            <svg class="w-6 h-6 text-slate-600" fill="currentColor" viewBox="0 0 24 24">
              <path d="M17.65,6.35C16.2,4.9 14.21,4 12,4A8,8 0 0,0 4,12A8,8 0 0,0 12,20C15.73,20 18.84,17.45 19.73,14H17.65C16.83,16.33 14.61,18 12,18A6,6 0 0,1 6,12A6,6 0 0,1 12,6C13.66,6 15.14,6.69 16.22,7.78L13,11H20V4L17.65,6.35Z"/>
            </svg>
            <h2 class="text-xl font-bold text-slate-800">No Corrections</h2>
          </div>

          <div class="grid grid-cols-1 gap-4">
            <div class="flex justify-between p-3 bg-white rounded-lg border">
              <span class="text-slate-600">Status:</span>
              <span class="font-bold text-slate-800">Standalone Mode</span>
            </div>
            <div class="flex justify-between p-3 bg-white rounded-lg border">
              <span class="text-slate-600">Precision:</span>
              <span class="font-bold font-mono text-slate-600">Standard GNSS</span>
            </div>
          </div>
        </div>
      </div>

      <!-- RTK Quality Section -->
      <div v-if="gnssData.rtk.active" class="mb-6">
        <div class="bg-white/90 backdrop-blur-sm rounded-2xl border-2 border-slate-200 p-4">
        <div class="flex items-center justify-between mb-4">
          <div class="flex items-center space-x-3">
            <svg class="w-6 h-6 text-emerald-600" fill="currentColor" viewBox="0 0 24 24">
              <path d="M12,2A3,3 0 0,1 15,5V11A3,3 0 0,1 12,14A3,3 0 0,1 9,11V5A3,3 0 0,1 12,2M19,11C19,14.53 16.39,17.44 13,17.93V21H11V17.93C7.61,17.44 5,14.53 5,11H7A5,5 0 0,0 12,16A5,5 0 0,0 17,11H19Z"/>
            </svg>
            <h2 class="text-xl font-bold text-slate-800">RTK Quality</h2>
            <div class="text-sm font-semibold" :class="dataRates.correctionRate !== null ? 'text-emerald-600' : 'text-slate-400'">{{ dataRates.correctionRate !== null ? dataRates.correctionRate : '—' }}</div>
          </div>
          <span class="text-sm font-bold px-3 py-1 rounded-lg" 
                :class="gnssData.rtkMode === 'Fixed' ? 'bg-emerald-100 text-emerald-700' : 'bg-yellow-100 text-yellow-700'">
            {{ gnssData.rtkMode }}
          </span>
        </div>
        
        <div class="grid grid-cols-2 gap-4 mb-6">
          <div class="text-center p-4 bg-emerald-50 rounded-xl">
            <div class="text-xs text-slate-600 mb-1">AR Ratio</div>
            <div class="text-2xl font-bold" :class="gnssData.rtk.arRatio !== null ? 'text-emerald-700' : 'text-slate-400'">{{ gnssData.rtk.arRatio !== null ? gnssData.rtk.arRatio.toFixed(1) : '—' }}</div>
          </div>
          <div class="text-center p-4 bg-blue-50 rounded-xl">
            <div class="text-xs text-slate-600 mb-1">Correction Age</div>
            <div class="text-2xl font-bold" :class="gnssData.rtk.correctionAge !== null ? 'text-blue-700' : 'text-slate-400'">{{ gnssData.rtk.correctionAge !== null ? gnssData.rtk.correctionAge.toFixed(1) + 's' : '—' }}</div>
          </div>
        </div>
        
        <div class="space-y-2">
          <div class="flex justify-between p-3 bg-slate-50 rounded-lg">
            <span class="text-slate-600">Baseline Length:</span>
            <span class="font-bold" :class="gnssData.rtk.baselineLength !== null ? '' : 'text-slate-400'">{{ gnssData.rtk.baselineLength !== null ? gnssData.rtk.baselineLength.toFixed(0) + 'm' : '—' }}</span>
          </div>
          <div class="p-3 bg-slate-50 rounded-lg">
            <div class="text-xs text-slate-600 mb-2">Relative Accuracy</div>
            <div class="grid grid-cols-3 gap-2 text-xs">
              <div class="text-center">
                <div class="font-mono font-semibold" :class="gnssData.rtk.relativeAccuracy.north !== null ? '' : 'text-slate-400'">{{ gnssData.rtk.relativeAccuracy.north !== null ? gnssData.rtk.relativeAccuracy.north.toFixed(3) + 'm' : '—' }}</div>
                <div class="text-slate-500">North</div>
              </div>
              <div class="text-center">
                <div class="font-mono font-semibold" :class="gnssData.rtk.relativeAccuracy.east !== null ? '' : 'text-slate-400'">{{ gnssData.rtk.relativeAccuracy.east !== null ? gnssData.rtk.relativeAccuracy.east.toFixed(3) + 'm' : '—' }}</div>
                <div class="text-slate-500">East</div>
              </div>
              <div class="text-center">
                <div class="font-mono font-semibold" :class="gnssData.rtk.relativeAccuracy.down !== null ? '' : 'text-slate-400'">{{ gnssData.rtk.relativeAccuracy.down !== null ? gnssData.rtk.relativeAccuracy.down.toFixed(3) + 'm' : '—' }}</div>
                <div class="text-slate-500">Down</div>
              </div>
            </div>
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
              <span class="font-bold font-mono" :class="gnssData.tAcc !== null ? 'text-slate-800' : 'text-slate-400'">{{ gnssData.tAcc !== null ? gnssData.tAcc + 'ns' : '—' }}</span>
            </div>
            <div class="flex justify-between p-3 bg-white rounded-lg border">
              <span class="text-slate-600">Solution Latency:</span>
              <span class="font-bold font-mono" :class="gnssData.solutionLatency !== null ? 'text-slate-800' : 'text-slate-400'">{{ gnssData.solutionLatency !== null ? gnssData.solutionLatency + 'ms' : '—' }}</span>
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
              <div class="text-sm font-bold" :class="gnssData.antenna.status !== null ? (gnssData.antenna.status === 'OK' ? 'text-emerald-700' : 'text-red-700') : 'text-slate-400'">
                {{ gnssData.antenna.status !== null ? gnssData.antenna.status : '—' }}
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
              <div class="text-sm font-bold" :class="gnssData.agc.level !== null ? 'text-slate-700' : 'text-slate-400'">{{ gnssData.agc.level !== null ? gnssData.agc.level : '—' }}</div>
            </div>
            <div class="text-center p-3 bg-white rounded-lg border">
              <div class="text-xs text-slate-600 mb-1">RF Noise</div>
              <div class="text-sm font-bold" :class="gnssData.rfNoise.level !== null ? 'text-slate-700' : 'text-slate-400'">{{ gnssData.rfNoise.level !== null ? gnssData.rfNoise.level + 'dBm' : '—' }}</div>
            </div>
            <div class="text-center p-3 bg-white rounded-lg border">
              <div class="text-xs text-slate-600 mb-1">Temp</div>
              <div class="text-sm font-bold" :class="gnssData.temperature !== null ? 'text-slate-700' : 'text-slate-400'">{{ gnssData.temperature !== null ? gnssData.temperature.toFixed(1) + '°C' : '—' }}</div>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>