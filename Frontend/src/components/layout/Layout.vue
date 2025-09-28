<script setup>
import { ref } from 'vue'
import Sidebar from './Sidebar.vue'

const props = defineProps({
  activeSection: {
    type: String,
    required: true
  }
})

const isMobileMenuOpen = ref(false)

const toggleSidebar = () => {
  // Toggle mobile menu visibility
  isMobileMenuOpen.value = !isMobileMenuOpen.value
}
</script>

<template>
  <div class="flex h-screen bg-gradient-to-br from-slate-100 to-slate-200">
    <!-- Sidebar -->
    <Sidebar
      :active-section="activeSection"
      :is-mobile-open="isMobileMenuOpen"
      @toggle-collapse="toggleSidebar"
    />

    <!-- Main Content Area -->
    <main class="flex-1 flex flex-col overflow-hidden">
      <!-- Content Header -->
      <header class="bg-white border-b border-slate-200 px-6 py-4 shadow-sm">
        <div class="flex items-center justify-between">
          <div class="flex items-center space-x-4">
            <!-- Mobile Menu Button -->
            <button
              class="lg:hidden p-2 rounded-md text-slate-600 hover:text-slate-900 hover:bg-slate-100"
              @click="toggleSidebar"
            >
              <svg class="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 6h16M4 12h16M4 18h16" />
              </svg>
            </button>
            <h1 class="text-2xl font-semibold text-slate-800 capitalize">{{ activeSection }}</h1>
          </div>

          <!-- Status indicators or other header content can go here -->
          <div class="flex items-center space-x-4">
            <slot name="header-actions"></slot>
          </div>
        </div>
      </header>

      <!-- Content Area -->
      <div class="flex-1 overflow-auto p-6">
        <div class="max-w-7xl mx-auto">
          <slot></slot>
        </div>
      </div>
    </main>

    <!-- Mobile Overlay -->
    <div
      v-if="isMobileMenuOpen"
      class="lg:hidden fixed inset-0 bg-black bg-opacity-50 z-40"
      @click="toggleSidebar"
    ></div>
  </div>
</template>

<style>
/* Mobile sidebar positioning */
@media (max-width: 1024px) {
  .sidebar-mobile {
    position: fixed;
    top: 0;
    left: 0;
    bottom: 0;
    z-index: 50;
    transform: translateX(-100%);
    transition: transform 0.3s ease-in-out;
  }

  .sidebar-mobile.show {
    transform: translateX(0);
  }
}
</style>