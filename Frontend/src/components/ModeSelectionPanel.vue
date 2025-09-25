<script setup>
import { ref, inject, computed, watch, onMounted } from 'vue'

const props = defineProps({
  currentMode: {
    type: String,
    default: 'Disabled'
  }
})

const emit = defineEmits(['modeChanged'])

const connection = inject('signalrConnection')
const isChangingMode = ref(false)
const selectedMode = ref(props.currentMode)

// Update selectedMode when currentMode prop changes
watch(() => props.currentMode, (newMode) => {
  console.log(`ModeSelectionPanel - currentMode prop changed from ${selectedMode.value} to ${newMode}`)
  selectedMode.value = newMode
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

const currentModeConfig = computed(() => getModeConfig(props.currentMode))

onMounted(() => {
  console.log('ModeSelectionPanel mounted')
  console.log('Initial props.currentMode:', props.currentMode)
  console.log('Initial selectedMode:', selectedMode.value)
  console.log('Connection available:', !!connection?.value)
  console.log('Connection object:', connection?.value)
  console.log('Connection state:', connection?.value?.state)

  // Watch for connection changes
  watch(connection, (newConnection) => {
    console.log('Connection changed in ModeSelectionPanel:', newConnection)
    console.log('New connection state:', newConnection?.state)
  }, { immediate: true })
})

const handleModeChange = async (newMode) => {
  console.log('handleModeChange called with:', newMode)
  console.log('Current mode:', props.currentMode)
  console.log('Is changing mode:', isChangingMode.value)
  console.log('Connection object:', connection?.value)
  console.log('Connection status:', connection?.value?.state)

  if (newMode === props.currentMode || isChangingMode.value) {
    console.log('Skipping mode change - same mode or already changing')
    return
  }

  isChangingMode.value = true
  console.log('Starting mode change process...')

  try {
    if (connection?.value && connection.value.state === 'Connected') {
      console.log('Invoking SetOperatingMode on SignalR connection...')
      const success = await connection.value.invoke('SetOperatingMode', newMode)
      console.log('SetOperatingMode result:', success)

      if (success) {
        console.log(`Mode changed successfully to: ${newMode}`)
        emit('modeChanged', newMode)
      } else {
        console.error('Failed to change mode - server returned false')
        // Reset selected mode on failure
        selectedMode.value = props.currentMode
      }
    } else {
      console.error('No SignalR connection available or connection not in Connected state')
      console.error('Connection state:', connection?.value?.state)
      selectedMode.value = props.currentMode
    }
  } catch (error) {
    console.error('Error changing mode:', error)
    console.error('Error details:', error.message, error.stack)
    // Reset selected mode on error
    selectedMode.value = props.currentMode
  } finally {
    console.log('Mode change process completed')
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
          'text-slate-600': currentModeConfig.color === 'slate',
          'text-blue-600': currentModeConfig.color === 'blue',
          'text-purple-600': currentModeConfig.color === 'purple'
        }" fill="currentColor" viewBox="0 0 24 24">
          <path d="M12,2A10,10 0 0,1 22,12A10,10 0 0,1 12,22A10,10 0 0,1 2,12A10,10 0 0,1 12,2M12,4A8,8 0 0,0 4,12A8,8 0 0,0 12,20A8,8 0 0,0 20,12A8,8 0 0,0 12,4M12,6A6,6 0 0,1 18,12A6,6 0 0,1 12,18A6,6 0 0,1 6,12A6,6 0 0,1 12,6M12,8A4,4 0 0,0 8,12A4,4 0 0,0 12,16A4,4 0 0,0 16,12A4,4 0 0,0 12,8Z"/>
        </svg>
        <h2 class="text-lg font-bold text-slate-800">Mode Selection</h2>
      </div>

      <!-- Current Mode Status -->
      <span class="text-sm font-bold px-3 py-1 rounded-lg" :class="{
        'bg-slate-100 text-slate-700': currentModeConfig.color === 'slate',
        'bg-blue-100 text-blue-700': currentModeConfig.color === 'blue',
        'bg-purple-100 text-purple-700': currentModeConfig.color === 'purple'
      }">
        {{ currentModeConfig.label.toUpperCase() }}
      </span>
    </div>

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
    <div v-if="isChangingMode" class="mt-4 p-3 bg-slate-50 rounded-lg flex items-center justify-center">
      <div class="flex items-center space-x-2 text-slate-600">
        <svg class="animate-spin h-4 w-4" fill="none" viewBox="0 0 24 24">
          <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
          <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
        </svg>
        <span class="text-sm">Changing mode...</span>
      </div>
    </div>
  </div>
</template>