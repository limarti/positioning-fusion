<template>
  <div v-if="show" class="fixed inset-0 z-50">
    <!-- Background overlay -->
    <div class="absolute inset-0 bg-black opacity-25 transition-opacity" @click="$emit('close')" />

    <!-- Modal panel -->
    <div class="relative z-10 flex items-center justify-center min-h-full p-4">
      <div class="relative bg-white border border-gray-200 rounded-lg shadow-xl w-full max-h-screen overflow-y-auto"
           :class="maxWidthClass">
      <!-- Header -->
      <div class="px-6 py-4 border-b border-gray-100">
        <div class="flex items-center justify-between">
          <h2 class="text-lg font-medium text-gray-900">
            {{ title }}
          </h2>
          <button type="button"
                  class="btn-icon"
                  @click="$emit('close')">
            <svg class="w-6 h-6" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>
        <p v-if="subtitle" class="text-sm text-gray-600 mt-1">
          {{ subtitle }}
        </p>
      </div>

      <!-- Content -->
      <div class="px-6 py-5">
        <slot />
      </div>

      <!-- Footer -->
      <div v-if="$slots.footer" class="px-6 py-4 border-t border-gray-100">
        <slot name="footer" />
      </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed } from 'vue';

const props = defineProps({
  show: {
    type: Boolean,
    required: true
  },
  title: {
    type: String,
    required: true
  },
  subtitle: {
    type: String,
    default: ''
  },
  maxWidth: {
    type: String,
    default: 'md',
    validator: (value) => ['sm', 'md', 'lg', 'xl', '2xl'].includes(value)
  }
});

defineEmits(['close']);

const maxWidthClass = computed(() => {
  const widths = {
    sm: 'max-w-sm',
    md: 'max-w-md',
    lg: 'max-w-lg',
    xl: 'max-w-xl',
    '2xl': 'max-w-2xl'
  };
  return widths[props.maxWidth];
});
</script>
