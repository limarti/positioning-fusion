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
  <div v-if="gnssData.corrections.mode !== 'Disabled'" class="bg-white rounded-xl border border-slate-200 p-4 break-inside-avoid mb-6">
    <div class="flex items-center justify-between mb-4">
      <div class="flex items-center space-x-3">
        <svg class="w-6 h-6" :class="gnssData.corrections.mode === 'Send' ? 'text-blue-600' : 'text-purple-600'" fill="currentColor" viewBox="0 0 24 24">
          <path d="M12,3L14,8H10L12,3M12,8V22H11V8H10L12,3L14,8H13V22H12M4,12L6,9V15L4,12M20,12L18,9V15L20,12M8,14L9,11V17L8,14M16,14L15,11V17L16,14"/>
        </svg>
        <h2 class="text-lg font-bold text-slate-800">RTK</h2>
        <div class="text-sm font-semibold" :class="dataRates.correctionRate !== null ? 'text-emerald-600' : 'text-slate-400'">{{ dataRates.correctionRate !== null ? dataRates.correctionRate : '—' }}</div>
      </div>
      
      <!-- Mode Badge -->
      <span class="text-sm font-bold px-3 py-1 rounded-lg"
            :class="gnssData.corrections.mode === 'Send' ? 'bg-blue-100 text-blue-700' : 'bg-purple-100 text-purple-700'">
        {{ gnssData.corrections.mode === 'Send' ? 'SENDING' : 'RECEIVING' }}
      </span>
    </div>

    <!-- Survey-In Status (Base Station Mode) -->
    <div v-if="gnssData.corrections.mode === 'Send'" class="mb-4">
      <div class="bg-blue-50 rounded-xl p-4 border border-blue-200">
        <div class="flex items-center justify-between mb-3">
          <div class="text-sm font-semibold text-blue-800">Survey-In Status</div>
          <span class="text-xs font-bold px-2 py-1 rounded-lg"
                :class="gnssData.surveyIn.valid ? 'bg-emerald-100 text-emerald-700' : 
                       gnssData.surveyIn.active ? 'bg-yellow-100 text-yellow-700' : 
                       'bg-slate-100 text-slate-700'">
            {{ gnssData.surveyIn.valid ? '✅ COMPLETED' : 
               gnssData.surveyIn.active ? '📍 ACTIVE' : '❌ INACTIVE' }}
          </span>
        </div>
        
        <div class="grid grid-cols-3 gap-3">
          <div class="text-center">
            <div class="text-xs text-blue-600 mb-1">Duration</div>
            <div class="font-bold text-blue-800">{{ gnssData.surveyIn.duration !== null ? gnssData.surveyIn.duration + 's' : '—' }}</div>
          </div>
          <div class="text-center">
            <div class="text-xs text-blue-600 mb-1">Observations</div>
            <div class="font-bold text-blue-800">{{ gnssData.surveyIn.observations !== null ? gnssData.surveyIn.observations : '—' }}</div>
          </div>
          <div class="text-center">
            <div class="text-xs text-blue-600 mb-1">Accuracy</div>
            <div class="font-bold text-blue-800">{{ gnssData.surveyIn.accuracyMm !== null ? (gnssData.surveyIn.accuracyMm / 1000).toFixed(2) + 'm' : '—' }}</div>
          </div>
        </div>
      </div>
    </div>

    <!-- LoRa Data Rates -->
    <div class="mb-4">
      <div class="bg-amber-50 rounded-xl p-4 border border-amber-200">
        <div class="text-sm font-semibold text-amber-800 mb-3">LoRa Radio</div>
        <div class="grid grid-cols-2 gap-3">
          <div class="text-center">
            <div class="text-xs text-amber-600 mb-1 flex items-center justify-center">
              <svg class="w-3 h-3 mr-1" fill="currentColor" viewBox="0 0 24 24">
                <path d="M7,14L12,9L17,14H7Z"/>
              </svg>
              Data In
            </div>
            <div class="font-bold text-amber-800">{{ dataRates.kbpsLoRaIn !== null && dataRates.kbpsLoRaIn !== undefined ? dataRates.kbpsLoRaIn.toFixed(1) + ' kbps' : '—' }}</div>
          </div>
          <div class="text-center">
            <div class="text-xs text-amber-600 mb-1 flex items-center justify-center">
              <svg class="w-3 h-3 mr-1" fill="currentColor" viewBox="0 0 24 24">
                <path d="M7,10L12,15L17,10H7Z"/>
              </svg>
              Data Out
            </div>
            <div class="font-bold text-amber-800">{{ dataRates.kbpsLoRaOut !== null && dataRates.kbpsLoRaOut !== undefined ? dataRates.kbpsLoRaOut.toFixed(1) + ' kbps' : '—' }}</div>
          </div>
        </div>
      </div>
    </div>

    <!-- Connection Status -->
    <div class="grid grid-cols-1 gap-3">
      <div class="flex justify-between p-3 bg-slate-50 rounded-lg">
        <span class="text-slate-600">Mode:</span>
        <span class="font-bold" :class="gnssData.corrections.mode === 'Send' ? 'text-blue-700' : 'text-purple-700'">
          {{ gnssData.corrections.mode === 'Send' ? 'Base Station (Sending)' : 'Rover (Receiving)' }}
        </span>
      </div>
    </div>
  </div>
</template>