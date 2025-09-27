<script setup>
const props = defineProps({
  title: {
    type: String,
    required: true
  },
  subtitle: {
    type: String,
    default: null
  },
  icon: {
    type: String,
    default: null
  },
  iconColor: {
    type: String,
    default: 'bg-emerald-500'
  }
})
</script>

<template>
  <div class="panel">
    <div class="panel-header">
      <div class="flex items-center space-x-2">
        <div v-if="icon" class="w-6 h-6 rounded-lg flex items-center justify-center" :class="iconColor">
          <div class="w-4 h-4 text-white" v-html="icon"></div>
        </div>
        <div>
          <h3 class="panel-title">{{ title }}</h3>
          <div v-if="subtitle && subtitle !== '—'" class="panel-subtitle">{{ subtitle }}</div>
        </div>
      </div>
    </div>

    <div class="panel-content">
      <slot></slot>
    </div>
  </div>
</template>