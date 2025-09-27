<script setup>
import Card from '../common/Card.vue'
import SatelliteHealthChart from '../SatelliteHealthChart.vue'

const props = defineProps({
  gnssData: {
    type: Object,
    required: true
  }
})
</script>

<template>
  <Card
    title="Satellite Health"
    icon-color="bg-gray-500"
  >

    <!-- Constellation Summary -->
    <div class="grid grid-cols-4 gap-3 mb-6">
      <div class="text-center p-3 bg-gray-50 rounded-xl border border-gray-200">
        <div class="text-sm text-slate-600 mb-1">GPS</div>
        <div class="text-base font-bold" :class="gnssData.constellations.gps.used !== null ? 'text-gray-800' : 'text-slate-400'">{{ gnssData.constellations.gps.used !== null ? gnssData.constellations.gps.used + '/' + gnssData.constellations.gps.tracked : '—' }}</div>
      </div>
      <div class="text-center p-3 bg-gray-100 rounded-xl border border-gray-200">
        <div class="text-sm text-slate-600 mb-1">GLONASS</div>
        <div class="text-base font-bold" :class="gnssData.constellations.glonass.used !== null ? 'text-gray-800' : 'text-slate-400'">{{ gnssData.constellations.glonass.used !== null ? gnssData.constellations.glonass.used + '/' + gnssData.constellations.glonass.tracked : '—' }}</div>
      </div>
      <div class="text-center p-3 bg-slate-50 rounded-xl border border-gray-200">
        <div class="text-sm text-slate-600 mb-1">Galileo</div>
        <div class="text-base font-bold" :class="gnssData.constellations.galileo.used !== null ? 'text-gray-800' : 'text-slate-400'">{{ gnssData.constellations.galileo.used !== null ? gnssData.constellations.galileo.used + '/' + gnssData.constellations.galileo.tracked : '—' }}</div>
      </div>
      <div class="text-center p-3 bg-slate-100 rounded-xl border border-gray-200">
        <div class="text-sm text-slate-600 mb-1">BeiDou</div>
        <div class="text-base font-bold" :class="gnssData.constellations.beidou.used !== null ? 'text-gray-800' : 'text-slate-400'">{{ gnssData.constellations.beidou.used !== null ? gnssData.constellations.beidou.used + '/' + gnssData.constellations.beidou.tracked : '—' }}</div>
      </div>
    </div>

    <!-- Satellite Health Chart -->
    <SatelliteHealthChart :satellites="gnssData.satellites" />
  </Card>
</template>