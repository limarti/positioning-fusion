<script setup>
import { ref, watch, onUnmounted } from 'vue'
import { useRouter } from 'vue-router'
import Layout from './components/layout/Layout.vue'
import ConnectionOverlay from './components/ConnectionOverlay.vue'
import { useSignalR } from './composables/useSignalR'
import { useGnssData } from './composables/useGnssData'
import { useSystemData } from './composables/useSystemData'
import { useConnectionData } from './composables/useConnectionData'

// Initialize all composables
const { signalrConnection, connectionStatus, retryAttempt, nextRetryIn, currentMode, initializeConnection, cleanup } = useSignalR()
const { state: gnssState } = useGnssData()
const { state: systemState } = useSystemData()
const { state: wifiState } = useConnectionData()

// Legacy data for components that haven't been updated yet
const loraData = ref({
  mode: null,
  signalStrength: null,
  correctionRate: null,
  packetsReceived: null,
  packetsSent: null
})


// Router setup
const router = useRouter()
const activeSection = ref(router.currentRoute.value.name || 'gnss')

// Watch route changes to keep activeSection in sync
watch(() => router.currentRoute.value, (newRoute) => {
  if (newRoute.name) {
    activeSection.value = newRoute.name
  }
})



// Initialize SignalR connection
initializeConnection()

// Cleanup on unmount
onUnmounted(cleanup)
</script>

<template>
  <div class="h-screen">
    <Layout
      :active-section="activeSection"
    >
      <template #header-actions>
        <!-- Battery Indicator -->
        <div class="flex items-center space-x-2 text-sm">
          <div class="flex items-center">
            <!-- Charging/Plugged icon (left of battery) -->
            <svg
              v-if="systemState.systemHealth.isExternalPowerConnected"
              xmlns="http://www.w3.org/2000/svg"
              viewBox="0 0 24 24"
              fill="currentColor"
              class="text-slate-600 w-3 h-3 mr-0.5"
            >
              <path
                fill-rule="evenodd"
                d="M14.615 1.595a.75.75 0 0 1 .359.852L12.982 9.75h7.268a.75.75 0 0 1 .548 1.262l-10.5 11.25a.75.75 0 0 1-1.272-.71l1.992-7.302H3.75a.75.75 0 0 1-.548-1.262l10.5-11.25a.75.75 0 0 1 .913-.143Z"
                clip-rule="evenodd"
              />
            </svg>
            <!-- Battery Icon with overlaid percentage -->
            <div class="relative">
              <svg
                xmlns="http://www.w3.org/2000/svg"
                fill="none"
                viewBox="0 0 24 24"
                stroke-width="1.5"
                stroke="currentColor"
                class="text-slate-600 w-8 h-8"
              >
                <path
                  stroke-linecap="round"
                  stroke-linejoin="round"
                  d="M21 10.5h.375c.621 0 1.125.504 1.125 1.125v2.25c0 .621-.504 1.125-1.125 1.125H21M3.75 18h15A2.25 2.25 0 0 0 21 15.75v-6a2.25 2.25 0 0 0-2.25-2.25h-15A2.25 2.25 0 0 0 1.5 9.75v6A2.25 2.25 0 0 0 3.75 18Z"
                />
              </svg>
              <!-- Battery percentage overlaid -->
              <div class="absolute inset-0 flex items-center justify-center">
                <span class="text-slate-600 text-[0.77rem] font-bold leading-none">
                  {{ systemState.systemHealth.batteryLevel !== null ? Math.round(systemState.systemHealth.batteryLevel) : '--' }}
                </span>
              </div>
            </div>
          </div>
        </div>
      </template>

      <!-- Router View for Dynamic Content -->
      <div class="main-centered">
        <router-view />
      </div>
    </Layout>

    <!-- Connection Overlay -->
    <ConnectionOverlay
      :connection-status="connectionStatus"
      :retry-attempt="retryAttempt"
      :next-retry-in="nextRetryIn"
    />
  </div>
</template>

<style scoped></style>
