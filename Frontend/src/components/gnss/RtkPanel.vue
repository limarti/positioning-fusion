<script setup>
import { ref, inject, computed, watch, onMounted } from 'vue'

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
  <div class="bg-white rounded-xl border border-slate-200 p-4 break-inside-avoid mb-6">
    <!-- Header -->
    <div class="flex items-center justify-between mb-4">
      <div class="flex items-center space-x-3">
        <svg class="w-6 h-6" :class="{
          'text-slate-400': currentModeConfig.color === 'slate',
          'text-blue-600': currentModeConfig.color === 'blue',
          'text-purple-600': currentModeConfig.color === 'purple'
        }" fill="currentColor" viewBox="0 0 24 24">
          <path d="M12,2A3,3 0 0,1 15,5V11A3,3 0 0,1 12,14A3,3 0 0,1 9,11V5A3,3 0 0,1 12,2M19,11C19,14.53 16.39,17.44 13,17.93V21H11V17.93C7.61,17.44 5,14.53 5,11H7A5,5 0 0,0 12,16A5,5 0 0,0 17,11H19Z"/>
        </svg>
        <h2 class="text-lg font-bold" :class="gnssData.corrections.mode === 'Disabled' ? 'text-slate-400' : 'text-slate-800'">RTK & Mode Selection</h2>
      </div>

      <!-- Current Mode Badge -->
      <span class="text-sm font-bold px-3 py-1 rounded-lg" :class="{
        'bg-slate-100 text-slate-700': currentModeConfig.color === 'slate',
        'bg-blue-100 text-blue-700': currentModeConfig.color === 'blue',
        'bg-purple-100 text-purple-700': currentModeConfig.color === 'purple'
      }">
        {{ currentModeConfig.label.toUpperCase() }}
      </span>
    </div>

    <!-- Mode Selection Section -->
    <div class="mb-6 p-4 bg-slate-50 rounded-xl">
      <div class="text-sm font-semibold text-slate-700 mb-3">Operating Mode</div>

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
              class="h-4 w-4 border-slate-300 focus:ring-2"
              :class="{
                'text-slate-600 focus:ring-slate-500': option.color === 'slate',
                'text-blue-600 focus:ring-blue-500': option.color === 'blue',
                'text-purple-600 focus:ring-purple-500': option.color === 'purple'
              }"
              @change="handleModeChange(option.value)"
            />
          </div>
          <div class="ml-3 text-sm">
            <label :for="option.value" class="font-medium text-slate-700 cursor-pointer">
              {{ option.label }}
            </label>
            <p class="text-slate-500">{{ option.description }}</p>
          </div>
        </div>
      </div>

      <!-- Loading State -->
      <div v-if="isChangingMode" class="mt-4 p-3 bg-white rounded-lg flex items-center justify-center">
        <div class="flex items-center space-x-2 text-slate-600">
          <svg class="animate-spin h-4 w-4" fill="none" viewBox="0 0 24 24">
            <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
            <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
          </svg>
          <span class="text-sm">Changing mode...</span>
        </div>
      </div>
    </div>

    <!-- RTK Status and Data (when active) -->
    <div v-if="gnssData.corrections.mode !== 'Disabled'" class="space-y-4">
      <!-- RTK Quality Status -->
      <div class="flex items-center justify-between p-3 bg-slate-50 rounded-lg">
        <span class="text-slate-600">RTK Status:</span>
        <span class="font-bold px-3 py-1 rounded-lg text-sm"
              :class="gnssData.rtkMode === 'Fixed' ? 'bg-emerald-100 text-emerald-700' : 'bg-yellow-100 text-yellow-700'">
          {{ gnssData.rtkMode }}
        </span>
      </div>

      <!-- Quality Metrics Grid -->
      <div class="grid grid-cols-2 gap-4">
        <div class="text-center p-3 bg-emerald-50 rounded-xl">
          <div class="text-xs text-slate-600 mb-1">AR Ratio</div>
          <div class="text-lg font-bold" :class="gnssData.rtk.arRatio !== null ? 'text-emerald-700' : 'text-slate-400'">{{ gnssData.rtk.arRatio !== null ? gnssData.rtk.arRatio.toFixed(1) : '—' }}</div>
        </div>
        <div class="text-center p-3 bg-blue-50 rounded-xl">
          <div class="text-xs text-slate-600 mb-1">Correction Age</div>
          <div class="text-lg font-bold" :class="gnssData.rtk.correctionAge !== null ? 'text-blue-700' : 'text-slate-400'">{{ gnssData.rtk.correctionAge !== null ? gnssData.rtk.correctionAge.toFixed(1) + 's' : '—' }}</div>
        </div>
      </div>

      <!-- Throughput Indicators -->
      <div class="grid grid-cols-2 gap-4">
        <!-- Base Station Mode: Show LoRa Out (corrections being sent) -->
        <div v-if="gnssData.corrections.mode === 'Send'" class="text-center p-3 bg-orange-50 rounded-xl">
          <div class="text-xs text-slate-600 mb-1">LoRa Throughput (Out)</div>
          <div class="text-lg font-bold" :class="dataRates.kbpsLoRaOut !== null && dataRates.kbpsLoRaOut > 0 ? 'text-orange-700' : 'text-slate-400'">
            {{ dataRates.kbpsLoRaOut !== null ? dataRates.kbpsLoRaOut.toFixed(1) + ' kbps' : '—' }}
          </div>
          <div class="text-xs text-slate-500 mt-1" v-if="dataRates.kbpsLoRaOut === 0">No corrections sent</div>
        </div>
        <!-- Rover Mode: Show LoRa In (corrections being received) -->
        <div v-if="gnssData.corrections.mode === 'Receive'" class="text-center p-3 bg-purple-50 rounded-xl">
          <div class="text-xs text-slate-600 mb-1">LoRa Throughput (In)</div>
          <div class="text-lg font-bold" :class="dataRates.kbpsLoRaIn !== null && dataRates.kbpsLoRaIn > 0 ? 'text-purple-700' : 'text-slate-400'">
            {{ dataRates.kbpsLoRaIn !== null ? dataRates.kbpsLoRaIn.toFixed(1) + ' kbps' : '—' }}
          </div>
          <div class="text-xs text-slate-500 mt-1" v-if="dataRates.kbpsLoRaIn === 0">No corrections received</div>
        </div>
        <!-- Fill empty space when mode is Send or Receive to maintain layout -->
        <div v-if="gnssData.corrections.mode === 'Send' || gnssData.corrections.mode === 'Receive'" class="text-center p-3 bg-slate-50 rounded-xl">
          <div class="text-xs text-slate-600 mb-1">—</div>
          <div class="text-lg font-bold text-slate-400">—</div>
        </div>
      </div>

      <!-- Baseline and Accuracy -->
      <div class="space-y-2">
        <div class="flex justify-between p-3 bg-slate-50 rounded-lg">
          <span class="text-slate-600">Baseline Length:</span>
          <span class="font-bold" :class="gnssData.rtk.baselineLength !== null ? '' : 'text-slate-400'">{{ gnssData.rtk.baselineLength !== null ? gnssData.rtk.baselineLength.toFixed(0) + 'm' : '—' }}</span>
        </div>
        <div class="p-3 bg-slate-50 rounded-lg">
          <div class="text-sm text-slate-600 mb-2">Relative Accuracy</div>
          <div class="grid grid-cols-3 gap-2 text-sm">
            <div class="text-center">
              <div class="font-mono font-semibold" :class="gnssData.rtk.relativeAccuracy.north !== null ? '' : 'text-slate-400'">{{ gnssData.rtk.relativeAccuracy.north !== null ? gnssData.rtk.relativeAccuracy.north.toFixed(3) + 'm' : '—' }}</div>
              <div class="text-slate-500">North</div>
            </div>
            <div class="text-center">
              <div class="font-mono font-semibold" :class="gnssData.rtk.relativeAccuracy.east !== null ? '' : 'text-slate-400'">{{ gnssData.rtk.relativeAccuracy.east !== null ? gnssData.rtk.relativeAccuracy.east.toFixed(3) + 'm' : '—' }}</div>
              <div class="text-slate-500">East</div>
            </div>
            <div class="text-center">
              <div class="font-mono font-semibold" :class="gnssData.rtk.relativeAccuracy.down !== null ? '' : 'text-slate-400'">{{ gnssData.rtk.relativeAccuracy.down !== null ? gnssData.rtk.relativeAccuracy.down.toFixed(3) + 'm' : '—' }}</div>
              <div class="text-slate-500">Down</div>
            </div>
          </div>
        </div>
      </div>

      <!-- Survey-In Status (Base Station Mode) -->
      <div v-if="gnssData.corrections.mode === 'Send'" class="bg-blue-50 rounded-xl p-4 border border-blue-200">
        <div class="text-sm font-semibold text-blue-800 mb-3">Survey-In Status</div>

        <div class="space-y-2 text-sm mb-3">
          <div class="flex justify-between">
            <span class="text-blue-600">Status:</span>
            <span class="text-xs font-bold px-2 py-1 rounded-lg"
                  :class="gnssData.surveyIn.valid ? 'bg-emerald-100 text-emerald-700' :
                         gnssData.surveyIn.active ? 'bg-yellow-100 text-yellow-700' :
                         'bg-slate-100 text-slate-700'">
              {{ gnssData.surveyIn.valid ? '✅ COMPLETED' :
                 gnssData.surveyIn.active ? '📍 ACTIVE' : '❌ INACTIVE' }}
            </span>
          </div>
          <div class="flex justify-between">
            <span class="text-blue-600">Duration:</span>
            <span class="font-bold text-blue-800">{{ gnssData.surveyIn.duration !== null ? gnssData.surveyIn.duration + 's' : '—' }}</span>
          </div>
          <div class="flex justify-between">
            <span class="text-blue-600">Observations:</span>
            <span class="font-bold text-blue-800">{{ gnssData.surveyIn.observations !== null ? gnssData.surveyIn.observations : '—' }}</span>
          </div>
          <div class="flex justify-between">
            <span class="text-blue-600">Accuracy:</span>
            <span class="font-bold text-blue-800">{{ gnssData.surveyIn.accuracyMm !== null ? (gnssData.surveyIn.accuracyMm / 1000).toFixed(2) + 'm' : '—' }}</span>
          </div>
        </div>

        <!-- Divider -->
        <div class="border-t border-blue-200 my-3"></div>

        <!-- Reference Station Position -->
        <div>
          <div class="text-xs font-semibold text-blue-800 mb-2">Broadcasting Position</div>
          <div class="space-y-2 text-sm">
            <div class="flex justify-between">
              <span class="text-blue-600">Latitude:</span>
              <span class="font-mono font-semibold" :class="gnssData.referenceStation.latitude !== null ? 'text-blue-800' : 'text-slate-400'">
                {{ gnssData.referenceStation.latitude !== null ? gnssData.referenceStation.latitude.toFixed(8) + '°' : '—' }}
              </span>
            </div>
            <div class="flex justify-between">
              <span class="text-blue-600">Longitude:</span>
              <span class="font-mono font-semibold" :class="gnssData.referenceStation.longitude !== null ? 'text-blue-800' : 'text-slate-400'">
                {{ gnssData.referenceStation.longitude !== null ? gnssData.referenceStation.longitude.toFixed(8) + '°' : '—' }}
              </span>
            </div>
            <div class="flex justify-between">
              <span class="text-blue-600">Altitude:</span>
              <span class="font-mono font-semibold" :class="gnssData.referenceStation.altitude !== null ? 'text-blue-800' : 'text-slate-400'">
                {{ gnssData.referenceStation.altitude !== null ? gnssData.referenceStation.altitude.toFixed(3) + 'm' : '—' }}
              </span>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Disabled State Message -->
    <div v-else class="text-center p-6 bg-slate-50 rounded-xl">
      <div class="text-slate-500 mb-2">RTK Corrections Disabled</div>
      <div class="text-sm text-slate-400">Select Base Station or Rover mode above to enable RTK corrections</div>
    </div>
  </div>
</template>