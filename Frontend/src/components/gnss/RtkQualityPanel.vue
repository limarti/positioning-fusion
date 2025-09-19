<script setup>
const props = defineProps({
  gnssData: {
    type: Object,
    required: true
  },
  dataRates: {
    type: Object,
    required: true
  }
})
</script>

<template>
  <div v-if="gnssData.rtk.active" class="bg-white rounded-xl border border-slate-200 p-4 break-inside-avoid mb-6">
    <div class="flex items-center justify-between mb-4">
      <div class="flex items-center space-x-3">
        <svg class="w-6 h-6 text-emerald-600" fill="currentColor" viewBox="0 0 24 24">
          <path d="M12,2A3,3 0 0,1 15,5V11A3,3 0 0,1 12,14A3,3 0 0,1 9,11V5A3,3 0 0,1 12,2M19,11C19,14.53 16.39,17.44 13,17.93V21H11V17.93C7.61,17.44 5,14.53 5,11H7A5,5 0 0,0 12,16A5,5 0 0,0 17,11H19Z"/>
        </svg>
        <h2 class="text-lg font-bold text-slate-800">RTK Quality</h2>
        <div class="text-sm font-semibold" :class="dataRates.correctionRate !== null ? 'text-emerald-600' : 'text-slate-400'">{{ dataRates.correctionRate !== null ? dataRates.correctionRate : '—' }}</div>
      </div>
      <span class="text-sm font-bold px-3 py-1 rounded-lg" 
            :class="gnssData.rtkMode === 'Fixed' ? 'bg-emerald-100 text-emerald-700' : 'bg-yellow-100 text-yellow-700'">
        {{ gnssData.rtkMode }}
      </span>
    </div>
    
    <div class="grid grid-cols-2 gap-4 mb-6">
      <div class="text-center p-4 bg-emerald-50 rounded-xl">
        <div class="text-sm text-slate-600 mb-1">AR Ratio</div>
        <div class="text-lg font-bold" :class="gnssData.rtk.arRatio !== null ? 'text-emerald-700' : 'text-slate-400'">{{ gnssData.rtk.arRatio !== null ? gnssData.rtk.arRatio.toFixed(1) : '—' }}</div>
      </div>
      <div class="text-center p-4 bg-blue-50 rounded-xl">
        <div class="text-sm text-slate-600 mb-1">Correction Age</div>
        <div class="text-lg font-bold" :class="gnssData.rtk.correctionAge !== null ? 'text-blue-700' : 'text-slate-400'">{{ gnssData.rtk.correctionAge !== null ? gnssData.rtk.correctionAge.toFixed(1) + 's' : '—' }}</div>
      </div>
    </div>
    
    <div class="space-y-2">
      <div class="flex justify-between p-3 bg-slate-50 rounded-lg">
        <span class="text-slate-600">Baseline Length:</span>
        <span class="font-bold" :class="gnssData.rtk.baselineLength !== null ? '' : 'text-slate-400'">{{ gnssData.rtk.baselineLength !== null ? gnssData.rtk.baselineLength.toFixed(0) + 'm' : '—' }}</span>
      </div>
      <div class="p-3 bg-slate-50 rounded-lg">
        <div class="text-sm text-slate-600 mb-2">Relative Accuracy</div>
        <div class="grid grid-cols-3 gap-2 text-sm">
          <div class="text-center">
            <div class="font-mono font-semibold" :class="gnssData.rtk.relativeAccuracy.north !== null ? '' : 'text-slate-400'">{{ gnssData.rtk.relativeAccuracy.north !== null ? gnssData.rtk.relativeAccuracy.north.toFixed(3) + 'm' : '—' }}</div>
            <div class="text-slate-500">North</div>
          </div>
          <div class="text-center">
            <div class="font-mono font-semibold" :class="gnssData.rtk.relativeAccuracy.east !== null ? '' : 'text-slate-400'">{{ gnssData.rtk.relativeAccuracy.east !== null ? gnssData.rtk.relativeAccuracy.east.toFixed(3) + 'm' : '—' }}</div>
            <div class="text-slate-500">East</div>
          </div>
          <div class="text-center">
            <div class="font-mono font-semibold" :class="gnssData.rtk.relativeAccuracy.down !== null ? '' : 'text-slate-400'">{{ gnssData.rtk.relativeAccuracy.down !== null ? gnssData.rtk.relativeAccuracy.down.toFixed(3) + 'm' : '—' }}</div>
            <div class="text-slate-500">Down</div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>