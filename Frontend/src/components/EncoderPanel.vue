<template>
  <Card title="Encoder"
        subtitle="Active"
        iconColor="bg-gray-500">
    <div class="space-y-2 text-sm">
      <div class="flex justify-between">
        <span class="text-slate-500">Count:</span>
        <span :class="systemState.encoderData.rawCount !== null ? 'font-mono' : 'text-slate-400'">{{ systemState.encoderData.rawCount !== null ? systemState.encoderData.rawCount : '—' }}</span>
      </div>
      <div class="flex justify-between">
        <span class="text-slate-500">Direction:</span>
        <span :class="systemState.encoderData.direction !== null ? '' : 'text-slate-400'">{{ systemState.encoderData.direction !== null ? systemState.encoderData.direction : '—' }}</span>
      </div>
      <div class="flex justify-between">
        <span class="text-slate-500">Rate:</span>
        <span :class="systemState.encoderData.pulsesPerSecond !== null ? 'font-mono' : 'text-slate-400'">{{ systemState.encoderData.pulsesPerSecond !== null ? systemState.encoderData.pulsesPerSecond + '/s' : '—' }}</span>
      </div>
    </div>
  </Card>
</template>

<script setup>
  import Card from './common/Card.vue';
  import { useSystemData } from '@/composables/useSystemData';

  // Get data from composable
  const { state: systemState } = useSystemData();
</script>