<template>
  <aside :class="[
    'bg-gradient-to-b from-slate-800 to-slate-900 text-white transition-all duration-300 flex flex-col',
    'lg:relative lg:translate-x-0',
    'fixed lg:static inset-y-0 left-0 z-50',
    'w-64',
    // Mobile visibility
    'lg:flex',
    isMobileOpen ? 'flex' : 'hidden lg:flex'
  ]">
    <!-- Header -->
    <div class="px-4 border-b border-slate-700 py-4">
      <div class="flex flex-col items-start">
        <img src="/src/assets/logo.svg" alt="Sierra Logo" class="w-32">
        <h1 class="text">
          Positioning System
        </h1>
      </div>
    </div>

    <!-- Navigation -->
    <nav class="flex-1 p-4 space-y-2">
      <!-- GNSS -->
      <router-link to="/gnss"
                   :class="[
                     'w-full flex items-center p-3 rounded-lg transition-all duration-200 cursor-pointer',
                     route.name === 'gnss'
                       ? 'bg-blue-600 text-white shadow-lg'
                       : 'text-slate-300 hover:bg-slate-700 hover:text-white'
                   ]"
                   @click="closeMobileMenu">
        <svg class="w-6 h-6 flex-shrink-0" fill="currentColor" viewBox="0 -960 960 960">
          <path d="M560-32v-80q117 0 198.5-81.5T840-392h80q0 75-28.5 140.5t-77 114q-48.5 48.5-114 77T560-32Zm0-160v-80q50 0 85-35t35-85h80q0 83-58.5 141.5T560-192ZM222-57q-15 0-30-6t-27-17L23-222q-11-12-17-27t-6-30q0-16 6-30.5T23-335l127-127q23-23 57-23.5t57 22.5l50 50 28-28-50-50q-23-23-23-56t23-56l57-57q23-23 56.5-23t56.5 23l50 50 28-28-50-50q-23-23-23-56.5t23-56.5l127-127q12-12 27-18t30-6q15 0 29.5 6t26.5 18l142 142q12 11 17.5 25.5T895-730q0 15-5.5 30T872-673L745-546q-23 23-56.5 23T632-546l-50-50-28 28 50 50q23 23 22.5 56.5T603-405l-56 56q-23 23-56.5 23T434-349l-50-50-28 28 50 50q23 23 22.5 57T405-207L278-80q-11 11-25.5 17T222-57Zm0-79 42-42-142-142-42 42 142 142Zm85-85 42-42-142-142-42 42 142 142Zm184-184 56-56-142-142-56 56 142 142Zm198-198 42-42-142-142-42 42 142 142Zm85-85 42-42-142-142-42 42 142 142ZM448-504Z" />
        </svg>
        <span class="ml-3 text-left font-medium">GNSS</span>
      </router-link>

      <!-- Camera -->
      <router-link to="/camera"
                   :class="[
                     'w-full flex items-center p-3 rounded-lg transition-all duration-200',
                     !hardwareStatus.cameraAvailable ? 'opacity-50 cursor-not-allowed pointer-events-none' : 'cursor-pointer',
                     route.name === 'camera'
                       ? 'bg-blue-600 text-white shadow-lg'
                       : 'text-slate-300 hover:bg-slate-700 hover:text-white'
                   ]"
                   @click="closeMobileMenu"
                   :style="{ pointerEvents: hardwareStatus.cameraAvailable ? 'auto' : 'none' }">
        <svg class="w-6 h-6 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.5" d="m15.75 10.5 4.72-4.72a.75.75 0 0 1 1.28.53v11.38a.75.75 0 0 1-1.28.53l-4.72-4.72M4.5 18.75h9a2.25 2.25 0 0 0 2.25-2.25v-9a2.25 2.25 0 0 0-2.25-2.25h-9A2.25 2.25 0 0 0 2.25 7.5v9a2.25 2.25 0 0 0 2.25 2.25Z" />
        </svg>
        <span class="ml-3 text-left font-medium">Camera</span>
      </router-link>

      <!-- IMU -->
      <router-link to="/imu"
                   :class="[
                     'w-full flex items-center p-3 rounded-lg transition-all duration-200',
                     !hardwareStatus.imuAvailable ? 'opacity-50 cursor-not-allowed pointer-events-none' : 'cursor-pointer',
                     route.name === 'imu'
                       ? 'bg-blue-600 text-white shadow-lg'
                       : 'text-slate-300 hover:bg-slate-700 hover:text-white'
                   ]"
                   @click="closeMobileMenu"
                   :style="{ pointerEvents: hardwareStatus.imuAvailable ? 'auto' : 'none' }">
        <svg class="w-6 h-6 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.5" d="m21 7.5-9-5.25L3 7.5m18 0-9 5.25m9-5.25v9l-9 5.25M3 7.5l9 5.25M3 7.5v9l9 5.25m0-9v9" />
        </svg>
        <span class="ml-3 text-left font-medium">IMU</span>
      </router-link>

      <!-- Encoder -->
      <router-link to="/encoder"
                   :class="[
                     'w-full flex items-center p-3 rounded-lg transition-all duration-200',
                     !hardwareStatus.encoderAvailable ? 'opacity-50 cursor-not-allowed pointer-events-none' : 'cursor-pointer',
                     route.name === 'encoder'
                       ? 'bg-blue-600 text-white shadow-lg'
                       : 'text-slate-300 hover:bg-slate-700 hover:text-white'
                   ]"
                   @click="closeMobileMenu"
                   :style="{ pointerEvents: hardwareStatus.encoderAvailable ? 'auto' : 'none' }">
        <svg class="w-6 h-6 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24" style="transform: rotate(30deg)">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.5" d="M5.636 5.636a9 9 0 1 0 12.728 0M12 3v9" />
        </svg>
        <span class="ml-3 text-left font-medium">Encoder</span>
      </router-link>

      <!-- WiFi -->
      <router-link to="/wifi"
                   :class="[
                     'w-full flex items-center p-3 rounded-lg transition-all duration-200 cursor-pointer',
                     route.name === 'wifi'
                       ? 'bg-blue-600 text-white shadow-lg'
                       : 'text-slate-300 hover:bg-slate-700 hover:text-white'
                   ]"
                   @click="closeMobileMenu">
        <svg class="w-6 h-6 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.5" d="M8.288 15.038a5.25 5.25 0 0 1 7.424 0M5.106 11.856c3.807-3.808 9.98-3.808 13.788 0M1.924 8.674c5.565-5.565 14.587-5.565 20.152 0M12.53 18.22l-.53.53-.53-.53a.75.75 0 0 1 1.06 0Z" />
        </svg>
        <span class="ml-3 text-left font-medium">WiFi</span>
      </router-link>

      <!-- Logging -->
      <router-link to="/logging"
                   :class="[
                     'w-full flex items-center p-3 rounded-lg transition-all duration-200 cursor-pointer',
                     route.name === 'logging'
                       ? 'bg-blue-600 text-white shadow-lg'
                       : 'text-slate-300 hover:bg-slate-700 hover:text-white'
                   ]"
                   @click="closeMobileMenu">
        <svg class="w-6 h-6 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.5" d="M15.75 17.25v3.375c0 .621-.504 1.125-1.125 1.125h-9.75a1.125 1.125 0 0 1-1.125-1.125V7.875c0-.621.504-1.125 1.125-1.125H6.75a9.06 9.06 0 0 1 1.5.124m7.5 10.376h3.375c.621 0 1.125-.504 1.125-1.125V11.25c0-4.46-3.243-8.161-7.5-8.876a9.06 9.06 0 0 0-1.5-.124H9.375c-.621 0-1.125.504-1.125 1.125v3.5m7.5 10.375H9.375a1.125 1.125 0 0 1-1.125-1.125v-9.25m12 6.625v-1.875a3.375 3.375 0 0 0-3.375-3.375h-1.5a1.125 1.125 0 0 1-1.125-1.125v-1.5a3.375 3.375 0 0 0-3.375-3.375H9.75" />
        </svg>
        <span class="ml-3 text-left font-medium">Logging</span>
      </router-link>

      <!-- System -->
      <router-link to="/system"
                   :class="[
                     'w-full flex items-center p-3 rounded-lg transition-all duration-200 cursor-pointer',
                     route.name === 'system'
                       ? 'bg-blue-600 text-white shadow-lg'
                       : 'text-slate-300 hover:bg-slate-700 hover:text-white'
                   ]"
                   @click="closeMobileMenu">
        <svg class="w-6 h-6 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.5" d="M11.42 15.17 17.25 21A2.652 2.652 0 0 0 21 17.25l-5.877-5.877M11.42 15.17l2.496-3.03c.317-.384.74-.626 1.208-.766M11.42 15.17l-4.655 5.653a2.548 2.548 0 1 1-3.586-3.586l6.837-5.63m5.108-.233c.55-.164 1.163-.188 1.743-.14a4.5 4.5 0 0 0 4.486-6.336l-3.276 3.277a3.004 3.004 0 0 1-2.25-2.25l3.276-3.276a4.5 4.5 0 0 0-6.336 4.486c.091 1.076-.071 2.264-.904 2.95l-.102.085m-1.745 1.437L5.909 7.5H4.5L2.25 3.75l1.5-1.5L7.5 4.5v1.409l4.26 4.26m-1.745 1.437 1.745-1.437m6.615 8.206L15.75 15.75M4.867 19.125h.008v.008h-.008v-.008Z" />
        </svg>
        <span class="ml-3 text-left font-medium">System</span>
      </router-link>
    </nav>
  </aside>
</template>

<script setup>
  import { defineProps, defineEmits } from 'vue';
  import { useRoute } from 'vue-router';
  import { useHardwareStatus } from '../../composables/useHardwareStatus';

  const props = defineProps({
    activeSection: {
      type: String,
      required: true
    },
    isMobileOpen: {
      type: Boolean,
      default: false
    }
  });

  const emit = defineEmits(['toggleCollapse']);
  const route = useRoute();
  const { hardwareStatus } = useHardwareStatus();

  const closeMobileMenu = () =>
  {
    // Close mobile menu when section is selected
    if (props.isMobileOpen)
    {
      emit('toggleCollapse');
    }
  };
</script>

<style scoped>
/* Additional animations can be added here if needed */
</style>