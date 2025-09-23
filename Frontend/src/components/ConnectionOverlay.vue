<template>
  <div v-if="showOverlay" class="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/90 backdrop-blur-sm">
    <div class="bg-white rounded-xl shadow-2xl p-8 max-w-md w-full mx-4 text-center">
      <!-- Status Icon -->
      <div class="mb-6">
        <div class="relative inline-flex items-center justify-center">
          <div class="w-16 h-16 rounded-full flex items-center justify-center bg-orange-100">
            <!-- Reconnecting Spinner -->
            <div class="w-8 h-8 border-3 border-current border-t-transparent rounded-full animate-spin text-orange-600">
            </div>
          </div>
        </div>
      </div>

      <!-- Status Text -->
      <h2 class="text-2xl font-bold mb-2 text-orange-700">
        {{ statusTitle }}
      </h2>
      
      <p class="text-slate-600 mb-4">
        {{ statusMessage }}
      </p>

    </div>
  </div>
</template>

<script setup>
import { computed } from 'vue'

const props = defineProps({
  connectionStatus: {
    type: String,
    required: true,
    validator: (value) => ['Connected', 'Reconnecting'].includes(value)
  },
  retryAttempt: {
    type: Number,
    default: 0
  },
  nextRetryIn: {
    type: Number,
    default: 0
  }
})

const showOverlay = computed(() => {
  return props.connectionStatus !== 'Connected'
})

const statusTitle = computed(() => {
  return 'Reconnecting...'
})

const statusMessage = computed(() => {
  return 'Attempting to connect to the data collection system.'
})
</script>