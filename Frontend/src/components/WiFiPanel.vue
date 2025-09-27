<script setup>
import { ref, inject, onMounted, onUnmounted, watch } from 'vue'
import Card from './common/Card.vue'

// SignalR connection
const signalrConnection = inject('signalrConnection')

// WiFi state
const wifiStatus = ref({
  currentMode: 'Disconnected',
  connectedNetworkSSID: null,
  signalStrength: null,
  isConnected: false,
  lastUpdated: null
})

const knownNetworks = ref([])
const fallbackNotification = ref(null)

// Mode Preference
const preferredMode = ref(null)

// AP Configuration
const apConfig = ref({
  ssid: '',
  password: ''
})
const originalAPPassword = ref('')
const isAPPasswordModified = ref(false)

// Dialog state
const showAddNetworkDialog = ref(false)
const dialogNetworkConfig = ref({
  ssid: '',
  password: ''
})
const showDialogPassword = ref(false)

// UI State
const isConnecting = ref(false)
const showAPPassword = ref(false)
const showNetworkPasswords = ref({})

// Connection handlers
let statusUpdateHandler = null
let fallbackNotificationHandler = null
let knownNetworksUpdateHandler = null

const setupSignalRHandlers = async () => {
  statusUpdateHandler = (data) => {
    console.log('WiFi status update:', data)
    wifiStatus.value = {
      currentMode: data.currentMode,
      connectedNetworkSSID: data.connectedNetworkSSID,
      signalStrength: data.signalStrength,
      isConnected: data.isConnected,
      lastUpdated: data.lastUpdated
    }
  }

  fallbackNotificationHandler = (data) => {
    console.log('WiFi fallback notification:', data)
    fallbackNotification.value = {
      message: data.message,
      reason: data.reason,
      timestamp: data.timestamp
    }
    
    // Auto-hide notification after 10 seconds
    setTimeout(() => {
      fallbackNotification.value = null
    }, 10000)
  }

  knownNetworksUpdateHandler = (data) => {
    console.log('Known networks update:', data)
    knownNetworks.value = data.networks || []
  }

  signalrConnection.value.on("WiFiStatusUpdate", statusUpdateHandler)
  signalrConnection.value.on("WiFiFallbackNotification", fallbackNotificationHandler)
  signalrConnection.value.on("WiFiKnownNetworksUpdate", knownNetworksUpdateHandler)
}

const loadInitialData = async () => {
  try {
    console.log('Loading WiFi status...')
    const status = await signalrConnection.value.invoke('GetWiFiStatus')
    wifiStatus.value = status
    console.log('WiFi status loaded:', status)

    console.log('Loading known networks...')
    const networks = await signalrConnection.value.invoke('GetKnownNetworks')
    knownNetworks.value = networks || []
    console.log('Known networks loaded:', networks)

    console.log('Loading preferred mode...')
    const mode = await signalrConnection.value.invoke('GetWiFiPreferredMode')
    console.log('Preferred mode received:', mode)
    preferredMode.value = mode

    console.log('Loading AP configuration...')
    const apConfiguration = await signalrConnection.value.invoke('GetAPConfiguration')
    console.log('AP configuration received:', apConfiguration)
    apConfig.value = {
      ssid: apConfiguration.ssid || '',
      password: apConfiguration.password || ''
    }
    originalAPPassword.value = apConfiguration.password || ''

    console.log('All initial WiFi data loaded successfully:', { status, networks, preferredMode: mode, apConfig: apConfiguration })
  } catch (error) {
    console.error('Error loading initial WiFi data:', error)
    console.error('Error details:', error.message, error.stack)
    throw error // Don't hide the error
  }
}

const initializeWiFiData = async () => {
  if (signalrConnection.value?.state === 'Connected' && preferredMode.value === null) {
    console.log('Initializing WiFi data...')
    await setupSignalRHandlers()
    await loadInitialData()
  }
}

onMounted(async () => {
  console.log('WiFiPanel mounted, SignalR connection available:', !!signalrConnection.value)
  await initializeWiFiData()
})

// Watch for SignalR connection to become available - only initialize once
watch(signalrConnection, async (newConnection, oldConnection) => {
  console.log('WiFiPanel - SignalR connection changed:', !!newConnection, 'state:', newConnection?.state)

  if (newConnection && preferredMode.value === null) {
    // Set up a one-time connected handler instead of polling state
    if (newConnection.state === 'Connected') {
      console.log('Connection already connected, initializing WiFi data...')
      await initializeWiFiData()
    } else {
      console.log('Waiting for connection to be established...')
      // Use a simple retry approach instead of onconnected to avoid conflicts with App.vue
      const checkAndInitialize = async () => {
        if (newConnection.state === 'Connected' && preferredMode.value === null) {
          console.log('Connection now ready, initializing WiFi data...')
          await initializeWiFiData()
        } else if (preferredMode.value === null) {
          // Retry after a short delay
          setTimeout(checkAndInitialize, 100)
        }
      }

      // Start checking after a brief delay to let the connection establish
      setTimeout(checkAndInitialize, 100)
    }
  }
}, { immediate: true })

onUnmounted(() => {
  if (signalrConnection.value) {
    signalrConnection.value.off("WiFiStatusUpdate", statusUpdateHandler)
    signalrConnection.value.off("WiFiFallbackNotification", fallbackNotificationHandler)
    signalrConnection.value.off("WiFiKnownNetworksUpdate", knownNetworksUpdateHandler)
  }
})

const addNetwork = async () => {
  if (!dialogNetworkConfig.value.ssid || !dialogNetworkConfig.value.password) {
    return
  }

  isConnecting.value = true

  try {
    const success = await signalrConnection.value.invoke('ConnectToWiFi', dialogNetworkConfig.value.ssid, dialogNetworkConfig.value.password)

    if (success) {
      console.log('Network connection initiated successfully')
      closeAddNetworkDialog()
    } else {
      console.error('Network connection failed')
    }
  } catch (error) {
    console.error('Error connecting to network:', error)
  } finally {
    isConnecting.value = false
  }
}

const closeAddNetworkDialog = () => {
  showAddNetworkDialog.value = false
  dialogNetworkConfig.value.ssid = ''
  dialogNetworkConfig.value.password = ''
  showDialogPassword.value = false
}


const removeKnownNetwork = async (ssid) => {
  try {
    const success = await signalrConnection.value.invoke('RemoveKnownNetwork', ssid)
    
    if (success) {
      console.log('Known network removed:', ssid)
    } else {
      console.error('Failed to remove known network:', ssid)
    }
  } catch (error) {
    console.error('Error removing known network:', error)
  }
}

const saveAPPassword = async () => {
  try {
    const success = await signalrConnection.value.invoke('SetAPConfiguration', apConfig.value.ssid, apConfig.value.password)

    if (success) {
      console.log('AP password updated successfully')
      originalAPPassword.value = apConfig.value.password
      isAPPasswordModified.value = false
    } else {
      console.error('AP password update failed')
    }
  } catch (error) {
    console.error('Error updating AP password:', error)
  }
}

const cancelAPPasswordEdit = () => {
  apConfig.value.password = originalAPPassword.value
  isAPPasswordModified.value = false
}

const setPreferredMode = async () => {
  try {
    const success = await signalrConnection.value.invoke('SetWiFiPreferredMode', preferredMode.value)

    if (success) {
      console.log('WiFi preferred mode updated successfully to:', preferredMode.value)
    } else {
      console.error('WiFi preferred mode update failed')
    }
  } catch (error) {
    console.error('Error setting WiFi preferred mode:', error)
  }
}

const toggleNetworkPassword = (ssid) => {
  showNetworkPasswords.value[ssid] = !showNetworkPasswords.value[ssid]
}


const formatLastConnected = (timestamp) => {
  if (!timestamp) return 'Never'
  const date = new Date(timestamp)
  return date.toLocaleDateString() + ' ' + date.toLocaleTimeString([], {hour: '2-digit', minute:'2-digit'})
}
</script>

<template>
  <div class="main-container">
    <!-- Header Section -->
    <div class="panel">
      <div class="panel-header">
        <h1 class="text-xl font-semibold text-gray-900">Network Configuration</h1>
        <p class="panel-subtitle">Manage WiFi connectivity and access point settings</p>
      </div>

      <!-- Fallback Notification -->
      <div v-if="fallbackNotification" class="mx-6 mt-4 p-4 bg-gray-50 border-l-4 border-gray-400 rounded-r-md">
        <div class="flex items-center">
          <svg class="w-5 h-5 mr-3 text-gray-500" fill="currentColor" viewBox="0 0 20 20">
            <path fill-rule="evenodd" d="M8.257 3.099c.765-1.36 2.722-1.36 3.486 0l5.58 9.92c.75 1.334-.213 2.98-1.742 2.98H4.42c-1.53 0-2.493-1.646-1.743-2.98l5.58-9.92zM11 13a1 1 0 11-2 0 1 1 0 012 0zm-1-8a1 1 0 00-1 1v3a1 1 0 002 0V6a1 1 0 00-1-1z" clip-rule="evenodd"></path>
          </svg>
          <span class="font-medium text-gray-700">{{ fallbackNotification.message }}</span>
        </div>
      </div>

      <!-- Current Status Section -->
      <div class="panel-content">
        <div class="flex items-center justify-between mb-4">
          <h2 class="text-lg font-medium text-gray-900">Connection Status</h2>
          <div class="flex items-center space-x-2">
            <div class="w-2 h-2 rounded-full" :class="wifiStatus.isConnected ? 'bg-gray-900' : 'bg-gray-400'"></div>
            <span class="text-sm font-medium" :class="wifiStatus.isConnected ? 'text-gray-900' : 'text-gray-500'">
              {{ wifiStatus.isConnected ? 'Connected' : 'Disconnected' }}
            </span>
          </div>
        </div>

        <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-6">
          <div class="bg-gray-50 rounded-lg p-4">
            <div class="text-xs font-medium text-gray-500 uppercase tracking-wide mb-1">Mode</div>
            <div class="text-sm font-semibold text-gray-900">
              {{ wifiStatus.currentMode || 'Disconnected' }}
            </div>
          </div>

          <div v-if="wifiStatus.connectedNetworkSSID" class="bg-gray-50 rounded-lg p-4">
            <div class="text-xs font-medium text-gray-500 uppercase tracking-wide mb-1">Network</div>
            <div class="text-sm font-semibold text-gray-900 truncate">{{ wifiStatus.connectedNetworkSSID }}</div>
          </div>

          <div v-if="wifiStatus.signalStrength" class="bg-gray-50 rounded-lg p-4">
            <div class="text-xs font-medium text-gray-500 uppercase tracking-wide mb-1">Signal Strength</div>
            <div class="flex items-center space-x-2">
              <div class="text-sm font-semibold text-gray-900">{{ wifiStatus.signalStrength }}%</div>
              <div class="flex space-x-1">
                <div class="w-1 h-3 rounded-full" :class="wifiStatus.signalStrength >= 25 ? 'bg-gray-900' : 'bg-gray-300'"></div>
                <div class="w-1 h-3 rounded-full" :class="wifiStatus.signalStrength >= 50 ? 'bg-gray-900' : 'bg-gray-300'"></div>
                <div class="w-1 h-3 rounded-full" :class="wifiStatus.signalStrength >= 75 ? 'bg-gray-900' : 'bg-gray-300'"></div>
                <div class="w-1 h-3 rounded-full" :class="wifiStatus.signalStrength >= 90 ? 'bg-gray-900' : 'bg-gray-300'"></div>
              </div>
            </div>
          </div>

          <div class="bg-gray-50 rounded-lg p-4">
            <div class="text-xs font-medium text-gray-500 uppercase tracking-wide mb-1">Status</div>
            <div class="text-sm font-semibold" :class="wifiStatus.isConnected ? 'text-gray-900' : 'text-gray-500'">
              {{ wifiStatus.isConnected ? 'Online' : 'Offline' }}
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Mode Configuration -->
    <div class="panel">
      <div class="panel-header">
        <h2 class="panel-title">Operating Mode</h2>
        <p class="panel-subtitle">Select the preferred WiFi operating mode for this device</p>
      </div>

      <div class="panel-content">
        <div v-if="preferredMode === null" class="text-gray-500 text-sm flex items-center">
          <svg class="animate-spin -ml-1 mr-3 h-4 w-4 text-gray-400" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
            <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
            <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
          </svg>
          Loading configuration...
        </div>

        <div v-else class="space-y-4">
          <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
            <label class="relative cursor-pointer">
              <input
                v-model="preferredMode"
                type="radio"
                value="Client"
                @change="setPreferredMode"
                class="sr-only"
              />
              <div class="p-4 rounded-lg border-2 transition-all duration-200"
                   :class="preferredMode === 'Client' ? 'border-gray-900 bg-gray-50' : 'border-gray-200 hover:border-gray-300'">
                <div class="flex items-center justify-between">
                  <div>
                    <div class="font-medium text-gray-900">Client Mode</div>
                    <div class="text-xs text-gray-500 mt-1">
                      Will try to connect to known networks first, falling back to AP mode if connection fails.
                    </div>
                  </div>
                  <div class="w-4 h-4 rounded-full border-2 flex items-center justify-center"
                       :class="preferredMode === 'Client' ? 'border-gray-900' : 'border-gray-300'">
                    <div v-if="preferredMode === 'Client'" class="w-2 h-2 rounded-full bg-gray-900"></div>
                  </div>
                </div>
              </div>
            </label>

            <label class="relative cursor-pointer">
              <input
                v-model="preferredMode"
                type="radio"
                value="AP"
                @change="setPreferredMode"
                class="sr-only"
              />
              <div class="p-4 rounded-lg border-2 transition-all duration-200"
                   :class="preferredMode === 'AP' ? 'border-gray-900 bg-gray-50' : 'border-gray-200 hover:border-gray-300'">
                <div class="flex items-center justify-between">
                  <div>
                    <div class="font-medium text-gray-900">Access Point Mode</div>
                    <div class="text-xs text-gray-500 mt-1">
                      Will start directly in Access Point mode, creating a WiFi hotspot for other devices to connect to.
                    </div>
                  </div>
                  <div class="w-4 h-4 rounded-full border-2 flex items-center justify-center"
                       :class="preferredMode === 'AP' ? 'border-gray-900' : 'border-gray-300'">
                    <div v-if="preferredMode === 'AP'" class="w-2 h-2 rounded-full bg-gray-900"></div>
                  </div>
                </div>
              </div>
            </label>
          </div>

          <!-- AP Configuration Form - always show -->
          <div class="space-y-4 pt-4 border-t border-gray-200">
            <div class="text-sm font-medium text-gray-900 mb-3">Access Point Settings</div>

            <div>
              <label class="form-label">Network Name (SSID)</label>
              <div class="form-input-readonly">
                {{ apConfig.ssid || 'Loading...' }}
              </div>
              <div class="form-helper-text">Auto-generated from device name</div>
            </div>

            <div>
              <label class="form-label">Password</label>
              <div class="relative">
                <input
                  v-model="apConfig.password"
                  :type="showAPPassword ? 'text' : 'password'"
                  class="form-input pr-20"
                  placeholder="Enter access point password"
                  @input="isAPPasswordModified = apConfig.password !== originalAPPassword"
                />
                <div class="absolute inset-y-0 right-0 flex items-center">
                  <button
                    @click="showAPPassword = !showAPPassword"
                    type="button"
                    class="px-2 btn-icon"
                  >
                    <svg v-if="showAPPassword" class="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
                      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z" />
                    </svg>
                    <svg v-else class="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M13.875 18.825A10.05 10.05 0 0112 19c-4.478 0-8.268-2.943-9.543-7a9.97 9.97 0 711.563-3.029m5.858.908a3 3 0 114.243 4.243M9.878 9.878l4.242 4.242M9.878 9.878L8.115 8.114m0 0L6.937 6.937m0 0a8.977 8.977 0 00-1.942-.845m1.942.845A8.977 8.977 0 0012 5c2.14 0 4.135.601 5.828 1.635l-1.937 1.937m0 0a3 3 0 00-4.243 4.243l1.937-1.937z" />
                    </svg>
                  </button>
                  <!-- Accept/Cancel buttons - only show when modified -->
                  <div v-if="isAPPasswordModified" class="flex space-x-1 pr-2">
                    <button
                      @click="saveAPPassword"
                      class="p-1 bg-green-600 text-white rounded hover:bg-green-700 focus:outline-none focus:ring-2 focus:ring-green-500 focus:ring-offset-2"
                      title="Save changes"
                    >
                      <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5 13l4 4L19 7"></path>
                      </svg>
                    </button>
                    <button
                      @click="cancelAPPasswordEdit"
                      class="p-1 bg-gray-500 text-white rounded hover:bg-gray-600 focus:outline-none focus:ring-2 focus:ring-gray-500 focus:ring-offset-2"
                      title="Cancel changes"
                    >
                      <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12"></path>
                      </svg>
                    </button>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>


    <!-- Known Networks -->
    <div class="panel">
      <div class="panel-header">
        <div class="flex items-center justify-between">
          <div>
            <h2 class="panel-title">Saved Networks</h2>
            <p class="panel-subtitle">Manage your stored network credentials</p>
          </div>
          <button
            @click="showAddNetworkDialog = true"
            class="btn-primary"
          >
            Add Network
          </button>
        </div>
      </div>

      <div v-if="knownNetworks.length > 0" class="divide-y divide-gray-100">
        <div
          v-for="network in knownNetworks"
          :key="network.ssid"
          class="px-6 py-4"
        >
          <div class="flex items-center justify-between">
            <div class="flex-1 min-w-0">
              <div class="font-medium text-gray-900 truncate">{{ network.ssid }}</div>
              <div class="text-sm text-gray-500">
                Last connected: {{ formatLastConnected(network.lastConnected) }}
              </div>
            </div>
            <div class="ml-4">
              <button
                @click="removeKnownNetwork(network.ssid)"
                class="btn-danger"
              >
                Remove
              </button>
            </div>
          </div>
        </div>
      </div>

      <div v-else class="px-6 py-8 text-center">
        <svg class="mx-auto h-12 w-12 text-gray-400" fill="none" viewBox="0 0 24 24" stroke="currentColor">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M8.111 16.404a5.5 5.5 0 017.778 0M12 20h.01m-7.08-7.071c3.904-3.905 10.236-3.905 14.141 0M1.394 9.393c5.857-5.857 15.355-5.857 21.213 0" />
        </svg>
        <h3 class="mt-4 text-sm font-medium text-gray-900">No saved networks</h3>
        <p class="mt-2 text-sm text-gray-500">Get started by adding your first network.</p>
      </div>
    </div>

    <!-- Add Network Dialog -->
    <div v-if="showAddNetworkDialog" class="fixed inset-0 z-50 flex items-center justify-center p-4">
      <!-- Background overlay -->
      <div class="fixed inset-0 bg-black bg-opacity-50 transition-opacity" @click="closeAddNetworkDialog"></div>

      <!-- Modal panel -->
      <div class="relative bg-white border border-gray-200 rounded-lg shadow-xl max-w-md w-full max-h-screen overflow-y-auto">
        <!-- Header -->
        <div class="px-6 py-4 border-b border-gray-100">
          <div class="flex items-center justify-between">
            <h2 class="text-lg font-medium text-gray-900">Add Network</h2>
            <button
              @click="closeAddNetworkDialog"
              type="button"
              class="btn-icon"
              :disabled="isConnecting"
            >
              <svg class="w-6 h-6" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" />
              </svg>
            </button>
          </div>
          <p class="text-sm text-gray-600 mt-1">Enter network credentials to connect</p>
        </div>

        <!-- Content -->
        <div class="px-6 py-5 space-y-4">
          <div>
            <label class="block text-xs font-medium text-gray-500 uppercase tracking-wide mb-2">Network Name (SSID)</label>
            <input
              v-model="dialogNetworkConfig.ssid"
              type="text"
              class="w-full px-4 py-3 border border-gray-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-gray-900 focus:border-transparent transition-all duration-200"
              placeholder="Enter network name"
              :disabled="isConnecting"
            />
          </div>

          <div>
            <label class="block text-xs font-medium text-gray-500 uppercase tracking-wide mb-2">Password</label>
            <div class="relative">
              <input
                v-model="dialogNetworkConfig.password"
                :type="showDialogPassword ? 'text' : 'password'"
                class="w-full px-4 py-3 pr-12 border border-gray-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-gray-900 focus:border-transparent transition-all duration-200"
                placeholder="Enter network password"
                :disabled="isConnecting"
              />
              <button
                @click="showDialogPassword = !showDialogPassword"
                type="button"
                class="absolute inset-y-0 right-0 pr-4 flex items-center btn-icon"
              >
                <svg v-if="showDialogPassword" class="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                  <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
                  <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z" />
                </svg>
                <svg v-else class="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                  <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M13.875 18.825A10.05 10.05 0 0112 19c-4.478 0-8.268-2.943-9.543-7a9.97 9.97 0 011.563-3.029m5.858.908a3 3 0 114.243 4.243M9.878 9.878l4.242 4.242M9.878 9.878L8.115 8.114m0 0L6.937 6.937m0 0a8.977 8.977 0 00-1.942-.845m1.942.845A8.977 8.977 0 0012 5c2.14 0 4.135.601 5.828 1.635l-1.937 1.937m0 0a3 3 0 00-4.243 4.243l1.937-1.937z" />
                </svg>
              </button>
            </div>
          </div>
        </div>

        <!-- Footer -->
        <div class="px-6 py-4 border-t border-gray-100 flex justify-end space-x-3">
          <button
            @click="closeAddNetworkDialog"
            type="button"
            :disabled="isConnecting"
            class="btn-secondary"
          >
            Cancel
          </button>
          <button
            @click="addNetwork"
            :disabled="isConnecting || !dialogNetworkConfig.ssid || !dialogNetworkConfig.password"
            class="btn-primary"
          >
            <div v-if="isConnecting" class="flex items-center">
              <svg class="animate-spin -ml-1 mr-2 h-4 w-4 text-white" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
              </svg>
              Connecting...
            </div>
            <span v-else>Connect to Network</span>
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

