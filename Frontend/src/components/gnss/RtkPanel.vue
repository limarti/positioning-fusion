<script setup>
const props = defineProps({
  gnssData: {
    type: Object,
    required: true
  }
})
</script>

<template>
  <div class="bg-white rounded-xl border border-slate-200 p-4 break-inside-avoid mb-6">
    <!-- Header -->
    <div class="flex items-center justify-between mb-4">
      <div class="flex items-center space-x-3">
        <svg class="w-6 h-6" :class="gnssData.corrections.mode === 'Disabled' ? 'text-slate-400' : gnssData.corrections.mode === 'Send' ? 'text-blue-600' : 'text-purple-600'" fill="currentColor" viewBox="0 0 24 24">
          <path d="M12,2A3,3 0 0,1 15,5V11A3,3 0 0,1 12,14A3,3 0 0,1 9,11V5A3,3 0 0,1 12,2M19,11C19,14.53 16.39,17.44 13,17.93V21H11V17.93C7.61,17.44 5,14.53 5,11H7A5,5 0 0,0 12,16A5,5 0 0,0 17,11H19Z"/>
        </svg>
        <h2 class="text-lg font-bold" :class="gnssData.corrections.mode === 'Disabled' ? 'text-slate-400' : 'text-slate-800'">RTK</h2>
      </div>
      
      <!-- Mode Badge -->
      <span v-if="gnssData.corrections.mode !== 'Disabled'" class="text-sm font-bold px-3 py-1 rounded-lg"
            :class="gnssData.corrections.mode === 'Send' ? 'bg-blue-100 text-blue-700' : 'bg-purple-100 text-purple-700'">
        {{ gnssData.corrections.mode === 'Send' ? 'BASE STATION' : 'ROVER' }}
      </span>
      <span v-else class="text-sm font-bold px-3 py-1 rounded-lg bg-slate-100 text-slate-500">
        DISABLED
      </span>
    </div>

    <!-- Disabled State -->
    <div v-if="gnssData.corrections.mode === 'Disabled'" class="text-center p-6 bg-slate-50 rounded-xl">
      <div class="text-slate-500 mb-2">RTK Corrections Disabled</div>
      <div class="text-sm text-slate-400">Restart with Base or Rover mode to enable RTK corrections</div>
    </div>

    <!-- Active RTK Content -->
    <div v-else class="space-y-4">
      <!-- RTK Quality Metrics (when active) -->
      <div v-if="gnssData.corrections.mode !== 'Disabled'" class="space-y-4">
        <!-- Quality Status -->
        <div class="flex items-center justify-between p-3 bg-slate-50 rounded-lg">
          <span class="text-slate-600">RTK Status:</span>
          <span class="font-bold px-3 py-1 rounded-lg text-sm" 
                :class="gnssData.rtkMode === 'Fixed' ? 'bg-emerald-100 text-emerald-700' : 'bg-yellow-100 text-yellow-700'">
            {{ gnssData.rtkMode }}
          </span>
        </div>

        <!-- Quality Metrics Grid -->
        <div class="grid grid-cols-2 gap-4">
          <div class="text-center p-3 bg-emerald-50 rounded-xl">
            <div class="text-xs text-slate-600 mb-1">AR Ratio</div>
            <div class="text-lg font-bold" :class="gnssData.rtk.arRatio !== null ? 'text-emerald-700' : 'text-slate-400'">{{ gnssData.rtk.arRatio !== null ? gnssData.rtk.arRatio.toFixed(1) : '—' }}</div>
          </div>
          <div class="text-center p-3 bg-blue-50 rounded-xl">
            <div class="text-xs text-slate-600 mb-1">Correction Age</div>
            <div class="text-lg font-bold" :class="gnssData.rtk.correctionAge !== null ? 'text-blue-700' : 'text-slate-400'">{{ gnssData.rtk.correctionAge !== null ? gnssData.rtk.correctionAge.toFixed(1) + 's' : '—' }}</div>
          </div>
        </div>

        <!-- Baseline and Accuracy -->
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

      <!-- Survey-In Status (Base Station Mode) -->
      <div v-if="gnssData.corrections.mode === 'Send'" class="bg-blue-50 rounded-xl p-4 border border-blue-200">
        <div class="text-sm font-semibold text-blue-800 mb-3">Survey-In Status</div>
        
        <div class="space-y-2 text-sm mb-3">
          <div class="flex justify-between">
            <span class="text-blue-600">Status:</span>
            <span class="text-xs font-bold px-2 py-1 rounded-lg"
                  :class="gnssData.surveyIn.valid ? 'bg-emerald-100 text-emerald-700' : 
                         gnssData.surveyIn.active ? 'bg-yellow-100 text-yellow-700' : 
                         'bg-slate-100 text-slate-700'">
              {{ gnssData.surveyIn.valid ? '✅ COMPLETED' : 
                 gnssData.surveyIn.active ? '📍 ACTIVE' : '❌ INACTIVE' }}
            </span>
          </div>
          <div class="flex justify-between">
            <span class="text-blue-600">Duration:</span>
            <span class="font-bold text-blue-800">{{ gnssData.surveyIn.duration !== null ? gnssData.surveyIn.duration + 's' : '—' }}</span>
          </div>
          <div class="flex justify-between">
            <span class="text-blue-600">Observations:</span>
            <span class="font-bold text-blue-800">{{ gnssData.surveyIn.observations !== null ? gnssData.surveyIn.observations : '—' }}</span>
          </div>
          <div class="flex justify-between">
            <span class="text-blue-600">Accuracy:</span>
            <span class="font-bold text-blue-800">{{ gnssData.surveyIn.accuracyMm !== null ? (gnssData.surveyIn.accuracyMm / 1000).toFixed(2) + 'm' : '—' }}</span>
          </div>
        </div>

        
        <!-- Divider -->
        <div class="border-t border-blue-200 my-3"></div>
        
        <!-- Reference Station Position -->
        <div>
          <div class="text-xs font-semibold text-blue-800 mb-2">Broadcasting Position</div>
          <div class="space-y-2 text-sm">
            <div class="flex justify-between">
              <span class="text-blue-600">Latitude:</span>
              <span class="font-mono font-semibold" :class="gnssData.referenceStation.latitude !== null ? 'text-blue-800' : 'text-slate-400'">
                {{ gnssData.referenceStation.latitude !== null ? gnssData.referenceStation.latitude.toFixed(8) + '°' : '—' }}
              </span>
            </div>
            <div class="flex justify-between">
              <span class="text-blue-600">Longitude:</span>
              <span class="font-mono font-semibold" :class="gnssData.referenceStation.longitude !== null ? 'text-blue-800' : 'text-slate-400'">
                {{ gnssData.referenceStation.longitude !== null ? gnssData.referenceStation.longitude.toFixed(8) + '°' : '—' }}
              </span>
            </div>
            <div class="flex justify-between">
              <span class="text-blue-600">Altitude:</span>
              <span class="font-mono font-semibold" :class="gnssData.referenceStation.altitude !== null ? 'text-blue-800' : 'text-slate-400'">
                {{ gnssData.referenceStation.altitude !== null ? gnssData.referenceStation.altitude.toFixed(3) + 'm' : '—' }}
              </span>
            </div>
          </div>
        </div>
      </div>


      <!-- Mode Summary -->
      <div class="flex justify-between p-3 bg-slate-50 rounded-lg">
        <span class="text-slate-600">Mode:</span>
        <span class="font-bold" :class="gnssData.corrections.mode === 'Send' ? 'text-blue-700' : 'text-purple-700'">
          {{ gnssData.corrections.mode === 'Send' ? 'Base Station (Sending)' : 'Rover (Receiving)' }}
        </span>
      </div>
    </div>
  </div>
</template>