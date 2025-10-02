<template>
  <div class="flex h-screen bg-gray-100">
    <!-- Sidebar -->
    <Sidebar :activeSection="activeSection"
             :isMobileOpen="isMobileMenuOpen"
             @toggleCollapse="toggleSidebar" />

    <!-- Main Content Area -->
    <main class="flex-1 flex flex-col overflow-hidden">
      <!-- Content Area -->
      <div class="flex-1 overflow-auto bg-gray-100">
        <!-- Mobile Menu Button (outside container) -->
        <button class="lg:hidden p-2 m-4 rounded-md text-gray-600 hover:text-gray-900 hover:bg-white"
                @click="toggleSidebar">
          <svg class="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 6h16M4 12h16M4 18h16" />
          </svg>
        </button>

        <slot :activeSection="activeSection" :headerActions="$slots['header-actions']" />
      </div>
    </main>

    <!-- Mobile Overlay -->
    <div v-if="isMobileMenuOpen"
         class="lg:hidden fixed inset-0 bg-black bg-opacity-50 z-40"
         @click="toggleSidebar" />
  </div>
</template>

<script setup>
  import { ref } from 'vue';
  import Sidebar from './Sidebar.vue';

  const props = defineProps({
    activeSection: {
      type: String,
      required: true
    }
  });

  const isMobileMenuOpen = ref(false);

  const toggleSidebar = () => 
  {
    // Toggle mobile menu visibility
    isMobileMenuOpen.value = !isMobileMenuOpen.value;
  };
</script>

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