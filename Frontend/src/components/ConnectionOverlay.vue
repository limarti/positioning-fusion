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

      <!-- Retry Information -->
      <div v-if="nextRetryIn > 0" 
           class="text-sm text-slate-500 mb-4">
        Retry attempt {{ retryAttempt }} in {{ nextRetryIn }}s
      </div>
      
      <div v-else-if="retryAttempt > 0" 
           class="text-sm text-slate-500 mb-4">
        Attempt {{ retryAttempt }}
      </div>

      <!-- Progress indicator for retry countdown -->
      <div v-if="nextRetryIn > 0" 
           class="w-full bg-slate-200 rounded-full h-2 mb-4">
        <div class="bg-slate-600 h-2 rounded-full transition-all duration-1000"
             :style="{ width: `${((5 - nextRetryIn) / 5) * 100}%` }">
        </div>
      </div>
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