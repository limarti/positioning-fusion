<script setup>
import GnssStatus from './gnss/GnssStatus.vue'
import SatelliteHealthPanel from './gnss/SatelliteHealthPanel.vue'
import RtkPanel from './gnss/RtkPanel.vue'
import PositionScatterPlot from './gnss/PositionScatterPlot.vue'
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
    <GnssStatus :gnssData="gnssData" />
    
    <!-- GNSS Subsections -->
    <div class="space-y-4">
      <!-- Centered Masonry Layout for All GNSS Cards -->
      <div class="columns-1 lg:columns-2 gap-6 space-y-6 mx-auto">
        <!-- Satellite Health Subsection -->
        <SatelliteHealthPanel :gnssData="gnssData" />
        
        <!-- Unified RTK Panel -->
        <RtkPanel :gnssData="gnssData" />
        
        <!-- Position Scatter Plot -->
        <PositionScatterPlot :gnssData="gnssData" />
        
        <!-- UBX Message Rates Subsection -->
        <MessageRatesPanel :messageRates="messageRates" />

      </div>
    </div>
  </div>
</template>