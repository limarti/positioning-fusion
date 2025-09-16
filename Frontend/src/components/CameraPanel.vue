<script setup>
// No props needed for camera panel currently as it shows static data
</script>

<template>
  <!-- Camera -->
  <div class="bg-white rounded-xl border border-slate-200 p-4">
    <div class="flex items-center space-x-2 mb-3">
      <div class="w-6 h-6 bg-cyan-500 rounded-lg flex items-center justify-center">
        <svg class="w-4 h-4 text-white" fill="currentColor" viewBox="0 0 24 24">
          <path d="M12,15A2,2 0 0,1 10,13A2,2 0 0,1 12,11A2,2 0 0,1 14,13A2,2 0 0,1 12,15M22,6H19L17.83,4.5C17.42,3.87 16.75,3.5 16,3.5H8C7.25,3.5 6.58,3.87 6.17,4.5L5,6H2A2,2 0 0,0 0,8V18A2,2 0 0,0 2,20H22A2,2 0 0,0 24,18V8A2,2 0 0,0 22,6M12,17A4,4 0 0,0 16,13A4,4 0 0,0 12,9A4,4 0 0,0 8,13A4,4 0 0,0 12,17Z"/>
        </svg>
      </div>
      <div>
        <h3 class="font-bold text-slate-800">Camera</h3>
        <div class="text-xs text-slate-400">—</div>
      </div>
    </div>
    <div class="space-y-2 text-xs">
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
  </div>
</template>