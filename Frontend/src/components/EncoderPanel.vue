<script setup>
import Card from './common/Card.vue'

const props = defineProps({
  encoderData: {
    type: Object,
    required: true
  }
})
</script>

<template>
  <Card
    title="Encoder"
    subtitle="Active"
    icon-color="bg-gray-500"
  >
    <div class="space-y-2 text-sm">
      <div class="flex justify-between">
        <span class="text-slate-500">Count:</span>
        <span :class="encoderData.rawCount !== null ? 'font-mono' : 'text-slate-400'">{{ encoderData.rawCount !== null ? encoderData.rawCount : '—' }}</span>
      </div>
      <div class="flex justify-between">
        <span class="text-slate-500">Direction:</span>
        <span :class="encoderData.direction !== null ? '' : 'text-slate-400'">{{ encoderData.direction !== null ? encoderData.direction : '—' }}</span>
      </div>
      <div class="flex justify-between">
        <span class="text-slate-500">Rate:</span>
        <span :class="encoderData.pulsesPerSecond !== null ? 'font-mono' : 'text-slate-400'">{{ encoderData.pulsesPerSecond !== null ? encoderData.pulsesPerSecond + '/s' : '—' }}</span>
      </div>
    </div>
  </Card>
</template>