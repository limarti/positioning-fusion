<template>
  <div class="main-centered">
    <!-- Header -->
    <div class="mb-6">
      <h1 class="text-2xl font-semibold text-gray-900">
        {{ title }}
      </h1>
    </div>

    <!-- Content -->
    <slot />
  </div>
</template>

<script setup>
const props = defineProps({
  title: {
    type: String,
    required: true
  }
});
</script>
