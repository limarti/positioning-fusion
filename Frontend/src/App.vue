<template>
  <div class="h-screen">
    <Layout :activeSection="activeSection">
      <!-- Router View for Dynamic Content -->
      <router-view />
    </Layout>

    <!-- Connection Overlay -->
    <ConnectionOverlay :connectionStatus="connectionStatus"
                       :retryAttempt="retryAttempt"
                       :nextRetryIn="nextRetryIn" />
  </div>
</template>

<script setup>
  import { ref, watch, onUnmounted } from 'vue';
  import { useRouter } from 'vue-router';
  import Layout from './components/layout/Layout.vue';
  import ConnectionOverlay from './components/ConnectionOverlay.vue';
  import { useSignalR } from './composables/useSignalR';
  import { useGnssData } from './composables/useGnssData';
  import { useSystemData } from './composables/useSystemData';
  import { useConnectionData } from './composables/useConnectionData';

  // Initialize all composables
  const { signalrConnection, connectionStatus, retryAttempt, nextRetryIn, currentMode, initializeConnection, cleanup } = useSignalR();
  const { state: gnssState } = useGnssData();
  const { state: systemState } = useSystemData();
  const { state: wifiState } = useConnectionData();

  // Legacy data for components that haven't been updated yet
  const loraData = ref({
    mode: null,
    signalStrength: null,
    correctionRate: null,
    packetsReceived: null,
    packetsSent: null
  });

  // Router setup
  const router = useRouter();
  const activeSection = ref(router.currentRoute.value.name || 'gnss');

  // Watch route changes to keep activeSection in sync
  watch(() => router.currentRoute.value, (newRoute) => 
  {
    if (newRoute.name) 
    {
      activeSection.value = newRoute.name;
    }
  });

  // Watch hostname changes to update document title
  watch(() => systemState.systemHealth.hostname, (newHostname) => 
  {
    if (newHostname) 
    {
      document.title = `Subterra | ${newHostname}`;
    }
    else 
    {
      document.title = 'Subterra';
    }
  }, { immediate: true });

  // Initialize SignalR connection
  initializeConnection();

  // Cleanup on unmount
  onUnmounted(cleanup);
</script>

<style scoped></style>
