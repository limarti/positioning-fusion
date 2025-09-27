<script setup>
import { ref, inject, computed, watch, onMounted } from 'vue'
import Card from '../common/Card.vue'

const props = defineProps({
  gnssData: {
    type: Object,
    required: true
  },
  dataRates: {
    type: Object,
    required: true
  }
})

const connection = inject('signalrConnection')
const isChangingMode = ref(false)
const selectedMode = ref(props.gnssData?.corrections?.mode || 'Disabled')

// Update selectedMode when gnssData.corrections.mode changes
watch(() => props.gnssData?.corrections?.mode, (newMode) => {
  if (newMode && newMode !== selectedMode.value) {
    selectedMode.value = newMode
  }
}, { immediate: true })

const modeOptions = [
  {
    value: 'Disabled',
    label: 'Disabled',
    description: 'RTK corrections disabled',
    color: 'slate'
  },
  {
    value: 'Send',
    label: 'Base Station',
    description: 'Send RTK corrections to rovers',
    color: 'blue'
  },
  {
    value: 'Receive',
    label: 'Rover',
    description: 'Receive RTK corrections',
    color: 'purple'
  }
]

const getModeConfig = (mode) => {
  return modeOptions.find(option => option.value === mode) || modeOptions[0]
}

const currentModeConfig = computed(() => getModeConfig(props.gnssData?.corrections?.mode || 'Disabled'))

const handleModeChange = async (newMode) => {
  if (newMode === props.gnssData?.corrections?.mode || isChangingMode.value) {
    return
  }

  isChangingMode.value = true

  try {
    if (connection?.value && connection.value.state === 'Connected') {
      const success = await connection.value.invoke('SetOperatingMode', newMode)

      if (!success) {
        console.error('Failed to change mode - server returned false')
        // Reset selected mode on failure
        selectedMode.value = props.gnssData?.corrections?.mode || 'Disabled'
      }
      // On success, the mode will be updated via SignalR ModeChanged event
    } else {
      console.error('No SignalR connection available or connection not in Connected state')
      selectedMode.value = props.gnssData?.corrections?.mode || 'Disabled'
    }
  } catch (error) {
    console.error('Error changing mode:', error)
    // Reset selected mode on error
    selectedMode.value = props.gnssData?.corrections?.mode || 'Disabled'
  } finally {
    isChangingMode.value = false
  }
}
</script>

<template>
  <Card
    title="RTK"
    icon-color="bg-gray-500"
  >

    <!-- Mode Selection Section -->
    <div class="mb-6">
      <div class="text-sm font-semibold text-gray-700 mb-3">Operating Mode</div>

      <!-- Mode Selection Radio Buttons -->
      <div class="space-y-3">
        <div v-for="option in modeOptions" :key="option.value" class="flex items-center">
          <div class="flex items-center h-5">
            <input
              :id="option.value"
              v-model="selectedMode"
              :value="option.value"
              type="radio"
              name="mode"
              :disabled="isChangingMode"
              class="h-4 w-4 border-gray-300 focus:ring-2 text-gray-600 focus:ring-gray-500"
              @change="handleModeChange(option.value)"
            />
          </div>
          <div class="ml-3 text-sm">
            <label :for="option.value" class="font-medium text-gray-700 cursor-pointer">
              {{ option.label }}
            </label>
            <p class="text-sm text-gray-500">{{ option.description }}</p>
          </div>
        </div>
      </div>

      <!-- Loading State -->
      <div v-if="isChangingMode" class="mt-4 flex items-center justify-center">
        <div class="flex items-center space-x-2 text-gray-600">
          <svg class="animate-spin h-4 w-4" fill="none" viewBox="0 0 24 24">
            <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
            <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
          </svg>
          <span class="text-sm">Changing mode...</span>
        </div>
      </div>
    </div>

    <!-- RTK Status and Data (when active) -->
    <div v-if="gnssData.corrections.mode !== 'Disabled'" class="space-y-6">
      <!-- RTK Solution Status (Both Modes) -->
      <div class="flex items-center justify-between py-2">
        <span class="text-sm text-gray-600">Solution Status:</span>
        <span class="text-xs font-semibold px-3 py-1 rounded-lg"
              :class="gnssData.rtkMode === 'Fixed' ? 'bg-green-100 text-green-800' : 'bg-yellow-100 text-yellow-700'">
          {{ gnssData.rtkMode }}
        </span>
      </div>

      <!-- Position Accuracy (Both Modes) -->
      <div class="border-t border-gray-200 pt-4">
        <div class="space-y-2">
          <div class="text-sm text-gray-600 mb-3">Position Accuracy</div>
          <div class="flex justify-between py-1">
            <span class="text-sm text-gray-600">North:</span>
            <span class="text-sm font-medium" :class="gnssData.rtk.relativeAccuracy.north !== null ? 'text-gray-800' : 'text-slate-400'">{{ gnssData.rtk.relativeAccuracy.north !== null ? gnssData.rtk.relativeAccuracy.north.toFixed(3) + 'm' : '—' }}</span>
          </div>
          <div class="flex justify-between py-1">
            <span class="text-sm text-gray-600">East:</span>
            <span class="text-sm font-medium" :class="gnssData.rtk.relativeAccuracy.east !== null ? 'text-gray-800' : 'text-slate-400'">{{ gnssData.rtk.relativeAccuracy.east !== null ? gnssData.rtk.relativeAccuracy.east.toFixed(3) + 'm' : '—' }}</span>
          </div>
          <div class="flex justify-between py-1">
            <span class="text-sm text-gray-600">Down:</span>
            <span class="text-sm font-medium" :class="gnssData.rtk.relativeAccuracy.down !== null ? 'text-gray-800' : 'text-slate-400'">{{ gnssData.rtk.relativeAccuracy.down !== null ? gnssData.rtk.relativeAccuracy.down.toFixed(3) + 'm' : '—' }}</span>
          </div>
        </div>
      </div>

      <!-- Rover-Specific Data -->
      <div v-if="gnssData.corrections.mode === 'Receive'" class="border-t border-gray-200 pt-4 space-y-4">
        <div class="flex justify-between py-1">
          <span class="text-sm text-gray-600">Baseline Distance:</span>
          <span class="text-sm font-medium" :class="gnssData.rtk.baselineLength !== null ? 'text-gray-800' : 'text-slate-400'">{{ gnssData.rtk.baselineLength !== null ? gnssData.rtk.baselineLength.toFixed(0) + 'm' : '—' }}</span>
        </div>
        <div class="space-y-2">
          <div class="flex justify-between py-1">
            <span class="text-sm text-gray-600">Solution Confidence:</span>
            <span class="text-sm font-medium" :class="gnssData.rtk.arRatio !== null ? 'text-gray-800' : 'text-slate-400'">{{ gnssData.rtk.arRatio !== null ? gnssData.rtk.arRatio.toFixed(1) : '—' }}</span>
          </div>
          <div class="flex justify-between py-1">
            <span class="text-sm text-gray-600">Correction Age:</span>
            <span class="text-sm font-medium" :class="gnssData.rtk.correctionAge !== null ? 'text-gray-800' : 'text-slate-400'">{{ gnssData.rtk.correctionAge !== null ? gnssData.rtk.correctionAge.toFixed(1) + 's' : '—' }}</span>
          </div>
        </div>
      </div>

      <!-- Radio Communication (Both Modes) -->
      <div class="border-t border-gray-200 pt-4">
        <!-- Base Station Mode: Show LoRa Out (corrections being sent) -->
        <div v-if="gnssData.corrections.mode === 'Send'" class="flex justify-between py-1">
          <span class="text-sm text-gray-600">Radio Throughput:</span>
          <span class="text-sm font-medium" :class="dataRates.kbpsLoRaOut !== null && dataRates.kbpsLoRaOut > 0 ? 'text-gray-800' : 'text-slate-400'">
            {{ dataRates.kbpsLoRaOut !== null ? dataRates.kbpsLoRaOut.toFixed(1) + ' kbps' : '—' }}
          </span>
        </div>
        <!-- Rover Mode: Show LoRa In (corrections being received) -->
        <div v-if="gnssData.corrections.mode === 'Receive'" class="flex justify-between py-1">
          <span class="text-sm text-gray-600">Radio Throughput:</span>
          <span class="text-sm font-medium" :class="dataRates.kbpsLoRaIn !== null && dataRates.kbpsLoRaIn > 0 ? 'text-gray-800' : 'text-slate-400'">
            {{ dataRates.kbpsLoRaIn !== null ? dataRates.kbpsLoRaIn.toFixed(1) + ' kbps' : '—' }}
          </span>
        </div>
      </div>

      <!-- Base Station Setup (Base Station Mode Only) -->
      <div v-if="gnssData.corrections.mode === 'Send'" class="border-t border-gray-200 pt-4 space-y-4">
        <div class="text-sm font-semibold text-gray-800">Base Station Setup</div>

        <div class="space-y-3">
          <div class="flex justify-between">
            <span class="text-sm text-gray-600">Survey Status:</span>
            <span class="text-xs font-semibold px-2 py-1 rounded-lg"
                  :class="gnssData.surveyIn.valid ? 'bg-green-100 text-green-800' :
                         gnssData.surveyIn.active ? 'bg-blue-100 text-blue-700' :
                         'bg-red-100 text-red-700'">
              {{ gnssData.surveyIn.valid ? 'COMPLETED' :
                 gnssData.surveyIn.active ? 'ACTIVE' : 'INACTIVE' }}
            </span>
          </div>
          <div class="flex justify-between">
            <span class="text-sm text-gray-600">Survey Duration:</span>
            <span class="text-sm font-medium text-gray-800">{{ gnssData.surveyIn.duration !== null ? gnssData.surveyIn.duration + 's' : '—' }}</span>
          </div>
          <div class="flex justify-between">
            <span class="text-sm text-gray-600">Survey Accuracy:</span>
            <span class="text-sm font-medium text-gray-800">{{ gnssData.surveyIn.accuracyMm !== null ? (gnssData.surveyIn.accuracyMm / 1000).toFixed(2) + 'm' : '—' }}</span>
          </div>
        </div>

        <!-- Reference Station Position -->
        <div class="space-y-3">
          <div class="text-sm font-semibold text-gray-800">Reference Position</div>
          <div class="space-y-2">
            <div class="flex justify-between">
              <span class="text-sm text-gray-600">Latitude:</span>
              <span class="text-sm font-medium" :class="gnssData.referenceStation.latitude !== null ? 'text-gray-800' : 'text-slate-400'">
                {{ gnssData.referenceStation.latitude !== null ? gnssData.referenceStation.latitude.toFixed(6) + '°' : '—' }}
              </span>
            </div>
            <div class="flex justify-between">
              <span class="text-sm text-gray-600">Longitude:</span>
              <span class="text-sm font-medium" :class="gnssData.referenceStation.longitude !== null ? 'text-gray-800' : 'text-slate-400'">
                {{ gnssData.referenceStation.longitude !== null ? gnssData.referenceStation.longitude.toFixed(6) + '°' : '—' }}
              </span>
            </div>
            <div class="flex justify-between">
              <span class="text-sm text-gray-600">Elevation:</span>
              <span class="text-sm font-medium" :class="gnssData.referenceStation.altitude !== null ? 'text-gray-800' : 'text-slate-400'">
                {{ gnssData.referenceStation.altitude !== null ? gnssData.referenceStation.altitude.toFixed(1) + 'm' : '—' }}
              </span>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Disabled State Message -->
    <div v-else class="text-center py-6">
      <div class="text-sm text-gray-600 mb-2">RTK Corrections Disabled</div>
      <div class="text-sm text-gray-500">Select Base Station or Rover mode above to enable RTK corrections</div>
    </div>
  </Card>
</template>