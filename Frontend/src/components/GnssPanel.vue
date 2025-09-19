<script setup>
import SatelliteHealthChart from './SatelliteHealthChart.vue'
import MessageRatesPanel from './MessageRatesPanel.vue'

const props = defineProps({
  gnssData: {
    type: Object,
    required: true
  },
  dataRates: {
    type: Object,
    required: true
  },
  messageRates: {
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

const getFixTypeClass = (fixType) => {
  if (!fixType || fixType === 'No Fix') return 'text-slate-400'
  if (fixType === 'RTK Fixed') return 'text-emerald-600'
  if (fixType === 'RTK Float') return 'text-yellow-600'
  if (fixType === 'GNSS Fixed') return 'text-blue-600'
  if (fixType === 'Acquiring') return 'text-orange-600'
  return 'text-slate-600'
}
</script>

<template>
  <!-- GNSS System -->
  <div class="bg-white rounded-xl border border-slate-200 p-4">
    <!-- GNSS Section Header -->
    <div class="flex items-center space-x-2 mb-3">
      <div class="w-6 h-6 bg-emerald-500 rounded-lg flex items-center justify-center">
        <svg class="w-4 h-4 text-white" fill="currentColor" viewBox="0 0 24 24">
          <path d="M12 2L13.09 8.26L22 9L13.09 9.74L12 16L10.91 9.74L2 9L10.91 8.26L12 2Z"/>
        </svg>
      </div>
      <div class="flex-1">
        <h3 class="font-bold text-slate-800">GNSS</h3>
        <div class="text-sm" :class="gnssData.connected ? 'text-emerald-600' : 'text-slate-400'">
          {{ gnssData.connected ? 'Connected' : 'Offline' }}
        </div>
      </div>
    </div>
    
    <!-- GNSS Status Summary -->
    <div class="bg-slate-100 rounded-lg border border-slate-200 px-4 py-3 mb-4">
      <div class="flex items-center justify-between mb-3">
        <div class="flex items-center space-x-4">
          <div>
            <div class="text-sm text-slate-600">Current Position</div>
            <div class="text-base font-mono text-slate-800">
              {{ gnssData.latitude !== null && gnssData.longitude !== null
                  ? `${gnssData.latitude.toFixed(7)}°, ${gnssData.longitude.toFixed(7)}°`
                  : 'Waiting for GNSS fix...' }}
            </div>
            <div v-if="gnssData.altitude !== null" class="text-sm text-slate-600 font-mono mt-1">
              Altitude: {{ gnssData.altitude.toFixed(2) }}m
            </div>
          </div>
        </div>
        <div class="text-right">
          <div class="text-sm text-slate-600">Fix Type</div>
          <div class="text-base font-bold" :class="getFixTypeClass(gnssData.fixType)">
            {{ gnssData.fixType || 'No Fix' }}
          </div>
        </div>
      </div>
      
      <!-- Core Health Summary -->
      <div class="grid grid-cols-2 md:grid-cols-8 gap-4 text-center text-sm">
        <div>
          <div class="text-sm text-slate-500 mb-1">hAcc</div>
          <div class="text-base font-bold" :class="gnssData.hAcc !== null ? 'text-emerald-600' : 'text-slate-400'">{{ formatAccuracy(gnssData.hAcc) }}</div>
        </div>
        <div>
          <div class="text-sm text-slate-500 mb-1">vAcc</div>
          <div class="text-base font-bold" :class="gnssData.vAcc !== null ? 'text-emerald-600' : 'text-slate-400'">{{ formatAccuracy(gnssData.vAcc) }}</div>
        </div>
        <div>
          <div class="text-sm text-slate-500 mb-1">HDOP</div>
          <div class="text-base font-bold" :class="gnssData.hdop !== null ? 'text-amber-600' : 'text-slate-400'">{{ gnssData.hdop !== null ? gnssData.hdop.toFixed(2) : '—' }}</div>
        </div>
        <div>
          <div class="text-sm text-slate-500 mb-1">VDOP</div>
          <div class="text-base font-bold" :class="gnssData.vdop !== null ? 'text-amber-600' : 'text-slate-400'">{{ gnssData.vdop !== null ? gnssData.vdop.toFixed(2) : '—' }}</div>
        </div>
        <div>
          <div class="text-sm text-slate-500 mb-1">PDOP</div>
          <div class="text-base font-bold" :class="gnssData.pdop !== null ? 'text-amber-600' : 'text-slate-400'">{{ gnssData.pdop !== null ? gnssData.pdop.toFixed(2) : '—' }}</div>
        </div>
        <div>
          <div class="text-sm text-slate-500 mb-1">Satellites</div>
          <div class="text-base font-bold" :class="gnssData.satellitesUsed !== null ? 'text-blue-600' : 'text-slate-400'">{{ gnssData.satellitesUsed !== null ? gnssData.satellitesUsed + '/' + gnssData.satellitesTracked : '—' }}</div>
        </div>
        <div>
          <div class="text-sm text-slate-500 mb-1 flex items-center justify-center">
            <svg class="w-3 h-3 mr-1" fill="currentColor" viewBox="0 0 24 24">
              <path d="M7,14L12,9L17,14H7Z"/>
            </svg>
            Data In
          </div>
          <div class="text-base font-bold" :class="dataRates.kbpsGnssIn !== null && dataRates.kbpsGnssIn !== undefined ? 'text-blue-600' : 'text-slate-400'">{{ dataRates.kbpsGnssIn !== null && dataRates.kbpsGnssIn !== undefined ? dataRates.kbpsGnssIn.toFixed(1) + ' kbps' : '—' }}</div>
        </div>
        <div>
          <div class="text-sm text-slate-500 mb-1 flex items-center justify-center">
            <svg class="w-3 h-3 mr-1" fill="currentColor" viewBox="0 0 24 24">
              <path d="M7,10L12,15L17,10H7Z"/>
            </svg>
            Data Out
          </div>
          <div class="text-base font-bold" :class="dataRates.kbpsGnssOut !== null && dataRates.kbpsGnssOut !== undefined ? 'text-blue-600' : 'text-slate-400'">{{ dataRates.kbpsGnssOut !== null && dataRates.kbpsGnssOut !== undefined ? dataRates.kbpsGnssOut.toFixed(1) + ' kbps' : '—' }}</div>
        </div>
      </div>
    </div>
    
    <!-- GNSS Subsections -->
    <div class="space-y-4">
      <!-- Centered Masonry Layout for All GNSS Cards -->
      <div class="columns-1 lg:columns-2 gap-6 space-y-6 mx-auto">
        <!-- Satellite Health Subsection -->
        <div class="bg-white rounded-xl border border-slate-200 p-4 break-inside-avoid mb-6">
          <div class="flex items-center space-x-3 mb-4">
            <svg class="w-6 h-6 text-blue-600" fill="currentColor" viewBox="0 0 24 24">
              <path d="M12 2L13.09 8.26L22 9L13.09 9.74L12 16L10.91 9.74L2 9L10.91 8.26L12 2Z"/>
            </svg>
            <h2 class="text-lg font-bold text-slate-800">Satellite Health</h2>
            <div class="ml-auto text-sm font-semibold" :class="dataRates.gnssRate !== null ? 'text-emerald-600' : 'text-slate-400'">{{ dataRates.gnssRate !== null ? dataRates.gnssRate : '—' }}</div>
          </div>

          <!-- Constellation Summary -->
          <div class="grid grid-cols-4 gap-3 mb-6">
            <div class="text-center p-3 bg-blue-50 rounded-xl">
              <div class="text-sm text-slate-600 mb-1">GPS</div>
              <div class="text-base font-bold" :class="gnssData.constellations.gps.used !== null ? 'text-blue-700' : 'text-slate-400'">{{ gnssData.constellations.gps.used !== null ? gnssData.constellations.gps.used + '/' + gnssData.constellations.gps.tracked : '—' }}</div>
            </div>
            <div class="text-center p-3 bg-red-50 rounded-xl">
              <div class="text-sm text-slate-600 mb-1">GLONASS</div>
              <div class="text-base font-bold" :class="gnssData.constellations.glonass.used !== null ? 'text-red-700' : 'text-slate-400'">{{ gnssData.constellations.glonass.used !== null ? gnssData.constellations.glonass.used + '/' + gnssData.constellations.glonass.tracked : '—' }}</div>
            </div>
            <div class="text-center p-3 bg-purple-50 rounded-xl">
              <div class="text-sm text-slate-600 mb-1">Galileo</div>
              <div class="text-base font-bold" :class="gnssData.constellations.galileo.used !== null ? 'text-purple-700' : 'text-slate-400'">{{ gnssData.constellations.galileo.used !== null ? gnssData.constellations.galileo.used + '/' + gnssData.constellations.galileo.tracked : '—' }}</div>
            </div>
            <div class="text-center p-3 bg-yellow-50 rounded-xl">
              <div class="text-sm text-slate-600 mb-1">BeiDou</div>
              <div class="text-base font-bold" :class="gnssData.constellations.beidou.used !== null ? 'text-yellow-700' : 'text-slate-400'">{{ gnssData.constellations.beidou.used !== null ? gnssData.constellations.beidou.used + '/' + gnssData.constellations.beidou.tracked : '—' }}</div>
            </div>
          </div>

          <!-- Satellite Health Chart -->
          <SatelliteHealthChart :satellites="gnssData.satellites" />
        </div>
        
        <!-- RTK Quality Section (Rover Mode Only) -->
        <div v-if="gnssData.rtk.active" class="bg-white rounded-xl border border-slate-200 p-4 break-inside-avoid mb-6">
          <div class="flex items-center justify-between mb-4">
            <div class="flex items-center space-x-3">
              <svg class="w-6 h-6 text-emerald-600" fill="currentColor" viewBox="0 0 24 24">
                <path d="M12,2A3,3 0 0,1 15,5V11A3,3 0 0,1 12,14A3,3 0 0,1 9,11V5A3,3 0 0,1 12,2M19,11C19,14.53 16.39,17.44 13,17.93V21H11V17.93C7.61,17.44 5,14.53 5,11H7A5,5 0 0,0 12,16A5,5 0 0,0 17,11H19Z"/>
              </svg>
              <h2 class="text-lg font-bold text-slate-800">RTK Quality</h2>
              <div class="text-sm font-semibold" :class="dataRates.correctionRate !== null ? 'text-emerald-600' : 'text-slate-400'">{{ dataRates.correctionRate !== null ? dataRates.correctionRate : '—' }}</div>
            </div>
            <span class="text-sm font-bold px-3 py-1 rounded-lg" 
                  :class="gnssData.rtkMode === 'Fixed' ? 'bg-emerald-100 text-emerald-700' : 'bg-yellow-100 text-yellow-700'">
              {{ gnssData.rtkMode }}
            </span>
          </div>
          
          <div class="grid grid-cols-2 gap-4 mb-6">
            <div class="text-center p-4 bg-emerald-50 rounded-xl">
              <div class="text-sm text-slate-600 mb-1">AR Ratio</div>
              <div class="text-lg font-bold" :class="gnssData.rtk.arRatio !== null ? 'text-emerald-700' : 'text-slate-400'">{{ gnssData.rtk.arRatio !== null ? gnssData.rtk.arRatio.toFixed(1) : '—' }}</div>
            </div>
            <div class="text-center p-4 bg-blue-50 rounded-xl">
              <div class="text-sm text-slate-600 mb-1">Correction Age</div>
              <div class="text-lg font-bold" :class="gnssData.rtk.correctionAge !== null ? 'text-blue-700' : 'text-slate-400'">{{ gnssData.rtk.correctionAge !== null ? gnssData.rtk.correctionAge.toFixed(1) + 's' : '—' }}</div>
            </div>
          </div>
          
          <div class="space-y-2">
            <div class="flex justify-between p-3 bg-slate-50 rounded-lg">
              <span class="text-slate-600">Baseline Length:</span>
              <span class="font-bold" :class="gnssData.rtk.baselineLength !== null ? '' : 'text-slate-400'">{{ gnssData.rtk.baselineLength !== null ? gnssData.rtk.baselineLength.toFixed(0) + 'm' : '—' }}</span>
            </div>
            <div class="p-3 bg-slate-50 rounded-lg">
              <div class="text-sm text-slate-600 mb-2">Relative Accuracy</div>
              <div class="grid grid-cols-3 gap-2 text-sm">
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
        
        <!-- Corrections Panel -->
        <div v-if="gnssData.corrections.mode !== 'Disabled'" class="bg-white rounded-xl border border-slate-200 p-4 break-inside-avoid mb-6">
          <div class="flex items-center justify-between mb-4">
            <div class="flex items-center space-x-3">
              <svg class="w-6 h-6" :class="gnssData.corrections.mode === 'Send' ? 'text-blue-600' : 'text-purple-600'" fill="currentColor" viewBox="0 0 24 24">
                <path d="M12,3L14,8H10L12,3M12,8V22H11V8H10L12,3L14,8H13V22H12M4,12L6,9V15L4,12M20,12L18,9V15L20,12M8,14L9,11V17L8,14M16,14L15,11V17L16,14"/>
              </svg>
              <h2 class="text-lg font-bold text-slate-800">Corrections</h2>
              <div class="text-sm font-semibold" :class="dataRates.correctionRate !== null ? 'text-emerald-600' : 'text-slate-400'">{{ dataRates.correctionRate !== null ? dataRates.correctionRate : '—' }}</div>
            </div>
            
            <!-- Mode Badge -->
            <span class="text-sm font-bold px-3 py-1 rounded-lg"
                  :class="gnssData.corrections.mode === 'Send' ? 'bg-blue-100 text-blue-700' : 'bg-purple-100 text-purple-700'">
              {{ gnssData.corrections.mode === 'Send' ? 'SENDING' : 'RECEIVING' }}
            </span>
          </div>

          <!-- Survey-In Status (Base Station Mode) -->
          <div v-if="gnssData.corrections.mode === 'Send'" class="mb-4">
            <div class="bg-blue-50 rounded-xl p-4 border border-blue-200">
              <div class="flex items-center justify-between mb-3">
                <div class="text-sm font-semibold text-blue-800">Survey-In Status</div>
                <span class="text-xs font-bold px-2 py-1 rounded-lg"
                      :class="gnssData.surveyIn.valid ? 'bg-emerald-100 text-emerald-700' : 
                             gnssData.surveyIn.active ? 'bg-yellow-100 text-yellow-700' : 
                             'bg-slate-100 text-slate-700'">
                  {{ gnssData.surveyIn.valid ? '✅ COMPLETED' : 
                     gnssData.surveyIn.active ? '📍 ACTIVE' : '❌ INACTIVE' }}
                </span>
              </div>
              
              <div class="grid grid-cols-3 gap-3">
                <div class="text-center">
                  <div class="text-xs text-blue-600 mb-1">Duration</div>
                  <div class="font-bold text-blue-800">{{ gnssData.surveyIn.duration !== null ? gnssData.surveyIn.duration + 's' : '—' }}</div>
                </div>
                <div class="text-center">
                  <div class="text-xs text-blue-600 mb-1">Observations</div>
                  <div class="font-bold text-blue-800">{{ gnssData.surveyIn.observations !== null ? gnssData.surveyIn.observations : '—' }}</div>
                </div>
                <div class="text-center">
                  <div class="text-xs text-blue-600 mb-1">Accuracy</div>
                  <div class="font-bold text-blue-800">{{ gnssData.surveyIn.accuracyMm !== null ? (gnssData.surveyIn.accuracyMm / 1000).toFixed(2) + 'm' : '—' }}</div>
                </div>
              </div>
            </div>
          </div>

          <!-- LoRa Data Rates -->
          <div class="mb-4">
            <div class="bg-amber-50 rounded-xl p-4 border border-amber-200">
              <div class="text-sm font-semibold text-amber-800 mb-3">LoRa Radio</div>
              <div class="grid grid-cols-2 gap-3">
                <div class="text-center">
                  <div class="text-xs text-amber-600 mb-1 flex items-center justify-center">
                    <svg class="w-3 h-3 mr-1" fill="currentColor" viewBox="0 0 24 24">
                      <path d="M7,14L12,9L17,14H7Z"/>
                    </svg>
                    Data In
                  </div>
                  <div class="font-bold text-amber-800">{{ dataRates.kbpsLoRaIn !== null && dataRates.kbpsLoRaIn !== undefined ? dataRates.kbpsLoRaIn.toFixed(1) + ' kbps' : '—' }}</div>
                </div>
                <div class="text-center">
                  <div class="text-xs text-amber-600 mb-1 flex items-center justify-center">
                    <svg class="w-3 h-3 mr-1" fill="currentColor" viewBox="0 0 24 24">
                      <path d="M7,10L12,15L17,10H7Z"/>
                    </svg>
                    Data Out
                  </div>
                  <div class="font-bold text-amber-800">{{ dataRates.kbpsLoRaOut !== null && dataRates.kbpsLoRaOut !== undefined ? dataRates.kbpsLoRaOut.toFixed(1) + ' kbps' : '—' }}</div>
                </div>
              </div>
            </div>
          </div>

          <!-- Connection Status -->
          <div class="grid grid-cols-1 gap-3">
            <div class="flex justify-between p-3 bg-slate-50 rounded-lg">
              <span class="text-slate-600">Mode:</span>
              <span class="font-bold" :class="gnssData.corrections.mode === 'Send' ? 'text-blue-700' : 'text-purple-700'">
                {{ gnssData.corrections.mode === 'Send' ? 'Base Station (Sending)' : 'Rover (Receiving)' }}
              </span>
            </div>
          </div>
        </div>
        
        
        <!-- UBX Message Rates Subsection -->
        <MessageRatesPanel :messageRates="messageRates" />

      </div>
    </div>
  </div>
</template>