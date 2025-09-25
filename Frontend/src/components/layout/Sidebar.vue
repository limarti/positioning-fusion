<script setup>
import { defineProps, defineEmits } from 'vue'

const props = defineProps({
  activeSection: {
    type: String,
    required: true
  },
  isMobileOpen: {
    type: Boolean,
    default: false
  }
})

const emit = defineEmits(['sectionChanged', 'toggleCollapse'])

const sections = [
  {
    id: 'gnss',
    name: 'GNSS',
    icon: 'M12 2L2 22h20L12 2zM12 6.5L18.5 20h-13L12 6.5z'
  },
  {
    id: 'camera',
    name: 'Camera',
    icon: 'M12 9a3 3 0 11-6 0 3 3 0 016 0zM1 12a11 11 0 1122 0A11 11 0 011 12z'
  },
  {
    id: 'imu',
    name: 'IMU',
    icon: 'M7 7h10v10H7zM5 5v14h14V5H5z'
  },
  {
    id: 'encoder',
    name: 'Encoder',
    icon: 'M12 6v6l4 2M12 2a10 10 0 11-10 10A10 10 0 0112 2z'
  },
  {
    id: 'wifi',
    name: 'WiFi',
    icon: 'M3 9.11A9 9 0 1121 9.11M7 12.94A5 5 0 1117 12.94M12 17a1 1 0 11-2 0 1 1 0 012 0z'
  },
  {
    id: 'logging',
    name: 'Logging',
    icon: 'M9 17H7v-2h2v2zm4 0h-2v-2h2v2zm4 0h-2v-2h2v2zm2-7H4V7h15v3zM19 3H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2z'
  },
  {
    id: 'system',
    name: 'System',
    icon: 'M19 3H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm-8 8H9v2h2v-2zm0-2H9V7h2v2zm2 0h2V7h-2v2zm0 4V9h2v2h-2zm2 2h-2v2h2v-2z'
  }
]

const selectSection = (sectionId) => {
  emit('sectionChanged', sectionId)
  // Close mobile menu when section is selected
  if (props.isMobileOpen) {
    emit('toggleCollapse')
  }
}
</script>

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
    <div class="p-4 border-b border-slate-700">
      <div class="flex items-center space-x-3">
        <img src="/src/assets/logo.svg" alt="Sierra Logo" class="h-8 w-8">
        <h1 class="text-lg font-semibold">GNSS System</h1>
      </div>
    </div>


    <!-- Navigation -->
    <nav class="flex-1 p-4 space-y-2">
      <button
        v-for="section in sections"
        :key="section.id"
        @click="selectSection(section.id)"
        :class="[
          'w-full flex items-center p-3 rounded-lg transition-all duration-200',
          activeSection === section.id
            ? 'bg-blue-600 text-white shadow-lg'
            : 'text-slate-300 hover:bg-slate-700 hover:text-white'
        ]"
      >
        <svg
          class="w-6 h-6 flex-shrink-0"
          fill="none"
          stroke="currentColor"
          viewBox="0 0 24 24"
        >
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" :d="section.icon" />
        </svg>
        <span class="ml-3 text-left font-medium">
          {{ section.name }}
        </span>
      </button>
    </nav>
  </aside>
</template>

<style scoped>
/* Additional animations can be added here if needed */
</style>