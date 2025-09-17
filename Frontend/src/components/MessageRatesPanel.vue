<script setup>
import { computed } from 'vue'

const props = defineProps({
  messageRates: {
    type: Object,
    required: true
  }
})

// Sort message types for consistent display
const sortedMessageTypes = computed(() => {
  if (!props.messageRates.messageRates) return []
  
  return Object.entries(props.messageRates.messageRates)
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([type, rate]) => ({
      type,
      rate: parseFloat(rate)
    }))
})

// Get color based on message type
const getMessageColor = (messageType) => {
  if (messageType.includes('NAV')) return 'text-blue-600 bg-blue-50 border-blue-200'
  if (messageType.includes('RXM')) return 'text-green-600 bg-green-50 border-green-200'
  if (messageType.includes('TIM')) return 'text-purple-600 bg-purple-50 border-purple-200'
  if (messageType.includes('MON')) return 'text-orange-600 bg-orange-50 border-orange-200'
  return 'text-slate-600 bg-slate-50 border-slate-200'
}

// Get rate status color
const getRateColor = (rate) => {
  if (rate >= 9.0) return 'text-emerald-700'
  if (rate >= 5.0) return 'text-yellow-700' 
  if (rate > 0) return 'text-orange-700'
  return 'text-slate-400'
}
</script>

<template>
  <div class="bg-white rounded-xl border border-slate-200 p-4 break-inside-avoid mb-6">
    <div class="flex items-center space-x-3 mb-4">
      <svg class="w-6 h-6 text-indigo-600" fill="currentColor" viewBox="0 0 24 24">
        <path d="M3,3H21V5H3V3M3,7H15V9H3V7M3,11H21V13H3V11M3,15H15V17H3V15M3,19H21V21H3V19Z"/>
      </svg>
      <h2 class="text-lg font-bold text-slate-800">UBX Message Rates</h2>
    </div>

    <div v-if="sortedMessageTypes.length === 0" class="text-center py-8 text-slate-500">
      No UBX messages received
    </div>

    <div v-else class="space-y-2">
      <div 
        v-for="message in sortedMessageTypes" 
        :key="message.type"
        class="flex items-center justify-between p-3 rounded-lg border"
        :class="getMessageColor(message.type)"
      >
        <div class="flex items-center space-x-3">
          <div class="text-sm font-mono font-semibold">
            {{ message.type }}
          </div>
        </div>
        <div class="flex items-center space-x-2">
          <div class="text-sm font-bold" :class="getRateColor(message.rate)">
            {{ message.rate.toFixed(1) }}
          </div>
          <div class="text-xs text-slate-500">Hz</div>
        </div>
      </div>
    </div>
  </div>
</template>