<script setup>
const props = defineProps({
  gnssData: {
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
        <div class="text-base font-bold text-indigo-600" >
          {{ gnssData.fixType || 'No Fix' }}
        </div>
      </div>
    </div>
    
    <!-- Core Health Summary -->
    <div class="grid grid-cols-2 md:grid-cols-6 gap-4 text-center text-sm">
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
    </div>
  </div>
</template>