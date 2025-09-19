<script setup>
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
</template>