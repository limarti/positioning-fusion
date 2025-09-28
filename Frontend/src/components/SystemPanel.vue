<script setup>
import Card from './common/Card.vue'
import { useSystemData } from '@/composables/useSystemData'
import { useSignalR } from '@/composables/useSignalR'

// Get data from composables
const {
  state: systemState,
  getBatteryColor,
  getUsageColor,
  saveHostname,
  cancelHostnameEdit
} = useSystemData()
const { signalrConnection } = useSignalR()

// Handle hostname save using the composable function
const onSaveHostname = async () => {
  await saveHostname(signalrConnection.value)
}
</script>

<template>
  <div class="main-container">
    <!-- System Info (Combined) -->
    <Card 
      title="System" 
      subtitle="—" 
      :icon="`<svg fill='currentColor' viewBox='0 0 24 24'><path d='M12 2L13.09 8.26L22 9L13.09 9.74L12 16L10.91 9.74L2 9L10.91 8.26L12 2Z'/></svg>`"
      icon-color="bg-emerald-500"
    >
    
    <!-- System data -->
    <div class="space-y-1 text-sm">
      <!-- System Health section -->
      <div class="flex justify-between">
        <span class="text-slate-500">CPU:</span>
        <span :class="systemState.systemHealth.cpuUsage !== null ? getUsageColor(systemState.systemHealth.cpuUsage) : 'text-slate-400'">{{ systemState.systemHealth.cpuUsage !== null ? systemState.systemHealth.cpuUsage.toFixed(1) + '%' : '—' }}</span>
      </div>
      <div class="flex justify-between">
        <span class="text-slate-500">RAM:</span>
        <span :class="systemState.systemHealth.memoryUsage !== null ? getUsageColor(systemState.systemHealth.memoryUsage) : 'text-slate-400'">{{ systemState.systemHealth.memoryUsage !== null ? systemState.systemHealth.memoryUsage.toFixed(1) + '%' : '—' }}</span>
      </div>
      <div class="flex justify-between">
        <span class="text-slate-500">Temperature:</span>
        <span :class="systemState.systemHealth.temperature !== null ? 'text-orange-600' : 'text-slate-400'">{{ systemState.systemHealth.temperature !== null ? systemState.systemHealth.temperature.toFixed(1) + '°C' : '—' }}</span>
      </div>

      <!-- Battery Information -->
<!--      <div class="flex justify-between">-->
<!--        <span class="text-slate-500">Battery:</span>-->
<!--        <span :class="systemState.powerStatus.batteryLevel !== null ? getBatteryColor(systemState.powerStatus.batteryLevel) : 'text-slate-400'">-->
<!--          {{ systemState.powerStatus.batteryLevel !== null ? systemState.powerStatus.batteryLevel.toFixed(1) + '%' : '—' }}-->
<!--          <span v-if="systemState.powerStatus.isExternalPowerConnected" class="text-green-600 ml-1">⚡</span>-->
<!--        </span>-->
<!--      </div>-->
      <div class="flex justify-between">
        <span class="text-slate-500">Voltage:</span>
        <span class="text-blue-600">{{ systemState.powerStatus.batteryVoltage !== null ? systemState.powerStatus.batteryVoltage.toFixed(2) + 'V' : '—' }}</span>
      </div>
      <div v-if="systemState.dischargeRate !== null" class="flex justify-between">
        <span class="text-slate-500">{{ systemState.powerStatus.isExternalPowerConnected ? 'Charging Rate:' : 'Discharge Rate:' }}</span>
        <span :class="systemState.powerStatus.isExternalPowerConnected ? 'text-green-500' : 'text-red-500'">{{ Math.abs(systemState.dischargeRate).toFixed(2) }}%/min</span>
      </div>

      <div class="flex justify-between">
        <span class="text-slate-500">GNSS Throughput:</span>
        <span class="text-blue-600 font-mono">
          <span v-if="systemState.dataRates.kbpsGnssIn !== null && systemState.dataRates.kbpsGnssIn !== undefined">
            <svg class="w-4 h-4 inline mr-0.5" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" d="M15.75 17.25 12 21m0 0-3.75-3.75M12 21V3" />
            </svg>{{ systemState.dataRates.kbpsGnssIn.toFixed(1) }}
          </span>
          <span v-else>
            <svg class="w-4 h-4 inline mr-0.5" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" d="M15.75 17.25 12 21m0 0-3.75-3.75M12 21V3" />
            </svg>0.0
          </span>
          <span v-if="systemState.dataRates.kbpsGnssOut !== null && systemState.dataRates.kbpsGnssOut !== undefined">
            <svg class="w-4 h-4 inline ml-1.5 mr-0.5" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" d="M8.25 6.75 12 3m0 0 3.75 3.75M12 3v18" />
            </svg>{{ systemState.dataRates.kbpsGnssOut.toFixed(1) }}
          </span>
          <span v-else>
            <svg class="w-4 h-4 inline ml-1.5 mr-0.5" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" d="M8.25 6.75 12 3m0 0 3.75 3.75M12 3v18" />
            </svg>0.0
          </span>
          kbps
        </span>
      </div>

      <div v-if="(systemState.dataRates.kbpsLoRaIn !== null && systemState.dataRates.kbpsLoRaIn !== undefined && systemState.dataRates.kbpsLoRaIn > 0) || (systemState.dataRates.kbpsLoRaOut !== null && systemState.dataRates.kbpsLoRaOut !== undefined && systemState.dataRates.kbpsLoRaOut > 0)" class="flex justify-between">
        <span class="text-slate-500">LoRa Throughput:</span>
        <span class="text-amber-600 font-mono">
          <span v-if="systemState.dataRates.kbpsLoRaIn !== null && systemState.dataRates.kbpsLoRaIn !== undefined && systemState.dataRates.kbpsLoRaIn > 0">
            <svg class="w-4 h-4 inline mr-0.5" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" d="M15.75 17.25 12 21m0 0-3.75-3.75M12 21V3" />
            </svg>{{ systemState.dataRates.kbpsLoRaIn.toFixed(1) }}
          </span>
          <span v-if="systemState.dataRates.kbpsLoRaOut !== null && systemState.dataRates.kbpsLoRaOut !== undefined && systemState.dataRates.kbpsLoRaOut > 0">
            <svg class="w-4 h-4 inline ml-1.5 mr-0.5" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" d="M8.25 6.75 12 3m0 0 3.75 3.75M12 3v18" />
            </svg>{{ systemState.dataRates.kbpsLoRaOut.toFixed(1) }}
          </span>
          kbps
        </span>
      </div>

      <div class="flex justify-between">
        <span class="text-slate-500">IMU Throughput:</span>
        <span class="text-purple-600 font-mono">
          <svg class="w-4 h-4 inline mr-0.5" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor">
            <path stroke-linecap="round" stroke-linejoin="round" d="M15.75 17.25 12 21m0 0-3.75-3.75M12 21V3" />
          </svg>{{ systemState.dataRates.kbpsImu !== null && systemState.dataRates.kbpsImu !== undefined ? systemState.dataRates.kbpsImu.toFixed(1) : '0.0' }} kbps
        </span>
      </div>

      <!-- Hostname Display -->
      <div class="flex justify-between">
        <span class="text-slate-500">Hostname:</span>
        <span class="text-blue-600 font-mono">{{ systemState.systemHealth.hostname || '—' }}</span>
      </div>

    </div>

  </Card>

  <!-- Device Name -->
  <Card
    title="Device Name"
    subtitle="Edit device name"
    :icon="`<svg fill='currentColor' viewBox='0 0 24 24'><path d='M12 2C13.1 2 14 2.9 14 4C14 5.1 13.1 6 12 6C10.9 6 10 5.1 10 4C10 2.9 10.9 2 12 2ZM21 9V7L15 1H5C3.89 1 3 1.89 3 3V21C3 22.11 3.89 23 5 23H19C20.11 23 21 22.11 21 21V9H21ZM19 21H5V3H13V9H19V21Z'/></svg>`"
    icon-color="bg-blue-500"
  >
    <div class="space-y-4">
      <!-- Info about changes -->
      <div class="text-xs text-slate-400">
        <p>Changes require a system reboot to take full effect.</p>
      </div>

      <!-- Hostname input form -->
      <div class="flex space-x-2">
        <div class="flex-1">
          <input
            v-model="systemState.editedHostname"
            type="text"
            placeholder="Device hostname"
            :class="[
              'form-input text-sm',
              !systemState.isHostnameValid && systemState.hostnameValidationError
                ? 'border-red-300 focus:ring-red-500 bg-red-50'
                : ''
            ]"
            :disabled="systemState.isUpdatingHostname"
            @keyup.enter="onSaveHostname"
            @keyup.escape="cancelHostnameEdit"
          />
        </div>

        <!-- Save/Cancel buttons - only show when modified -->
        <div v-if="systemState.isHostnameModified" class="flex space-x-2">
          <button
            @click="onSaveHostname"
            :disabled="systemState.isUpdatingHostname || !systemState.editedHostname || !systemState.editedHostname.trim() || !systemState.isHostnameValid"
            class="px-3 py-2 bg-green-600 text-white rounded-md hover:bg-green-700 focus:outline-none focus:ring-2 focus:ring-green-500 focus:ring-offset-2 disabled:opacity-50 disabled:cursor-not-allowed text-sm"
            title="Save changes"
          >
            <span v-if="systemState.isUpdatingHostname">
              <svg class="animate-spin h-4 w-4" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                <path class="opacity-75" fill="currentColor" d="m4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
              </svg>
            </span>
            <span v-else>
              <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5 13l4 4L19 7"></path>
              </svg>
            </span>
          </button>
          <button
            @click="cancelHostnameEdit"
            :disabled="systemState.isUpdatingHostname"
            class="px-3 py-2 bg-gray-500 text-white rounded-md hover:bg-gray-600 focus:outline-none focus:ring-2 focus:ring-gray-500 focus:ring-offset-2 disabled:opacity-50 disabled:cursor-not-allowed text-sm"
            title="Cancel changes"
          >
            <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12"></path>
            </svg>
          </button>
        </div>
      </div>

      <!-- Validation error message -->
      <div v-if="systemState.hostnameValidationError && systemState.isHostnameModified" class="text-sm text-red-600 bg-red-50 p-2 rounded-md border border-red-200">
        <svg class="w-4 h-4 inline mr-1" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-2.5L13.732 4c-.77-.833-1.964-.833-2.732 0L3.732 16.5c-.77.833.192 2.5 1.732 2.5z"></path>
        </svg>
        {{ systemState.hostnameValidationError }}
      </div>

      <!-- Success/Error messages -->
      <div v-if="systemState.hostnameSuccess" class="text-sm text-green-600 bg-green-50 p-3 rounded-md">
        {{ systemState.hostnameSuccess }}
      </div>
      <div v-if="systemState.hostnameError" class="text-sm text-red-600 bg-red-50 p-3 rounded-md">
        {{ systemState.hostnameError }}
      </div>

    </div>
  </Card>
  </div>
</template>