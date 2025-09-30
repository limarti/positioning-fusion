<template>
  <div class="main-container">
    <!-- GNSS Status Summary -->
    <GnssStatus />

    <!-- GNSS Subpanels -->
    <div class="columns-1 lg:columns-2 gap-6 space-y-6">
      <!-- Satellite Health Subsection -->
      <div class="break-inside-avoid mb-6">
        <SatelliteHealthPanel />
      </div>

      <!-- Unified RTK Panel with Mode Selection -->
      <div class="break-inside-avoid mb-6">
        <RtkPanel />
      </div>

      <!-- Position Scatter Plot -->
      <div class="break-inside-avoid mb-6">
        <PositionScatterPlot />
      </div>

      <!-- UBX Message Rates Subsection -->
      <div class="break-inside-avoid mb-6">
        <MessageRatesPanel />
      </div>
    </div>
  </div>
</template>

<script setup>
  import GnssStatus from '@/components/gnss/GnssStatus.vue';
  import SatelliteHealthPanel from '@/components/gnss/SatelliteHealthPanel.vue';
  import RtkPanel from '@/components/gnss/RtkPanel.vue';
  import PositionScatterPlot from '@/components/gnss/PositionScatterPlot.vue';
  import MessageRatesPanel from '@/components/MessageRatesPanel.vue';
  import { useGnssData } from '@/composables/useGnssData';
  import { useSystemData } from '@/composables/useSystemData';

  // Get data from composables
  const { state: gnssState } = useGnssData();
  const { state: systemState } = useSystemData();
</script>