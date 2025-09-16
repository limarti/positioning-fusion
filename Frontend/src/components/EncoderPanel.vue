<script setup>
const props = defineProps({
  encoderData: {
    type: Object,
    required: true
  }
})
</script>

<template>
  <!-- Encoder -->
  <div class="bg-white rounded-xl border border-slate-200 p-4">
    <div class="flex items-center space-x-2 mb-3">
      <div class="w-6 h-6 bg-orange-500 rounded-lg flex items-center justify-center">
        <svg class="w-4 h-4 text-white" fill="currentColor" viewBox="0 0 24 24">
          <path d="M12,2A10,10 0 0,1 22,12A10,10 0 0,1 12,22A10,10 0 0,1 2,12A10,10 0 0,1 12,2M12,4A8,8 0 0,0 4,12A8,8 0 0,0 12,20A8,8 0 0,0 20,12A8,8 0 0,0 12,4Z"/>
        </svg>
      </div>
      <div>
        <h3 class="font-bold text-slate-800">Encoder</h3>
        <div class="text-xs text-emerald-600">Active</div>
      </div>
    </div>
    <div class="space-y-2 text-xs">
      <div class="flex justify-between">
        <span class="text-slate-500">Count:</span>
        <span :class="encoderData.rawCount !== null ? 'font-mono' : 'text-slate-400'">{{ encoderData.rawCount !== null ? encoderData.rawCount : '—' }}</span>
      </div>
      <div class="flex justify-between">
        <span class="text-slate-500">Direction:</span>
        <span :class="encoderData.direction !== null ? '' : 'text-slate-400'">{{ encoderData.direction !== null ? encoderData.direction : '—' }}</span>
      </div>
      <div class="flex justify-between">
        <span class="text-slate-500">Rate:</span>
        <span :class="encoderData.pulsesPerSecond !== null ? 'font-mono' : 'text-slate-400'">{{ encoderData.pulsesPerSecond !== null ? encoderData.pulsesPerSecond + '/s' : '—' }}</span>
      </div>
    </div>
  </div>
</template>