<script setup>
import { ref, watch } from 'vue'
import Card from './common/Card.vue'

const props = defineProps({
  systemHealth: {
    type: Object,
    required: true
  },
  powerStatus: {
    type: Object,
    required: true
  },
  dataRates: {
    type: Object,
    required: true
  }
})

// Simple discharge rate tracking
const batteryHistory = ref([])
const dischargeRate = ref(null)

watch(() => props.powerStatus.batteryLevel, (newLevel) => {
  if (newLevel !== null) {
    const now = Date.now()
    batteryHistory.value.push({ level: newLevel, timestamp: now })
    
    // Keep only last 2 minutes
    const twoMinutesAgo = now - (1 * 60 * 1000)
    batteryHistory.value = batteryHistory.value.filter(entry => entry.timestamp > twoMinutesAgo)
    
    // Calculate rate as soon as we have 2 data points
    if (batteryHistory.value.length >= 2) {
      const oldest = batteryHistory.value[0]
      const newest = batteryHistory.value[batteryHistory.value.length - 1]
      const levelDiff = oldest.level - newest.level
      const timeDiff = (newest.timestamp - oldest.timestamp) / (60 * 1000) // minutes
      
      if (timeDiff > 0) {
        dischargeRate.value = levelDiff / timeDiff // %/minute
      }
    }
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
  <Card 
    title="System" 
    subtitle="—" 
    :icon="`<svg fill='currentColor' viewBox='0 0 24 24'><path d='M12 2L13.09 8.26L22 9L13.09 9.74L12 16L10.91 9.74L2 9L10.91 8.26L12 2Z'/></svg>`"
    icon-color="bg-emerald-500"
  >
    
    <!-- Combined system data in single column -->
    <div class="space-y-1 text-sm">
      <!-- Power section -->
      <div class="flex justify-between">
        <span class="text-slate-500">Battery:</span>
        <div class="flex items-center space-x-1">
          <span :class="powerStatus.batteryLevel !== null ? getBatteryColor(powerStatus.batteryLevel) : 'text-slate-400'">{{ powerStatus.batteryLevel !== null ? powerStatus.batteryLevel.toFixed(1) + '%' : '—' }}</span>
          <span v-if="powerStatus.isExternalPowerConnected" class="text-green-500 text-xs">🔌</span>
          <span v-else class="text-orange-500 text-xs">🔋</span>
        </div>
      </div>
      <div class="w-full bg-slate-200 rounded-full h-1 mb-1">
        <div class="h-1 rounded-full transition-all" 
             :class="powerStatus.batteryLevel !== null ? (powerStatus.batteryLevel > 60 ? 'bg-emerald-500' : powerStatus.batteryLevel > 30 ? 'bg-yellow-500' : 'bg-red-500') : 'bg-slate-300'"
             :style="`width: ${powerStatus.batteryLevel !== null ? powerStatus.batteryLevel : 0}%`"></div>
      </div>
      <div class="flex justify-between">
        <span class="text-slate-500">Voltage:</span>
        <span :class="powerStatus.batteryVoltage !== null ? 'font-mono text-blue-600' : 'text-slate-400'">{{ powerStatus.batteryVoltage !== null ? powerStatus.batteryVoltage.toFixed(2) + 'V' : '—' }}</span>
      </div>
      <div class="flex justify-between">
        <span class="text-slate-500">Power:</span>
        <span :class="powerStatus.isExternalPowerConnected ? 'text-green-600' : 'text-orange-600'">{{ powerStatus.isExternalPowerConnected ? 'Plugged In' : 'Battery Only' }}</span>
      </div>
      <div class="flex justify-between">
        <span class="text-slate-500">Rate:</span>
        <span class="font-mono text-slate-400">{{ dischargeRate !== null ? dischargeRate.toFixed(2) + '%/min' : '—' }}</span>
      </div>
      
      <!-- System Health section -->
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

      <div v-if="(dataRates.kbpsGnssIn !== null && dataRates.kbpsGnssIn !== undefined && dataRates.kbpsGnssIn > 0) || (dataRates.kbpsGnssOut !== null && dataRates.kbpsGnssOut !== undefined && dataRates.kbpsGnssOut > 0)" class="flex justify-between">
        <span class="text-slate-500">GNSS Throughput:</span>
        <span class="text-blue-600 font-mono">
          <span v-if="dataRates.kbpsGnssIn !== null && dataRates.kbpsGnssIn !== undefined && dataRates.kbpsGnssIn > 0">
            <svg class="w-4 h-4 inline mr-0.5" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" d="M15.75 17.25 12 21m0 0-3.75-3.75M12 21V3" />
            </svg>{{ dataRates.kbpsGnssIn.toFixed(1) }}
          </span>
          <span v-if="dataRates.kbpsGnssOut !== null && dataRates.kbpsGnssOut !== undefined && dataRates.kbpsGnssOut > 0">
            <svg class="w-4 h-4 inline ml-1.5 mr-0.5" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" d="M8.25 6.75 12 3m0 0 3.75 3.75M12 3v18" />
            </svg>{{ dataRates.kbpsGnssOut.toFixed(1) }}
          </span>
          kbps
        </span>
      </div>

      <div v-if="(dataRates.kbpsLoRaIn !== null && dataRates.kbpsLoRaIn !== undefined && dataRates.kbpsLoRaIn > 0) || (dataRates.kbpsLoRaOut !== null && dataRates.kbpsLoRaOut !== undefined && dataRates.kbpsLoRaOut > 0)" class="flex justify-between">
        <span class="text-slate-500">LoRa Throughput:</span>
        <span class="text-amber-600 font-mono">
          <span v-if="dataRates.kbpsLoRaIn !== null && dataRates.kbpsLoRaIn !== undefined && dataRates.kbpsLoRaIn > 0">
            <svg class="w-4 h-4 inline mr-0.5" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" d="M15.75 17.25 12 21m0 0-3.75-3.75M12 21V3" />
            </svg>{{ dataRates.kbpsLoRaIn.toFixed(1) }}
          </span>
          <span v-if="dataRates.kbpsLoRaOut !== null && dataRates.kbpsLoRaOut !== undefined && dataRates.kbpsLoRaOut > 0">
            <svg class="w-4 h-4 inline ml-1.5 mr-0.5" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" d="M8.25 6.75 12 3m0 0 3.75 3.75M12 3v18" />
            </svg>{{ dataRates.kbpsLoRaOut.toFixed(1) }}
          </span>
          kbps
        </span>
      </div>

    </div>

  </Card>
</template>