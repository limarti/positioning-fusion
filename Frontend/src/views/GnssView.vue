<script setup>
import { inject } from 'vue'
import GnssStatus from '@/components/gnss/GnssStatus.vue'
import SatelliteHealthPanel from '@/components/gnss/SatelliteHealthPanel.vue'
import RtkPanel from '@/components/gnss/RtkPanel.vue'
import PositionScatterPlot from '@/components/gnss/PositionScatterPlot.vue'
import MessageRatesPanel from '@/components/MessageRatesPanel.vue'

// Inject data from App.vue
const gnssData = inject('gnssData')
const dataRates = inject('dataRates')
const messageRates = inject('messageRates')
</script>

<template>
  <div class="main-container">
    <!-- GNSS Status Summary -->
    <GnssStatus :gnssData="gnssData" />

    <!-- GNSS Subpanels -->
    <div class="columns-1 lg:columns-2 gap-6 space-y-6">
      <!-- Satellite Health Subsection -->
      <div class="break-inside-avoid mb-6">
        <SatelliteHealthPanel :gnssData="gnssData" />
      </div>

      <!-- Unified RTK Panel with Mode Selection -->
      <div class="break-inside-avoid mb-6">
        <RtkPanel :gnssData="gnssData" :dataRates="dataRates" />
      </div>

      <!-- Position Scatter Plot -->
      <div class="break-inside-avoid mb-6">
        <PositionScatterPlot :gnssData="gnssData" />
      </div>

      <!-- UBX Message Rates Subsection -->
      <div class="break-inside-avoid mb-6">
        <MessageRatesPanel :messageRates="messageRates" />
      </div>
    </div>
  </div>
</template>