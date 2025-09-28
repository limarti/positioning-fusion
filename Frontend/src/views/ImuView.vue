<script setup>
import { inject } from 'vue'
import ImuPanel from '@/components/ImuPanel.vue'

// Inject data from App.vue
const imuData = inject('imuData')
const dataRates = inject('dataRates')
</script>

<template>
  <ImuPanel :imuData="imuData" :dataRates="dataRates" />
</template>