<template>
  <aside :class="[
    'bg-white border-r border-gray-200 transition-all duration-300 flex flex-col',
    'lg:relative lg:translate-x-0',
    'fixed lg:static inset-y-0 left-0 z-50',
    'w-64',
    // Mobile visibility
    'lg:flex',
    isMobileOpen ? 'flex' : 'hidden lg:flex'
  ]">
    <!-- Header -->
    <div class="px-6 py-6 border-b border-gray-100">
      <div class="flex flex-col items-start">
        <img src="/src/assets/logo.svg" alt="Sierra Logo" class="w-32 mb-2">
        <h1 class="text-sm font-medium text-gray-600">
          Positioning System
        </h1>
      </div>
    </div>

    <!-- Navigation -->
    <nav class="flex-1 px-3 pt-4 space-y-1">
      <!-- GNSS -->
      <router-link to="/gnss"
                   :class="[
                     'w-full flex items-center px-3 py-2.5 rounded-lg transition-all duration-200 text-sm font-medium',
                     !hardwareStatus.gnssAvailable ? 'opacity-50 cursor-not-allowed pointer-events-none' : 'cursor-pointer',
                     route.name === 'gnss'
                       ? 'bg-blue-50 text-blue-600'
                       : 'text-gray-600 hover:bg-gray-50 hover:text-gray-900'
                   ]"
                   @click="closeMobileMenu"
                   :style="{ pointerEvents: hardwareStatus.gnssAvailable ? 'auto' : 'none' }">
        <svg class="w-5 h-5 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 20l-5.447-2.724A1 1 0 013 16.382V5.618a1 1 0 011.447-.894L9 7m0 13l6-3m-6 3V7m6 10l4.553 2.276A1 1 0 0021 18.382V7.618a1 1 0 00-.553-.894L15 4m0 13V4m0 0L9 7" />
        </svg>
        <span class="ml-3 text-left">GNSS</span>
      </router-link>

      <!-- Camera -->
      <router-link to="/camera"
                   :class="[
                     'w-full flex items-center px-3 py-2.5 rounded-lg transition-all duration-200 text-sm font-medium',
                     !hardwareStatus.cameraAvailable ? 'opacity-50 cursor-not-allowed pointer-events-none' : 'cursor-pointer',
                     route.name === 'camera'
                       ? 'bg-blue-50 text-blue-600'
                       : 'text-gray-600 hover:bg-gray-50 hover:text-gray-900'
                   ]"
                   @click="closeMobileMenu"
                   :style="{ pointerEvents: hardwareStatus.cameraAvailable ? 'auto' : 'none' }">
        <svg class="w-5 h-5 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="m15.75 10.5 4.72-4.72a.75.75 0 0 1 1.28.53v11.38a.75.75 0 0 1-1.28.53l-4.72-4.72M4.5 18.75h9a2.25 2.25 0 0 0 2.25-2.25v-9a2.25 2.25 0 0 0-2.25-2.25h-9A2.25 2.25 0 0 0 2.25 7.5v9a2.25 2.25 0 0 0 2.25 2.25Z" />
        </svg>
        <span class="ml-3 text-left">Camera</span>
      </router-link>

      <!-- IMU -->
      <router-link to="/imu"
                   :class="[
                     'w-full flex items-center px-3 py-2.5 rounded-lg transition-all duration-200 text-sm font-medium',
                     !hardwareStatus.imuAvailable ? 'opacity-50 cursor-not-allowed pointer-events-none' : 'cursor-pointer',
                     route.name === 'imu'
                       ? 'bg-blue-50 text-blue-600'
                       : 'text-gray-600 hover:bg-gray-50 hover:text-gray-900'
                   ]"
                   @click="closeMobileMenu"
                   :style="{ pointerEvents: hardwareStatus.imuAvailable ? 'auto' : 'none' }">
        <svg class="w-5 h-5 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="m21 7.5-9-5.25L3 7.5m18 0-9 5.25m9-5.25v9l-9 5.25M3 7.5l9 5.25M3 7.5v9l9 5.25m0-9v9" />
        </svg>
        <span class="ml-3 text-left">IMU</span>
      </router-link>

      <!-- Encoder -->
      <router-link to="/encoder"
                   :class="[
                     'w-full flex items-center px-3 py-2.5 rounded-lg transition-all duration-200 text-sm font-medium',
                     !hardwareStatus.encoderAvailable ? 'opacity-50 cursor-not-allowed pointer-events-none' : 'cursor-pointer',
                     route.name === 'encoder'
                       ? 'bg-blue-50 text-blue-600'
                       : 'text-gray-600 hover:bg-gray-50 hover:text-gray-900'
                   ]"
                   @click="closeMobileMenu"
                   :style="{ pointerEvents: hardwareStatus.encoderAvailable ? 'auto' : 'none' }">
        <svg class="w-5 h-5 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24" style="transform: rotate(30deg)">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5.636 5.636a9 9 0 1 0 12.728 0M12 3v9" />
        </svg>
        <span class="ml-3 text-left">Encoder</span>
      </router-link>

      <!-- WiFi -->
      <router-link to="/wifi"
                   :class="[
                     'w-full flex items-center px-3 py-2.5 rounded-lg transition-all duration-200 text-sm font-medium cursor-pointer',
                     route.name === 'wifi'
                       ? 'bg-blue-50 text-blue-600'
                       : 'text-gray-600 hover:bg-gray-50 hover:text-gray-900'
                   ]"
                   @click="closeMobileMenu">
        <svg class="w-5 h-5 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M8.288 15.038a5.25 5.25 0 0 1 7.424 0M5.106 11.856c3.807-3.808 9.98-3.808 13.788 0M1.924 8.674c5.565-5.565 14.587-5.565 20.152 0M12.53 18.22l-.53.53-.53-.53a.75.75 0 0 1 1.06 0Z" />
        </svg>
        <span class="ml-3 text-left">WiFi</span>
      </router-link>

      <!-- Logging -->
      <router-link to="/logging"
                   :class="[
                     'w-full flex items-center px-3 py-2.5 rounded-lg transition-all duration-200 text-sm font-medium cursor-pointer',
                     route.name === 'logging'
                       ? 'bg-blue-50 text-blue-600'
                       : 'text-gray-600 hover:bg-gray-50 hover:text-gray-900'
                   ]"
                   @click="closeMobileMenu">
        <svg class="w-5 h-5 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15.75 17.25v3.375c0 .621-.504 1.125-1.125 1.125h-9.75a1.125 1.125 0 0 1-1.125-1.125V7.875c0-.621.504-1.125 1.125-1.125H6.75a9.06 9.06 0 0 1 1.5.124m7.5 10.376h3.375c.621 0 1.125-.504 1.125-1.125V11.25c0-4.46-3.243-8.161-7.5-8.876a9.06 9.06 0 0 0-1.5-.124H9.375c-.621 0-1.125.504-1.125 1.125v3.5m7.5 10.375H9.375a1.125 1.125 0 0 1-1.125-1.125v-9.25m12 6.625v-1.875a3.375 3.375 0 0 0-3.375-3.375h-1.5a1.125 1.125 0 0 1-1.125-1.125v-1.5a3.375 3.375 0 0 0-3.375-3.375H9.75" />
        </svg>
        <span class="ml-3 text-left">Logging</span>
      </router-link>

      <!-- System -->
      <router-link to="/system"
                   :class="[
                     'w-full flex items-center px-3 py-2.5 rounded-lg transition-all duration-200 text-sm font-medium cursor-pointer',
                     route.name === 'system'
                       ? 'bg-blue-50 text-blue-600'
                       : 'text-gray-600 hover:bg-gray-50 hover:text-gray-900'
                   ]"
                   @click="closeMobileMenu">
        <svg class="w-5 h-5 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.065 2.572c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.572 1.065c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.065-2.572c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.572-1.065z" />
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
        </svg>
        <span class="ml-3 text-left">System</span>
      </router-link>
    </nav>

    <!-- Battery Indicator -->
    <div class="px-6 py-4 border-t border-gray-100">
      <div class="flex items-center justify-between">
        <div class="text-xs font-medium text-gray-600 uppercase tracking-wide">Battery</div>
        <div class="flex items-center space-x-2">
          <!-- Charging/Plugged icon -->
          <svg v-if="systemState.systemHealth.isExternalPowerConnected"
               xmlns="http://www.w3.org/2000/svg"
               viewBox="0 0 24 24"
               fill="currentColor"
               class="text-green-600 w-4 h-4">
            <path fill-rule="evenodd"
                  d="M14.615 1.595a.75.75 0 0 1 .359.852L12.982 9.75h7.268a.75.75 0 0 1 .548 1.262l-10.5 11.25a.75.75 0 0 1-1.272-.71l1.992-7.302H3.75a.75.75 0 0 1-.548-1.262l10.5-11.25a.75.75 0 0 1 .913-.143Z"
                  clip-rule="evenodd" />
          </svg>
          <!-- Battery percentage -->
          <div class="flex items-center space-x-1">
            <div class="relative">
              <svg xmlns="http://www.w3.org/2000/svg"
                   fill="none"
                   viewBox="0 0 24 24"
                   stroke-width="1.5"
                   stroke="currentColor"
                   class="w-6 h-6"
                   :class="systemState.systemHealth.batteryLevel !== null && systemState.systemHealth.batteryLevel < 20 ? 'text-red-600' : 'text-gray-600'">
                <path stroke-linecap="round"
                      stroke-linejoin="round"
                      d="M21 10.5h.375c.621 0 1.125.504 1.125 1.125v2.25c0 .621-.504 1.125-1.125 1.125H21M3.75 18h15A2.25 2.25 0 0 0 21 15.75v-6a2.25 2.25 0 0 0-2.25-2.25h-15A2.25 2.25 0 0 0 1.5 9.75v6A2.25 2.25 0 0 0 3.75 18Z" />
              </svg>
            </div>
            <span class="text-sm font-semibold text-gray-900">
              {{ systemState.systemHealth.batteryLevel !== null ? Math.round(systemState.systemHealth.batteryLevel) : '--' }}%
            </span>
          </div>
        </div>
      </div>
    </div>
  </aside>
</template>

<script setup>
  import { defineProps, defineEmits } from 'vue';
  import { useRoute } from 'vue-router';
  import { useHardwareStatus } from '../../composables/useHardwareStatus';
  import { useSystemData } from '../../composables/useSystemData';

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
  const { state: systemState } = useSystemData();

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