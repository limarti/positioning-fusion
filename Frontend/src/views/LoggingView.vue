<script setup>
import { inject } from 'vue'
import FileLoggingPanel from '@/components/FileLoggingPanel.vue'

// Inject data from App.vue
const fileLoggingStatus = inject('fileLoggingStatus')
</script>

<template>
  <FileLoggingPanel :fileLoggingStatus="fileLoggingStatus" />
</template>