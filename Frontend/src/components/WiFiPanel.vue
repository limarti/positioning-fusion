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

// Client Connection
const clientConfig = ref({
  ssid: '',
  password: ''
})

// UI State
const isConnecting = ref(false)
const showAPPassword = ref(false)
const showClientPassword = ref(false)
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

const connectToWiFi = async () => {
  if (!clientConfig.value.ssid || !clientConfig.value.password) {
    return
  }

  isConnecting.value = true
  
  try {
    const success = await signalrConnection.value.invoke('ConnectToWiFi', clientConfig.value.ssid, clientConfig.value.password)
    
    if (success) {
      console.log('WiFi connection initiated successfully')
      clientConfig.value.ssid = ''
      clientConfig.value.password = ''
    } else {
      console.error('WiFi connection failed')
    }
  } catch (error) {
    console.error('Error connecting to WiFi:', error)
  } finally {
    isConnecting.value = false
  }
}

const connectToKnownNetwork = async (ssid) => {
  const network = knownNetworks.value.find(n => n.ssid === ssid)
  if (!network) return

  isConnecting.value = true
  
  try {
    const success = await signalrConnection.value.invoke('ConnectToWiFi', network.ssid, '') // Password stored on server
    
    if (success) {
      console.log('Connected to known network:', ssid)
    } else {
      console.error('Failed to connect to known network:', ssid)
    }
  } catch (error) {
    console.error('Error connecting to known network:', error)
  } finally {
    isConnecting.value = false
  }
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

const configureAP = async () => {
  try {
    const success = await signalrConnection.value.invoke('SetAPConfiguration', apConfig.value.ssid, apConfig.value.password)

    if (success) {
      console.log('AP configuration updated successfully')
    } else {
      console.error('AP configuration failed')
    }
  } catch (error) {
    console.error('Error configuring AP:', error)
  }
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

const getStatusColor = (mode) => {
  switch (mode) {
    case 'Client': return 'text-green-600'
    case 'AP': return 'text-blue-600'
    default: return 'text-gray-500'
  }
}

const getSignalStrengthColor = (strength) => {
  if (!strength) return 'text-gray-400'
  if (strength >= 70) return 'text-green-500'
  if (strength >= 50) return 'text-yellow-500'
  return 'text-red-500'
}

const formatLastConnected = (timestamp) => {
  if (!timestamp) return 'Never'
  const date = new Date(timestamp)
  return date.toLocaleDateString() + ' ' + date.toLocaleTimeString([], {hour: '2-digit', minute:'2-digit'})
}
</script>

<template>
  <Card>
    <template #title>WiFi Configuration</template>
    
    <!-- Fallback Notification -->
    <div v-if="fallbackNotification" class="mb-4 p-3 bg-yellow-100 border border-yellow-400 text-yellow-700 rounded-lg">
      <div class="flex items-center">
        <svg class="w-5 h-5 mr-2" fill="currentColor" viewBox="0 0 20 20">
          <path fill-rule="evenodd" d="M8.257 3.099c.765-1.36 2.722-1.36 3.486 0l5.58 9.92c.75 1.334-.213 2.98-1.742 2.98H4.42c-1.53 0-2.493-1.646-1.743-2.98l5.58-9.92zM11 13a1 1 0 11-2 0 1 1 0 012 0zm-1-8a1 1 0 00-1 1v3a1 1 0 002 0V6a1 1 0 00-1-1z" clip-rule="evenodd"></path>
        </svg>
        <span class="font-medium">{{ fallbackNotification.message }}</span>
      </div>
    </div>

    <!-- WiFi Mode Preference -->
    <div class="mb-6 p-4 border border-purple-200 bg-purple-50 rounded-lg">
      <h3 class="font-semibold mb-3 text-purple-800">WiFi Mode Preference</h3>
      <div v-if="preferredMode === null" class="text-gray-500 text-sm">
        Loading mode preference...
      </div>
      <div v-else class="flex items-center space-x-4">
        <div class="flex items-center">
          <input
            id="mode-client"
            v-model="preferredMode"
            type="radio"
            value="Client"
            @change="setPreferredMode"
            class="h-4 w-4 text-purple-600 focus:ring-purple-500 border-gray-300"
          />
          <label for="mode-client" class="ml-2 text-sm font-medium text-gray-700">
            Client Mode (Try to connect to networks first)
          </label>
        </div>
        <div class="flex items-center">
          <input
            id="mode-ap"
            v-model="preferredMode"
            type="radio"
            value="AP"
            @change="setPreferredMode"
            class="h-4 w-4 text-purple-600 focus:ring-purple-500 border-gray-300"
          />
          <label for="mode-ap" class="ml-2 text-sm font-medium text-gray-700">
            Access Point Mode (Start as AP immediately)
          </label>
        </div>
      </div>
      <div v-if="preferredMode" class="mt-2 text-xs text-gray-600">
        <span v-if="preferredMode === 'Client'">
          Will try to connect to known networks first, falling back to AP mode if connection fails.
        </span>
        <span v-if="preferredMode === 'AP'">
          Will start directly in Access Point mode, even if known networks are configured.
        </span>
      </div>
    </div>

    <!-- Current Status -->
    <div class="mb-6 p-4 bg-gray-50 rounded-lg">
      <h3 class="font-semibold mb-2">Current Status</h3>
      <div class="grid grid-cols-2 gap-4 text-sm">
        <div>
          <span class="text-gray-600">Mode:</span>
          <span :class="getStatusColor(wifiStatus.currentMode)" class="ml-2 font-medium">
            {{ wifiStatus.currentMode || 'Disconnected' }}
          </span>
        </div>
        <div>
          <span class="text-gray-600">Connected:</span>
          <span :class="wifiStatus.isConnected ? 'text-green-600' : 'text-red-600'" class="ml-2">
            {{ wifiStatus.isConnected ? 'Yes' : 'No' }}
          </span>
        </div>
        <div v-if="wifiStatus.connectedNetworkSSID">
          <span class="text-gray-600">Network:</span>
          <span class="ml-2 font-medium">{{ wifiStatus.connectedNetworkSSID }}</span>
        </div>
        <div v-if="wifiStatus.signalStrength">
          <span class="text-gray-600">Signal:</span>
          <span :class="getSignalStrengthColor(wifiStatus.signalStrength)" class="ml-2 font-medium">
            {{ wifiStatus.signalStrength }}%
          </span>
        </div>
      </div>
    </div>

    <!-- AP Configuration -->
    <div class="mb-6 p-4 border border-blue-200 bg-blue-50 rounded-lg">
      <h3 class="font-semibold mb-3 text-blue-800">Access Point Configuration</h3>
      <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
        <div>
          <label class="block text-sm font-medium text-gray-700 mb-1">SSID</label>
          <input
            v-model="apConfig.ssid"
            type="text"
            class="w-full px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-blue-500"
            placeholder="Network name"
          />
        </div>
        <div>
          <label class="block text-sm font-medium text-gray-700 mb-1">Password</label>
          <div class="relative">
            <input
              v-model="apConfig.password"
              :type="showAPPassword ? 'text' : 'password'"
              class="w-full px-3 py-2 pr-10 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-blue-500"
              placeholder="AP password"
            />
            <button
              @click="showAPPassword = !showAPPassword"
              type="button"
              class="absolute inset-y-0 right-0 pr-3 flex items-center"
            >
              <svg v-if="showAPPassword" class="h-5 w-5 text-gray-400" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z" />
              </svg>
              <svg v-else class="h-5 w-5 text-gray-400" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M13.875 18.825A10.05 10.05 0 0112 19c-4.478 0-8.268-2.943-9.543-7a9.97 9.97 0 011.563-3.029m5.858.908a3 3 0 114.243 4.243M9.878 9.878l4.242 4.242M9.878 9.878L8.115 8.114m0 0L6.937 6.937m0 0a8.977 8.977 0 00-1.942-.845m1.942.845A8.977 8.977 0 0012 5c2.14 0 4.135.601 5.828 1.635l-1.937 1.937m0 0a3 3 0 00-4.243 4.243l1.937-1.937z" />
              </svg>
            </button>
          </div>
        </div>
      </div>
      <button
        @click="configureAP"
        class="mt-3 px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-blue-500"
      >
        Update AP Configuration
      </button>
    </div>

    <!-- Client Connection -->
    <div class="mb-6 p-4 border border-green-200 bg-green-50 rounded-lg">
      <h3 class="font-semibold mb-3 text-green-800">Connect to Network</h3>
      <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
        <div>
          <label class="block text-sm font-medium text-gray-700 mb-1">SSID</label>
          <input
            v-model="clientConfig.ssid"
            type="text"
            class="w-full px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-green-500"
            placeholder="Network name"
            :disabled="isConnecting"
          />
        </div>
        <div>
          <label class="block text-sm font-medium text-gray-700 mb-1">Password</label>
          <div class="relative">
            <input
              v-model="clientConfig.password"
              :type="showClientPassword ? 'text' : 'password'"
              class="w-full px-3 py-2 pr-10 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-green-500"
              placeholder="Network password"
              :disabled="isConnecting"
            />
            <button
              @click="showClientPassword = !showClientPassword"
              type="button"
              class="absolute inset-y-0 right-0 pr-3 flex items-center"
            >
              <svg v-if="showClientPassword" class="h-5 w-5 text-gray-400" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z" />
              </svg>
              <svg v-else class="h-5 w-5 text-gray-400" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M13.875 18.825A10.05 10.05 0 0112 19c-4.478 0-8.268-2.943-9.543-7a9.97 9.97 0 011.563-3.029m5.858.908a3 3 0 114.243 4.243M9.878 9.878l4.242 4.242M9.878 9.878L8.115 8.114m0 0L6.937 6.937m0 0a8.977 8.977 0 00-1.942-.845m1.942.845A8.977 8.977 0 0012 5c2.14 0 4.135.601 5.828 1.635l-1.937 1.937m0 0a3 3 0 00-4.243 4.243l1.937-1.937z" />
              </svg>
            </button>
          </div>
        </div>
      </div>
      <button
        @click="connectToWiFi"
        :disabled="isConnecting || !clientConfig.ssid || !clientConfig.password"
        class="mt-3 px-4 py-2 bg-green-600 text-white rounded-md hover:bg-green-700 focus:outline-none focus:ring-2 focus:ring-green-500 disabled:opacity-50 disabled:cursor-not-allowed"
      >
        <span v-if="isConnecting">Connecting...</span>
        <span v-else>Connect</span>
      </button>
    </div>

    <!-- Known Networks -->
    <div v-if="knownNetworks.length > 0">
      <h3 class="font-semibold mb-3">Known Networks</h3>
      <div class="space-y-2">
        <div
          v-for="network in knownNetworks"
          :key="network.ssid"
          class="p-3 border border-gray-200 rounded-lg bg-white"
        >
          <div class="flex items-center justify-between">
            <div class="flex-1">
              <div class="font-medium">{{ network.ssid }}</div>
              <div class="text-sm text-gray-500">
                Last connected: {{ formatLastConnected(network.lastConnected) }}
              </div>
            </div>
            <div class="flex items-center space-x-2">
              <button
                @click="connectToKnownNetwork(network.ssid)"
                :disabled="isConnecting"
                class="px-3 py-1 text-sm bg-blue-600 text-white rounded hover:bg-blue-700 disabled:opacity-50"
              >
                Connect
              </button>
              <button
                @click="removeKnownNetwork(network.ssid)"
                class="px-3 py-1 text-sm bg-red-600 text-white rounded hover:bg-red-700"
              >
                Remove
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>
  </Card>
</template>

<style scoped>
/* Additional styling if needed */
</style>