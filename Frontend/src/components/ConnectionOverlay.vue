<template>
  <div v-if="showOverlay" class="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/90 backdrop-blur-sm">
    <div class="bg-white rounded-xl shadow-2xl p-8 max-w-md w-full mx-4 text-center">
      <!-- Status Icon -->
      <div class="mb-6">
        <div class="relative inline-flex items-center justify-center">
          <div :class="[
            'w-16 h-16 rounded-full flex items-center justify-center',
            connectionStatus === 'Connecting' ? 'bg-yellow-100' :
            connectionStatus === 'Reconnecting' ? 'bg-orange-100' :
            'bg-red-100'
          ]">
            <!-- Connecting/Reconnecting Spinner -->
            <div v-if="connectionStatus === 'Connecting' || connectionStatus === 'Reconnecting'"
                 class="w-8 h-8 border-3 border-current border-t-transparent rounded-full animate-spin"
                 :class="connectionStatus === 'Connecting' ? 'text-yellow-600' : 'text-orange-600'">
            </div>
            <!-- Disconnected X -->
            <div v-else class="w-8 h-8 text-red-600">
              <svg fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" />
              </svg>
            </div>
          </div>
        </div>
      </div>

      <!-- Status Text -->
      <h2 :class="[
        'text-2xl font-bold mb-2',
        connectionStatus === 'Connecting' ? 'text-yellow-700' :
        connectionStatus === 'Reconnecting' ? 'text-orange-700' :
        'text-red-700'
      ]">
        {{ statusTitle }}
      </h2>
      
      <p class="text-slate-600 mb-4">
        {{ statusMessage }}
      </p>

      <!-- Retry Information -->
      <div v-if="connectionStatus === 'Disconnected' && nextRetryIn > 0" 
           class="text-sm text-slate-500 mb-4">
        Retry attempt {{ retryAttempt }} in {{ nextRetryIn }}s
      </div>
      
      <div v-else-if="connectionStatus === 'Reconnecting'" 
           class="text-sm text-slate-500 mb-4">
        Attempt {{ retryAttempt }}
      </div>

      <!-- Progress indicator for retry countdown -->
      <div v-if="connectionStatus === 'Disconnected' && nextRetryIn > 0" 
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
    validator: (value) => ['Connected', 'Connecting', 'Reconnecting', 'Disconnected'].includes(value)
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
  switch (props.connectionStatus) {
    case 'Connecting':
      return 'Connecting...'
    case 'Reconnecting':
      return 'Reconnecting...'
    case 'Disconnected':
      return 'Connection Lost'
    default:
      return 'Unknown Status'
  }
})

const statusMessage = computed(() => {
  switch (props.connectionStatus) {
    case 'Connecting':
      return 'Establishing connection to the data collection system.'
    case 'Reconnecting':
      return 'Attempting to reconnect to the data collection system.'
    case 'Disconnected':
      return 'Unable to connect to the data collection system. Please check your network connection and ensure the system is running.'
    default:
      return 'Please wait...'
  }
})
</script>