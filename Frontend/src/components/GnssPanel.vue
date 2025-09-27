<script setup>
import Card from './common/Card.vue'
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
  <Card
    title="GNSS"
    icon-color="bg-gray-500"
  >
    
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
  </Card>
</template>