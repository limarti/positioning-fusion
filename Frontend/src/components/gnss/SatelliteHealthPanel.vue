<script setup>
import SatelliteHealthChart from '../SatelliteHealthChart.vue'

const props = defineProps({
  gnssData: {
    type: Object,
    required: true
  }
})
</script>

<template>
  <div class="bg-white rounded-xl border border-slate-200 p-4 break-inside-avoid mb-6">
    <div class="flex items-center space-x-3 mb-4">
      <svg class="w-6 h-6 text-blue-600" fill="currentColor" viewBox="0 0 24 24">
        <path d="M12 2L13.09 8.26L22 9L13.09 9.74L12 16L10.91 9.74L2 9L10.91 8.26L12 2Z"/>
      </svg>
      <h2 class="text-lg font-bold text-slate-800">Satellite Health</h2>
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
</template>