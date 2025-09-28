<script setup>
import { inject } from 'vue'
import SystemPanel from '@/components/SystemPanel.vue'

// Inject data from App.vue
const systemHealth = inject('systemHealth')
const powerStatus = inject('powerStatus')
const dataRates = inject('dataRates')
</script>

<template>
  <SystemPanel :systemHealth="systemHealth" :powerStatus="powerStatus" :dataRates="dataRates" />
</template>