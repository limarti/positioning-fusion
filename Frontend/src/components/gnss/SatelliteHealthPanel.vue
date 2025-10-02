<template>
  <Card title="Satellite Health">

    <!-- Constellation Summary -->
    <div class="grid grid-cols-4 gap-3 mb-6">
      <div class="text-center p-3 bg-gray-50 rounded-xl border border-gray-200">
        <div class="text-sm text-slate-600 mb-1">
          GPS
        </div>
        <div class="text-base font-bold" :class="gnssState.gnssData.constellations.gps.used !== null ? 'text-gray-800' : 'text-slate-400'">
          {{ gnssState.gnssData.constellations.gps.used !== null ? gnssState.gnssData.constellations.gps.used + '/' + gnssState.gnssData.constellations.gps.tracked : '—' }}
        </div>
      </div>
      <div class="text-center p-3 bg-gray-100 rounded-xl border border-gray-200">
        <div class="text-sm text-slate-600 mb-1">
          GLONASS
        </div>
        <div class="text-base font-bold" :class="gnssState.gnssData.constellations.glonass.used !== null ? 'text-gray-800' : 'text-slate-400'">
          {{ gnssState.gnssData.constellations.glonass.used !== null ? gnssState.gnssData.constellations.glonass.used + '/' + gnssState.gnssData.constellations.glonass.tracked : '—' }}
        </div>
      </div>
      <div class="text-center p-3 bg-slate-50 rounded-xl border border-gray-200">
        <div class="text-sm text-slate-600 mb-1">
          Galileo
        </div>
        <div class="text-base font-bold" :class="gnssState.gnssData.constellations.galileo.used !== null ? 'text-gray-800' : 'text-slate-400'">
          {{ gnssState.gnssData.constellations.galileo.used !== null ? gnssState.gnssData.constellations.galileo.used + '/' + gnssState.gnssData.constellations.galileo.tracked : '—' }}
        </div>
      </div>
      <div class="text-center p-3 bg-slate-100 rounded-xl border border-gray-200">
        <div class="text-sm text-slate-600 mb-1">
          BeiDou
        </div>
        <div class="text-base font-bold" :class="gnssState.gnssData.constellations.beidou.used !== null ? 'text-gray-800' : 'text-slate-400'">
          {{ gnssState.gnssData.constellations.beidou.used !== null ? gnssState.gnssData.constellations.beidou.used + '/' + gnssState.gnssData.constellations.beidou.tracked : '—' }}
        </div>
      </div>
    </div>

    <!-- Satellite Health Chart -->
    <SatelliteHealthChart :satellites="gnssState.gnssData.satellites" />
  </Card>
</template>

<script setup>
  import Card from '../common/Card.vue';
  import SatelliteHealthChart from '../SatelliteHealthChart.vue';
  import { useGnssData } from '@/composables/useGnssData';

  // Get data from composable
  const { state: gnssState } = useGnssData();
</script>