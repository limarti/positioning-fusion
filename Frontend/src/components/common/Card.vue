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
  <div class="bg-white rounded-xl border border-slate-200 p-4">
    <div class="flex items-center space-x-2 mb-3">
      <div v-if="icon" class="w-6 h-6 rounded-lg flex items-center justify-center" :class="iconColor">
        <div class="w-4 h-4 text-white" v-html="icon"></div>
      </div>
      <div>
        <h3 class="font-bold text-slate-800">{{ title }}</h3>
        <div v-if="subtitle" class="text-sm text-slate-400">{{ subtitle }}</div>
      </div>
    </div>
    
    <slot></slot>
  </div>
</template>