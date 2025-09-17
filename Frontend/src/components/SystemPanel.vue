<script setup>
const props = defineProps({
  systemHealth: {
    type: Object,
    required: true
  },
  powerStatus: {
    type: Object,
    required: true
  }
})

const getBatteryColor = (level) => {
  if (level > 60) return 'text-green-500'
  if (level > 30) return 'text-yellow-500'
  return 'text-red-500'
}

const getUsageColor = (usage) => {
  if (usage < 50) return 'text-green-500'
  if (usage < 80) return 'text-yellow-500'
  return 'text-red-500'
}
</script>

<template>
  <!-- System Info (Combined) -->
  <div class="bg-white rounded-xl border border-slate-200 p-4">
    <div class="flex items-center space-x-2 mb-3">
      <div class="w-6 h-6 bg-emerald-500 rounded-lg flex items-center justify-center">
        <svg class="w-4 h-4 text-white" fill="currentColor" viewBox="0 0 24 24">
          <path d="M12 2L13.09 8.26L22 9L13.09 9.74L12 16L10.91 9.74L2 9L10.91 8.26L12 2Z"/>
        </svg>
      </div>
      <div>
        <h3 class="font-bold text-slate-800">System</h3>
        <div class="text-sm text-slate-400">—</div>
      </div>
    </div>
    
    <!-- Combined system data in compact grid -->
    <div class="grid grid-cols-2 gap-3 text-sm">
      <!-- Power section -->
      <div class="space-y-1">
        <div class="flex justify-between">
          <span class="text-slate-500">Battery:</span>
          <span :class="powerStatus.batteryLevel !== null ? getBatteryColor(powerStatus.batteryLevel) : 'text-slate-400'">{{ powerStatus.batteryLevel !== null ? powerStatus.batteryLevel + '%' : '—' }}</span>
        </div>
        <div class="w-full bg-slate-200 rounded-full h-1 mb-1">
          <div class="h-1 rounded-full transition-all" 
               :class="powerStatus.batteryLevel !== null ? (powerStatus.batteryLevel > 60 ? 'bg-emerald-500' : powerStatus.batteryLevel > 30 ? 'bg-yellow-500' : 'bg-red-500') : 'bg-slate-300'"
               :style="`width: ${powerStatus.batteryLevel !== null ? powerStatus.batteryLevel : 0}%`"></div>
        </div>
        <div class="flex justify-between">
          <span class="text-slate-500">Draw:</span>
          <span :class="powerStatus.powerConsumption !== null ? 'font-mono' : 'text-slate-400'">{{ powerStatus.powerConsumption !== null ? powerStatus.powerConsumption + 'W' : '—' }}</span>
        </div>
        <div class="flex justify-between">
          <span class="text-slate-500">Runtime:</span>
          <span :class="powerStatus.estimatedRuntime !== null ? '' : 'text-slate-400'">{{ powerStatus.estimatedRuntime !== null ? powerStatus.estimatedRuntime : '—' }}</span>
        </div>
      </div>
      
      <!-- System + Files section -->
      <div class="space-y-1">
        <div class="flex justify-between">
          <span class="text-slate-500">CPU:</span>
          <span :class="systemHealth.cpuUsage !== null ? getUsageColor(systemHealth.cpuUsage) : 'text-slate-400'">{{ systemHealth.cpuUsage !== null ? systemHealth.cpuUsage.toFixed(1) + '%' : '—' }}</span>
        </div>
        <div class="flex justify-between">
          <span class="text-slate-500">RAM:</span>
          <span :class="systemHealth.memoryUsage !== null ? getUsageColor(systemHealth.memoryUsage) : 'text-slate-400'">{{ systemHealth.memoryUsage !== null ? systemHealth.memoryUsage.toFixed(1) + '%' : '—' }}</span>
        </div>
        <div class="flex justify-between">
          <span class="text-slate-500">Temp:</span>
          <span :class="systemHealth.temperature !== null ? 'text-orange-600' : 'text-slate-400'">{{ systemHealth.temperature !== null ? systemHealth.temperature.toFixed(1) + '°C' : '—' }}</span>
        </div>
      </div>
    </div>
  </div>
</template>