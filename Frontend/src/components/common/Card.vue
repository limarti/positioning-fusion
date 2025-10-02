<template>
  <div class="bg-white rounded-xl shadow-sm border border-gray-100">
    <div class="px-6 py-5 border-b border-gray-100">
      <h3 class="text-lg font-semibold text-gray-900">
        {{ title }}
      </h3>
      <p v-if="subtitle" class="text-sm text-gray-600 mt-1">
        {{ subtitle }}
      </p>
    </div>

    <div class="p-6">
      <slot />
    </div>
  </div>
</template>

<script setup>
const props = defineProps({
  title: {
    type: String,
    required: true
  },
  subtitle: {
    type: String,
    default: null
  }
});
</script>