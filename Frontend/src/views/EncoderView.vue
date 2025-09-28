<script setup>
import { inject } from 'vue'
import EncoderPanel from '@/components/EncoderPanel.vue'

// Inject data from App.vue
const encoderData = inject('encoderData')
</script>

<template>
  <EncoderPanel :encoderData="encoderData" />
</template>