<script setup>
import Card from './common/Card.vue'

// No props needed for camera panel currently as it shows static data
</script>

<template>
  <!-- Camera -->
  <Card 
    title="Camera" 
    subtitle="—"
    :icon="`<svg fill='currentColor' viewBox='0 0 24 24'><path d='M12,15A2,2 0 0,1 10,13A2,2 0 0,1 12,11A2,2 0 0,1 14,13A2,2 0 0,1 12,15M22,6H19L17.83,4.5C17.42,3.87 16.75,3.5 16,3.5H8C7.25,3.5 6.58,3.87 6.17,4.5L5,6H2A2,2 0 0,0 0,8V18A2,2 0 0,0 2,20H22A2,2 0 0,0 24,18V8A2,2 0 0,0 22,6M12,17A4,4 0 0,0 16,13A4,4 0 0,0 12,9A4,4 0 0,0 8,13A4,4 0 0,0 12,17Z'/></svg>`"
    icon-color="bg-cyan-500"
  >
    <div class="space-y-2 text-sm">
      <div class="flex justify-between">
        <span class="text-slate-500">Duration:</span>
        <span class="text-slate-400">—</span>
      </div>
      <div class="flex justify-between">
        <span class="text-slate-500">Size:</span>
        <span class="text-slate-400">—</span>
      </div>
      <div class="flex justify-between">
        <span class="text-slate-500">Format:</span>
        <span class="text-slate-400">—</span>
      </div>
    </div>
  </Card>
</template>