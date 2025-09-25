<script setup>
import { ref, watch, inject } from 'vue'
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

// Get SignalR connection from provider
const connection = inject('signalrConnection')

// Hostname management
const editedHostname = ref('')
const originalHostname = ref('')
const isUpdatingHostname = ref(false)
const hostnameError = ref('')
const hostnameSuccess = ref('')
const isHostnameModified = ref(false)
const hostnameValidationError = ref('')
const isHostnameValid = ref(true)

// Watch for hostname changes from backend
watch(() => props.systemHealth.hostname, (newHostname) => {
  if (newHostname && !isHostnameModified.value) {
    originalHostname.value = newHostname
    editedHostname.value = newHostname
  } else if (!newHostname && !editedHostname.value) {
    // Initialize with empty string if no hostname available yet
    originalHostname.value = ''
    editedHostname.value = ''
  }
}, { immediate: true })

// Watch for user edits to hostname input
watch(editedHostname, (newValue) => {
  isHostnameModified.value = newValue !== originalHostname.value && newValue !== null && newValue !== undefined

  // Validate hostname in real-time
  if (newValue) {
    const validationError = validateHostname(newValue)
    hostnameValidationError.value = validationError || ''
    isHostnameValid.value = !validationError
  } else {
    hostnameValidationError.value = ''
    isHostnameValid.value = true
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

// Hostname validation function
const validateHostname = (hostname) => {
  if (!hostname || typeof hostname !== 'string') {
    return 'Hostname is required'
  }

  const trimmedHostname = hostname.trim()

  // Check length (2-63 characters for practical use)
  if (trimmedHostname.length === 0) {
    return 'Hostname cannot be empty'
  }
  if (trimmedHostname.length === 1) {
    return 'Hostname must be at least 2 characters long'
  }
  if (trimmedHostname.length > 63) {
    return 'Hostname must be 63 characters or less'
  }

  // Check format: only letters, digits, and hyphens
  const validCharsRegex = /^[a-zA-Z0-9-]+$/
  if (!validCharsRegex.test(trimmedHostname)) {
    return 'Hostname can only contain letters, digits, and hyphens'
  }

  // Check start and end: cannot start or end with hyphen
  if (trimmedHostname.startsWith('-')) {
    return 'Hostname cannot start with a hyphen'
  }
  if (trimmedHostname.endsWith('-')) {
    return 'Hostname cannot end with a hyphen'
  }

  // Check for consecutive hyphens (optional - some systems allow this)
  if (trimmedHostname.includes('--')) {
    return 'Hostname cannot contain consecutive hyphens'
  }

  return null // Valid
}

// Hostname management functions
const saveHostname = async () => {
  if (!connection.value || !editedHostname.value || !editedHostname.value.trim()) return

  // Check validation before saving
  if (!isHostnameValid.value) {
    hostnameError.value = hostnameValidationError.value || 'Invalid hostname format'
    return
  }

  isUpdatingHostname.value = true
  hostnameError.value = ''
  hostnameSuccess.value = ''

  try {
    const result = await connection.value.invoke('UpdateHostname', editedHostname.value.trim())

    if (result.success) {
      hostnameSuccess.value = result.message
      originalHostname.value = editedHostname.value
      isHostnameModified.value = false
    } else {
      hostnameError.value = result.message
    }
  } catch (error) {
    console.error('Failed to update hostname:', error)
    hostnameError.value = 'Failed to update hostname: ' + error.message
  } finally {
    isUpdatingHostname.value = false

    // Clear messages after 5 seconds
    setTimeout(() => {
      hostnameError.value = ''
      hostnameSuccess.value = ''
    }, 5000)
  }
}

const cancelHostnameEdit = () => {
  editedHostname.value = originalHostname.value
  isHostnameModified.value = false
  hostnameError.value = ''
  hostnameValidationError.value = ''
  isHostnameValid.value = true
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
    
    <!-- System data -->
    <div class="space-y-1 text-sm">
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
        <span class="text-slate-500">Temperature:</span>
        <span :class="systemHealth.temperature !== null ? 'text-orange-600' : 'text-slate-400'">{{ systemHealth.temperature !== null ? systemHealth.temperature.toFixed(1) + '°C' : '—' }}</span>
      </div>

      <!-- Battery Information -->
<!--      <div class="flex justify-between">-->
<!--        <span class="text-slate-500">Battery:</span>-->
<!--        <span :class="powerStatus.batteryLevel !== null ? getBatteryColor(powerStatus.batteryLevel) : 'text-slate-400'">-->
<!--          {{ powerStatus.batteryLevel !== null ? powerStatus.batteryLevel.toFixed(1) + '%' : '—' }}-->
<!--          <span v-if="powerStatus.isExternalPowerConnected" class="text-green-600 ml-1">⚡</span>-->
<!--        </span>-->
<!--      </div>-->
      <div class="flex justify-between">
        <span class="text-slate-500">Voltage:</span>
        <span class="text-blue-600">{{ powerStatus.batteryVoltage !== null ? powerStatus.batteryVoltage.toFixed(2) + 'V' : '—' }}</span>
      </div>
      <div v-if="dischargeRate !== null" class="flex justify-between">
        <span class="text-slate-500">{{ powerStatus.isExternalPowerConnected ? 'Charging Rate:' : 'Discharge Rate:' }}</span>
        <span :class="powerStatus.isExternalPowerConnected ? 'text-green-500' : 'text-red-500'">{{ Math.abs(dischargeRate).toFixed(2) }}%/min</span>
      </div>

      <div class="flex justify-between">
        <span class="text-slate-500">GNSS Throughput:</span>
        <span class="text-blue-600 font-mono">
          <span v-if="dataRates.kbpsGnssIn !== null && dataRates.kbpsGnssIn !== undefined">
            <svg class="w-4 h-4 inline mr-0.5" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" d="M15.75 17.25 12 21m0 0-3.75-3.75M12 21V3" />
            </svg>{{ dataRates.kbpsGnssIn.toFixed(1) }}
          </span>
          <span v-else>
            <svg class="w-4 h-4 inline mr-0.5" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" d="M15.75 17.25 12 21m0 0-3.75-3.75M12 21V3" />
            </svg>0.0
          </span>
          <span v-if="dataRates.kbpsGnssOut !== null && dataRates.kbpsGnssOut !== undefined">
            <svg class="w-4 h-4 inline ml-1.5 mr-0.5" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" d="M8.25 6.75 12 3m0 0 3.75 3.75M12 3v18" />
            </svg>{{ dataRates.kbpsGnssOut.toFixed(1) }}
          </span>
          <span v-else>
            <svg class="w-4 h-4 inline ml-1.5 mr-0.5" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" d="M8.25 6.75 12 3m0 0 3.75 3.75M12 3v18" />
            </svg>0.0
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

      <div class="flex justify-between">
        <span class="text-slate-500">IMU Throughput:</span>
        <span class="text-purple-600 font-mono">
          <svg class="w-4 h-4 inline mr-0.5" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor">
            <path stroke-linecap="round" stroke-linejoin="round" d="M15.75 17.25 12 21m0 0-3.75-3.75M12 21V3" />
          </svg>{{ dataRates.kbpsImu !== null && dataRates.kbpsImu !== undefined ? dataRates.kbpsImu.toFixed(1) : '0.0' }} kbps
        </span>
      </div>

      <!-- Hostname Display -->
      <div class="flex justify-between">
        <span class="text-slate-500">Hostname:</span>
        <span class="text-blue-600 font-mono">{{ systemHealth.hostname || '—' }}</span>
      </div>

    </div>

  </Card>

  <!-- Hostname Configuration -->
  <Card
    title="Hostname Configuration"
    subtitle="Edit device hostname"
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
            v-model="editedHostname"
            type="text"
            placeholder="Device hostname"
            :class="[
              'w-full px-3 py-2 border rounded-md focus:outline-none focus:ring-2 focus:border-transparent text-sm',
              !isHostnameValid && hostnameValidationError
                ? 'border-red-300 focus:ring-red-500 bg-red-50'
                : 'border-slate-300 focus:ring-blue-500'
            ]"
            :disabled="isUpdatingHostname"
            @keyup.enter="saveHostname"
            @keyup.escape="cancelHostnameEdit"
          />
        </div>

        <!-- Save/Cancel buttons - only show when modified -->
        <div v-if="isHostnameModified" class="flex space-x-2">
          <button
            @click="saveHostname"
            :disabled="isUpdatingHostname || !editedHostname || !editedHostname.trim() || !isHostnameValid"
            class="px-3 py-2 bg-green-600 text-white rounded-md hover:bg-green-700 focus:outline-none focus:ring-2 focus:ring-green-500 focus:ring-offset-2 disabled:opacity-50 disabled:cursor-not-allowed text-sm"
            title="Save changes"
          >
            <span v-if="isUpdatingHostname">
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
            :disabled="isUpdatingHostname"
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
      <div v-if="hostnameValidationError && isHostnameModified" class="text-sm text-red-600 bg-red-50 p-2 rounded-md border border-red-200">
        <svg class="w-4 h-4 inline mr-1" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-2.5L13.732 4c-.77-.833-1.964-.833-2.732 0L3.732 16.5c-.77.833.192 2.5 1.732 2.5z"></path>
        </svg>
        {{ hostnameValidationError }}
      </div>

      <!-- Success/Error messages -->
      <div v-if="hostnameSuccess" class="text-sm text-green-600 bg-green-50 p-3 rounded-md">
        {{ hostnameSuccess }}
      </div>
      <div v-if="hostnameError" class="text-sm text-red-600 bg-red-50 p-3 rounded-md">
        {{ hostnameError }}
      </div>

      <!-- Hostname format help -->
      <div class="text-xs text-slate-400">
        <p>Hostname requirements:</p>
        <ul class="list-disc list-inside ml-2 mt-1 space-y-0.5">
          <li>2-63 characters long</li>
          <li>Letters, digits, and hyphens only</li>
          <li>Cannot start or end with a hyphen</li>
          <li>No consecutive hyphens</li>
        </ul>
      </div>
    </div>
  </Card>
</template>