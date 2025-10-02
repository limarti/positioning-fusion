<template>
  <div class="main-container space-y-6">
    <!-- Header Section -->
    <Card title="Network Configuration"
          subtitle="Manage WiFi connectivity and access point settings">
      <!-- Fallback Notification -->
      <div v-if="wifiState.fallbackNotification" class="p-4 bg-gray-50 border-l-4 border-gray-400 rounded-r-md mb-4">
        <div class="flex items-center">
          <svg class="w-5 h-5 mr-3 text-gray-500" fill="currentColor" viewBox="0 0 20 20">
            <path fill-rule="evenodd" d="M8.257 3.099c.765-1.36 2.722-1.36 3.486 0l5.58 9.92c.75 1.334-.213 2.98-1.742 2.98H4.42c-1.53 0-2.493-1.646-1.743-2.98l5.58-9.92zM11 13a1 1 0 11-2 0 1 1 0 012 0zm-1-8a1 1 0 00-1 1v3a1 1 0 002 0V6a1 1 0 00-1-1z" clip-rule="evenodd" />
          </svg>
          <span class="font-medium text-gray-700">{{ wifiState.fallbackNotification.message }}</span>
        </div>
      </div>

      <!-- Current Status Section -->
      <div class="flex items-center justify-between mb-4">
        <h3 class="text-md font-medium text-gray-900">
          Connection Status
        </h3>
          <div class="flex items-center space-x-2">
            <div class="w-2 h-2 rounded-full" :class="wifiState.wifiStatus.isConnected ? 'bg-gray-900' : 'bg-gray-400'" />
            <span class="text-sm font-medium" :class="wifiState.wifiStatus.isConnected ? 'text-gray-900' : 'text-gray-500'">
              {{ wifiState.wifiStatus.isConnected ? 'Connected' : 'Disconnected' }}
            </span>
          </div>
        </div>

        <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-6">
          <div class="bg-gray-50 rounded-lg p-4">
            <div class="text-xs font-medium text-gray-500 uppercase tracking-wide mb-1">
              Mode
            </div>
            <div class="text-sm font-semibold text-gray-900">
              {{ wifiState.wifiStatus.currentMode || 'Disconnected' }}
            </div>
          </div>

          <div v-if="wifiState.wifiStatus.connectedNetworkSSID" class="bg-gray-50 rounded-lg p-4">
            <div class="text-xs font-medium text-gray-500 uppercase tracking-wide mb-1">
              Network
            </div>
            <div class="text-sm font-semibold text-gray-900 truncate">
              {{ wifiState.wifiStatus.connectedNetworkSSID }}
            </div>
          </div>

          <div v-if="wifiState.wifiStatus.signalStrength" class="bg-gray-50 rounded-lg p-4">
            <div class="text-xs font-medium text-gray-500 uppercase tracking-wide mb-1">
              Signal Strength
            </div>
            <div class="flex items-center space-x-2">
              <div class="text-sm font-semibold text-gray-900">
                {{ wifiState.wifiStatus.signalStrength }}%
              </div>
              <div class="flex space-x-1">
                <div class="w-1 h-3 rounded-full" :class="wifiState.wifiStatus.signalStrength >= 25 ? 'bg-gray-900' : 'bg-gray-300'" />
                <div class="w-1 h-3 rounded-full" :class="wifiState.wifiStatus.signalStrength >= 50 ? 'bg-gray-900' : 'bg-gray-300'" />
                <div class="w-1 h-3 rounded-full" :class="wifiState.wifiStatus.signalStrength >= 75 ? 'bg-gray-900' : 'bg-gray-300'" />
                <div class="w-1 h-3 rounded-full" :class="wifiState.wifiStatus.signalStrength >= 90 ? 'bg-gray-900' : 'bg-gray-300'" />
              </div>
            </div>
          </div>

          <div class="bg-gray-50 rounded-lg p-4">
            <div class="text-xs font-medium text-gray-500 uppercase tracking-wide mb-1">
              Status
            </div>
            <div class="text-sm font-semibold" :class="wifiState.wifiStatus.isConnected ? 'text-gray-900' : 'text-gray-500'">
              {{ wifiState.wifiStatus.isConnected ? 'Online' : 'Offline' }}
            </div>
          </div>
        </div>
    </Card>

    <!-- Mode Configuration -->
    <Card title="Operating Mode"
          subtitle="Select the preferred WiFi operating mode for this device">
        <div v-if="wifiState.preferredMode === null" class="text-gray-500 text-sm flex items-center">
          <svg class="animate-spin -ml-1 mr-3 h-4 w-4 text-gray-400" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
            <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4" />
            <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
          </svg>
          Loading configuration...
        </div>

        <div v-else class="space-y-4">
          <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
            <label class="relative cursor-pointer">
              <input v-model="wifiState.preferredMode"
                     type="radio"
                     value="Client"
                     class="sr-only"
                     @change="onSetPreferredMode">
              <div class="p-4 rounded-lg border-2 transition-all duration-200"
                   :class="wifiState.preferredMode === 'Client' ? 'border-gray-900 bg-gray-50' : 'border-gray-200 hover:border-gray-300'">
                <div class="flex items-center justify-between">
                  <div>
                    <div class="font-medium text-gray-900">Client Mode</div>
                    <div class="text-xs text-gray-500 mt-1">
                      Will try to connect to known networks first, falling back to AP mode if connection fails.
                    </div>
                  </div>
                  <div class="w-4 h-4 rounded-full border-2 flex items-center justify-center"
                       :class="wifiState.preferredMode === 'Client' ? 'border-gray-900' : 'border-gray-300'">
                    <div v-if="wifiState.preferredMode === 'Client'" class="w-2 h-2 rounded-full bg-gray-900" />
                  </div>
                </div>
              </div>
            </label>

            <label class="relative cursor-pointer">
              <input v-model="wifiState.preferredMode"
                     type="radio"
                     value="AP"
                     class="sr-only"
                     @change="onSetPreferredMode">
              <div class="p-4 rounded-lg border-2 transition-all duration-200"
                   :class="wifiState.preferredMode === 'AP' ? 'border-gray-900 bg-gray-50' : 'border-gray-200 hover:border-gray-300'">
                <div class="flex items-center justify-between">
                  <div>
                    <div class="font-medium text-gray-900">Access Point Mode</div>
                    <div class="text-xs text-gray-500 mt-1">
                      Will start directly in Access Point mode, creating a WiFi hotspot for other devices to connect to.
                    </div>
                  </div>
                  <div class="w-4 h-4 rounded-full border-2 flex items-center justify-center"
                       :class="wifiState.preferredMode === 'AP' ? 'border-gray-900' : 'border-gray-300'">
                    <div v-if="wifiState.preferredMode === 'AP'" class="w-2 h-2 rounded-full bg-gray-900" />
                  </div>
                </div>
              </div>
            </label>
          </div>

          <!-- AP Configuration Form - always show -->
          <div class="space-y-4 pt-4 border-t border-gray-200">
            <div class="text-sm font-medium text-gray-900 mb-3">
              Access Point Settings
            </div>

            <div>
              <label class="form-label">Network Name (SSID)</label>
              <div class="form-input-readonly">
                {{ wifiState.apConfig.ssid || 'Loading...' }}
              </div>
              <div class="form-helper-text">
                Auto-generated from device name
              </div>
            </div>

            <div>
              <label class="form-label">Password</label>
              <div class="relative">
                <input v-model="wifiState.apConfig.password"
                       :type="wifiState.showAPPassword ? 'text' : 'password'"
                       class="form-input pr-20"
                       placeholder="Enter access point password"
                       @input="wifiState.isAPPasswordModified = wifiState.apConfig.password !== originalAPPassword">
                <div class="absolute inset-y-0 right-0 flex items-center">
                  <button type="button"
                          class="px-2 btn-icon"
                          @click="wifiState.showAPPassword = !wifiState.showAPPassword">
                    <svg v-if="wifiState.showAPPassword" class="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
                      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z" />
                    </svg>
                    <svg v-else class="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M13.875 18.825A10.05 10.05 0 0112 19c-4.478 0-8.268-2.943-9.543-7a9.97 9.97 0 711.563-3.029m5.858.908a3 3 0 114.243 4.243M9.878 9.878l4.242 4.242M9.878 9.878L8.115 8.114m0 0L6.937 6.937m0 0a8.977 8.977 0 00-1.942-.845m1.942.845A8.977 8.977 0 0012 5c2.14 0 4.135.601 5.828 1.635l-1.937 1.937m0 0a3 3 0 00-4.243 4.243l1.937-1.937z" />
                    </svg>
                  </button>
                  <!-- Accept/Cancel buttons - only show when modified -->
                  <div v-if="wifiState.isAPPasswordModified" class="flex space-x-1 pr-2">
                    <button class="p-1 bg-green-600 text-white rounded hover:bg-green-700 focus:outline-none focus:ring-2 focus:ring-green-500 focus:ring-offset-2"
                            title="Save changes"
                            @click="onSaveAPPassword">
                      <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5 13l4 4L19 7" />
                      </svg>
                    </button>
                    <button class="p-1 bg-gray-500 text-white rounded hover:bg-gray-600 focus:outline-none focus:ring-2 focus:ring-gray-500 focus:ring-offset-2"
                            title="Cancel changes"
                            @click="cancelAPPasswordEdit">
                      <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" />
                      </svg>
                    </button>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
    </Card>

    <!-- Known Networks -->
    <Card title="Saved Networks"
          subtitle="Manage your stored network credentials">
      <div class="flex justify-end mb-4">
        <button class="btn-primary"
                @click="wifiState.showAddNetworkDialog = true">
          Add Network
        </button>
      </div>

      <div v-if="wifiState.knownNetworks.length > 0" class="divide-y divide-gray-100 -mx-6">
        <div v-for="network in wifiState.knownNetworks"
             :key="network.ssid"
             class="px-6 py-4 first:pt-0">
          <div class="flex items-center justify-between">
            <div class="flex-1 min-w-0">
              <div class="font-medium text-gray-900 truncate">
                {{ network.ssid }}
              </div>
              <div class="text-sm text-gray-500">
                Last connected: {{ formatLastConnected(network.lastConnected) }}
              </div>
            </div>
            <div class="ml-4">
              <button class="btn-danger"
                      @click="onRemoveKnownNetwork(network.ssid)">
                Remove
              </button>
            </div>
          </div>
        </div>
      </div>

      <div v-else class="py-8 text-center">
        <svg class="mx-auto h-12 w-12 text-gray-400" fill="none" viewBox="0 0 24 24" stroke="currentColor">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M8.111 16.404a5.5 5.5 0 017.778 0M12 20h.01m-7.08-7.071c3.904-3.905 10.236-3.905 14.141 0M1.394 9.393c5.857-5.857 15.355-5.857 21.213 0" />
        </svg>
        <h3 class="mt-4 text-sm font-medium text-gray-900">
          No saved networks
        </h3>
        <p class="mt-2 text-sm text-gray-500">
          Get started by adding your first network.
        </p>
      </div>
    </Card>

    <!-- Add Network Dialog -->
    <div v-if="wifiState.showAddNetworkDialog" class="fixed inset-0 z-50 flex items-center justify-center p-4">
      <!-- Background overlay -->
      <div class="fixed inset-0 bg-black bg-opacity-50 transition-opacity" @click="closeAddNetworkDialog" />

      <!-- Modal panel -->
      <div class="relative bg-white border border-gray-200 rounded-lg shadow-xl max-w-md w-full max-h-screen overflow-y-auto">
        <!-- Header -->
        <div class="px-6 py-4 border-b border-gray-100">
          <div class="flex items-center justify-between">
            <h2 class="text-lg font-medium text-gray-900">
              Add Network
            </h2>
            <button type="button"
                    class="btn-icon"
                    :disabled="wifiState.isConnecting"
                    @click="closeAddNetworkDialog">
              <svg class="w-6 h-6" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" />
              </svg>
            </button>
          </div>
          <p class="text-sm text-gray-600 mt-1">
            Enter network credentials to connect
          </p>
        </div>

        <!-- Content -->
        <div class="px-6 py-5 space-y-4">
          <div>
            <label class="block text-xs font-medium text-gray-500 uppercase tracking-wide mb-2">Network Name (SSID)</label>
            <input v-model="wifiState.dialogNetworkConfig.ssid"
                   type="text"
                   class="w-full px-4 py-3 border border-gray-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-gray-900 focus:border-transparent transition-all duration-200"
                   placeholder="Enter network name"
                   :disabled="wifiState.isConnecting">
          </div>

          <div>
            <label class="block text-xs font-medium text-gray-500 uppercase tracking-wide mb-2">Password</label>
            <div class="relative">
              <input v-model="wifiState.dialogNetworkConfig.password"
                     :type="wifiState.showDialogPassword ? 'text' : 'password'"
                     class="w-full px-4 py-3 pr-12 border border-gray-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-gray-900 focus:border-transparent transition-all duration-200"
                     placeholder="Enter network password"
                     :disabled="wifiState.isConnecting">
              <button type="button"
                      class="absolute inset-y-0 right-0 pr-4 flex items-center btn-icon"
                      @click="wifiState.showDialogPassword = !wifiState.showDialogPassword">
                <svg v-if="wifiState.showDialogPassword" class="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
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
          <button type="button"
                  :disabled="wifiState.isConnecting"
                  class="btn-secondary"
                  @click="closeAddNetworkDialog">
            Cancel
          </button>
          <button :disabled="wifiState.isConnecting || !wifiState.dialogNetworkConfig.ssid || !wifiState.dialogNetworkConfig.password"
                  class="btn-primary"
                  @click="onAddNetwork">
            <div v-if="wifiState.isConnecting" class="flex items-center">
              <svg class="animate-spin -ml-1 mr-2 h-4 w-4 text-white" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4" />
                <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
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

<script setup>
  import { onMounted, onUnmounted, watch } from 'vue';
  import Card from './common/Card.vue';
  import { useConnectionData } from '@/composables/useConnectionData';
  import { useSignalR } from '@/composables/useSignalR';

  // Get data from composables
  const {
    state: wifiState,
    initializeWiFiData,
    addNetwork,
    closeAddNetworkDialog,
    removeKnownNetwork,
    saveAPPassword,
    cancelAPPasswordEdit,
    setPreferredMode,
    toggleNetworkPassword,
    formatLastConnected,
    cleanup
  } = useConnectionData();
  const { signalrConnection } = useSignalR();

  // Component event handlers that call composable functions
  const onAddNetwork = async () => 
  {
    await addNetwork(signalrConnection.value);
  };

  const onRemoveKnownNetwork = async (ssid) => 
  {
    await removeKnownNetwork(signalrConnection.value, ssid);
  };

  const onSaveAPPassword = async () => 
  {
    await saveAPPassword(signalrConnection.value);
  };

  const onSetPreferredMode = async () => 
  {
    await setPreferredMode(signalrConnection.value);
  };

  onMounted(async () => 
  {
    console.log('WiFiPanel mounted, SignalR connection available:', !!signalrConnection.value);
    await initializeWiFiData(signalrConnection.value);
  });

  // Watch for SignalR connection to become available - only initialize once
  watch(signalrConnection, async (newConnection, oldConnection) => 
  {
    console.log('WiFiPanel - SignalR connection changed:', !!newConnection, 'state:', newConnection?.state);

    if (newConnection && wifiState.preferredMode === null) 
    {
      // Set up a one-time connected handler instead of polling state
      if (newConnection.state === 'Connected') 
      {
        console.log('Connection already connected, initializing WiFi data...');
        await initializeWiFiData(newConnection);
      }
      else 
      {
        console.log('Waiting for connection to be established...');
        // Use a simple retry approach instead of onconnected to avoid conflicts with App.vue
        const checkAndInitialize = async () => 
        {
          if (newConnection.state === 'Connected' && wifiState.preferredMode === null) 
          {
            console.log('Connection now ready, initializing WiFi data...');
            await initializeWiFiData(newConnection);
          }
          else if (wifiState.preferredMode === null) 
          {
            // Retry after a short delay
            setTimeout(checkAndInitialize, 100);
          }
        };

        // Start checking after a brief delay to let the connection establish
        setTimeout(checkAndInitialize, 100);
      }
    }
  }, { immediate: true });

  onUnmounted(() => 
  {
    cleanup(signalrConnection.value);
  });
</script>

