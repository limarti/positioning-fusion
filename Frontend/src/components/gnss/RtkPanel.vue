<template>
  <Card title="RTK"
        iconColor="bg-gray-500">

    <!-- Mode Selection Section -->
    <div class="mb-6">
      <div class="text-sm font-semibold text-gray-700 mb-3">
        Operating Mode
      </div>

      <!-- Mode Selection Radio Buttons -->
      <div class="space-y-3">
        <div v-for="option in modeOptions" :key="option.value" class="flex items-center">
          <div class="flex items-center h-5">
            <input :id="option.value"
                   v-model="gnssState.selectedMode"
                   :value="option.value"
                   type="radio"
                   name="mode"
                   :disabled="gnssState.isChangingMode"
                   class="h-4 w-4 border-gray-300 focus:ring-2 text-gray-600 focus:ring-gray-500"
                   @change="onModeChange(option.value)">
          </div>
          <div class="ml-3 text-sm">
            <label :for="option.value" class="font-medium text-gray-700 cursor-pointer">
              {{ option.label }}
            </label>
            <p class="text-sm text-gray-500">
              {{ option.description }}
            </p>
          </div>
        </div>
      </div>

      <!-- Loading State -->
      <div v-if="gnssState.isChangingMode" class="mt-4 flex items-center justify-center">
        <div class="flex items-center space-x-2 text-gray-600">
          <svg class="animate-spin h-4 w-4" fill="none" viewBox="0 0 24 24">
            <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4" />
            <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
          </svg>
          <span class="text-sm">Changing mode...</span>
        </div>
      </div>
    </div>

    <!-- RTK Status and Data (when active) -->
    <div v-if="gnssState.gnssData.corrections.mode !== 'Disabled'" class="space-y-6">
      <!-- RTK Solution Status (Both Modes) -->
      <div class="flex items-center justify-between py-2">
        <span class="text-sm text-gray-600">Solution Status:</span>
        <span class="text-xs font-semibold px-3 py-1 rounded-lg"
              :class="gnssState.gnssData.rtkMode === 'Fixed' ? 'bg-green-100 text-green-800' : 'bg-yellow-100 text-yellow-700'">
          {{ gnssState.gnssData.rtkMode }}
        </span>
      </div>

      <!-- Position Accuracy (Both Modes) -->
      <div class="border-t border-gray-200 pt-4">
        <div class="space-y-2">
          <div class="text-sm text-gray-600 mb-3">
            Position Accuracy
          </div>
          <div class="flex justify-between py-1">
            <span class="text-sm text-gray-600">North:</span>
            <span class="text-sm font-medium" :class="gnssState.gnssData.rtk.relativeAccuracy.north !== null ? 'text-gray-800' : 'text-slate-400'">{{ gnssState.gnssData.rtk.relativeAccuracy.north !== null ? gnssState.gnssData.rtk.relativeAccuracy.north.toFixed(3) + 'm' : '—' }}</span>
          </div>
          <div class="flex justify-between py-1">
            <span class="text-sm text-gray-600">East:</span>
            <span class="text-sm font-medium" :class="gnssState.gnssData.rtk.relativeAccuracy.east !== null ? 'text-gray-800' : 'text-slate-400'">{{ gnssState.gnssData.rtk.relativeAccuracy.east !== null ? gnssState.gnssData.rtk.relativeAccuracy.east.toFixed(3) + 'm' : '—' }}</span>
          </div>
          <div class="flex justify-between py-1">
            <span class="text-sm text-gray-600">Down:</span>
            <span class="text-sm font-medium" :class="gnssState.gnssData.rtk.relativeAccuracy.down !== null ? 'text-gray-800' : 'text-slate-400'">{{ gnssState.gnssData.rtk.relativeAccuracy.down !== null ? gnssState.gnssData.rtk.relativeAccuracy.down.toFixed(3) + 'm' : '—' }}</span>
          </div>
        </div>
      </div>

      <!-- Rover-Specific Data -->
      <div v-if="gnssState.gnssData.corrections.mode === 'Receive'" class="border-t border-gray-200 pt-4 space-y-4">
        <div class="flex justify-between py-1">
          <span class="text-sm text-gray-600">Baseline Distance:</span>
          <span class="text-sm font-medium" :class="gnssState.gnssData.rtk.baselineLength !== null ? 'text-gray-800' : 'text-slate-400'">{{ gnssState.gnssData.rtk.baselineLength !== null ? gnssState.gnssData.rtk.baselineLength.toFixed(0) + 'm' : '—' }}</span>
        </div>
        <div class="space-y-2">
          <div class="flex justify-between py-1">
            <span class="text-sm text-gray-600">Solution Confidence:</span>
            <span class="text-sm font-medium" :class="gnssState.gnssData.rtk.arRatio !== null ? 'text-gray-800' : 'text-slate-400'">{{ gnssState.gnssData.rtk.arRatio !== null ? gnssState.gnssData.rtk.arRatio.toFixed(1) : '—' }}</span>
          </div>
          <div class="flex justify-between py-1">
            <span class="text-sm text-gray-600">Correction Age:</span>
            <span class="text-sm font-medium" :class="getCorrectionAgeClass()">{{ formatCorrectionAge() }}</span>
          </div>
        </div>

        <!-- Enhanced Correction Status -->
        <div class="space-y-2">
          <div class="flex justify-between py-1">
            <span class="text-sm text-gray-600">Correction Source:</span>
            <div class="flex items-center space-x-2">
              <span class="text-xs font-semibold px-2 py-1 rounded-lg" :class="getCorrectionSourceClass()">
                {{ gnssState.gnssData.corrections.status.source }}
              </span>
              <div class="flex space-x-1">
                <span v-if="gnssState.gnssData.corrections.status.sbas" class="text-xs bg-green-100 text-green-700 px-1 py-0.5 rounded">SBAS</span>
                <span v-if="gnssState.gnssData.corrections.status.rtcm" class="text-xs bg-blue-100 text-blue-700 px-1 py-0.5 rounded">RTCM</span>
                <span v-if="gnssState.gnssData.corrections.status.spartn" class="text-xs bg-purple-100 text-purple-700 px-1 py-0.5 rounded">SPARTN</span>
              </div>
            </div>
          </div>
          <div class="flex justify-between py-1">
            <span class="text-sm text-gray-600">Correction Status:</span>
            <span class="text-xs font-semibold px-2 py-1 rounded-lg" :class="getCorrectionStatusClass()">
              {{ gnssState.gnssData.corrections.status.status }}
            </span>
          </div>
        </div>
      </div>

      <!-- Radio Communication (Both Modes) -->
      <div class="border-t border-gray-200 pt-4">
        <!-- Base Station Mode: Show LoRa Out (corrections being sent) -->
        <div v-if="gnssState.gnssData.corrections.mode === 'Send'" class="flex justify-between py-1">
          <span class="text-sm text-gray-600">Radio Throughput:</span>
          <span class="text-sm font-medium" :class="systemState.dataRates.kbpsLoRaOut !== null && systemState.dataRates.kbpsLoRaOut > 0 ? 'text-gray-800' : 'text-slate-400'">
            {{ systemState.dataRates.kbpsLoRaOut !== null ? systemState.dataRates.kbpsLoRaOut.toFixed(1) + ' kbps' : '—' }}
          </span>
        </div>
        <!-- Rover Mode: Show LoRa In (corrections being received) -->
        <div v-if="gnssState.gnssData.corrections.mode === 'Receive'" class="flex justify-between py-1">
          <span class="text-sm text-gray-600">Radio Throughput:</span>
          <span class="text-sm font-medium" :class="systemState.dataRates.kbpsLoRaIn !== null && systemState.dataRates.kbpsLoRaIn > 0 ? 'text-gray-800' : 'text-slate-400'">
            {{ systemState.dataRates.kbpsLoRaIn !== null ? systemState.dataRates.kbpsLoRaIn.toFixed(1) + ' kbps' : '—' }}
          </span>
        </div>
      </div>

      <!-- Base Station Setup (Base Station Mode Only) -->
      <div v-if="gnssState.gnssData.corrections.mode === 'Send'" class="border-t border-gray-200 pt-4 space-y-4">
        <div class="text-sm font-semibold text-gray-800">
          Base Station Setup
        </div>

        <div class="space-y-3">
          <div class="flex justify-between">
            <span class="text-sm text-gray-600">Survey Status:</span>
            <span class="text-xs font-semibold px-2 py-1 rounded-lg"
                  :class="gnssState.gnssData.surveyIn.valid ? 'bg-green-100 text-green-800' :
                    gnssState.gnssData.surveyIn.active ? 'bg-blue-100 text-blue-700' :
                    'bg-red-100 text-red-700'">
              {{ gnssState.gnssData.surveyIn.valid ? 'COMPLETED' :
                gnssState.gnssData.surveyIn.active ? 'ACTIVE' : 'INACTIVE' }}
            </span>
          </div>
          <div class="flex justify-between">
            <span class="text-sm text-gray-600">Survey Duration:</span>
            <span class="text-sm font-medium text-gray-800">{{ gnssState.gnssData.surveyIn.duration !== null ? gnssState.gnssData.surveyIn.duration + 's' : '—' }}</span>
          </div>
          <div class="flex justify-between">
            <span class="text-sm text-gray-600">Survey Accuracy:</span>
            <span class="text-sm font-medium text-gray-800">{{ gnssState.gnssData.surveyIn.accuracyMm !== null ? (gnssState.gnssData.surveyIn.accuracyMm / 1000).toFixed(2) + 'm' : '—' }}</span>
          </div>
        </div>

        <!-- Reference Station Position -->
        <div class="space-y-3">
          <div class="text-sm font-semibold text-gray-800">
            Reference Position
          </div>
          <div class="space-y-2">
            <div class="flex justify-between">
              <span class="text-sm text-gray-600">Latitude:</span>
              <span class="text-sm font-medium" :class="gnssState.gnssData.referenceStation.latitude !== null ? 'text-gray-800' : 'text-slate-400'">
                {{ gnssState.gnssData.referenceStation.latitude !== null ? gnssState.gnssData.referenceStation.latitude.toFixed(6) + '°' : '—' }}
              </span>
            </div>
            <div class="flex justify-between">
              <span class="text-sm text-gray-600">Longitude:</span>
              <span class="text-sm font-medium" :class="gnssState.gnssData.referenceStation.longitude !== null ? 'text-gray-800' : 'text-slate-400'">
                {{ gnssState.gnssData.referenceStation.longitude !== null ? gnssState.gnssData.referenceStation.longitude.toFixed(6) + '°' : '—' }}
              </span>
            </div>
            <div class="flex justify-between">
              <span class="text-sm text-gray-600">Elevation:</span>
              <span class="text-sm font-medium" :class="gnssState.gnssData.referenceStation.altitude !== null ? 'text-gray-800' : 'text-slate-400'">
                {{ gnssState.gnssData.referenceStation.altitude !== null ? gnssState.gnssData.referenceStation.altitude.toFixed(1) + 'm' : '—' }}
              </span>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Disabled State Message -->
    <div v-else class="text-center py-6">
      <div class="text-sm text-gray-600 mb-2">
        RTK Corrections Disabled
      </div>
      <div class="text-sm text-gray-500">
        Select Base Station or Rover mode above to enable RTK corrections
      </div>
    </div>
  </Card>
</template>

<script setup>
  import { computed, watch } from 'vue';
  import Card from '../common/Card.vue';
  import { useGnssData } from '@/composables/useGnssData';
  import { useSystemData } from '@/composables/useSystemData';
  import { useSignalR } from '@/composables/useSignalR';

  // Get data from composables
  const { state: gnssState, modeOptions, handleModeChange } = useGnssData();
  const { state: systemState } = useSystemData();
  const { signalrConnection } = useSignalR();

  // Handle mode change using the composable function
  const onModeChange = async (newMode) =>
  {
    await handleModeChange(signalrConnection.value, newMode);
  };

  // Correction status formatting and styling
  const formatCorrectionAge = () =>
  {
    const ageMs = gnssState.gnssData.corrections.status.age;
    if (ageMs === null || ageMs === undefined) return '—';

    // Convert milliseconds to seconds
    const ageSeconds = ageMs / 1000;

    if (ageSeconds < 60) 
    {
      return ageSeconds.toFixed(1) + 's';
    }
    else 
    {
      return (ageSeconds / 60).toFixed(1) + 'm';
    }
  };

  const getCorrectionAgeClass = () =>
  {
    const ageMs = gnssState.gnssData.corrections.status.age;
    if (ageMs === null || ageMs === undefined) return 'text-slate-400';

    const ageSeconds = ageMs / 1000;

    // Color code based on correction age
    if (ageSeconds <= 5) return 'text-green-600 font-medium'; // Fresh
    else if (ageSeconds <= 30) return 'text-yellow-600 font-medium'; // Aging
    else return 'text-red-600 font-medium'; // Stale
  };

  const getCorrectionSourceClass = () =>
  {
    const source = gnssState.gnssData.corrections.status.source;
    const valid = gnssState.gnssData.corrections.status.valid;

    if (!valid || source === 'None') 
    {
      return 'bg-gray-100 text-gray-600';
    }

    switch (source) 
    {
    case 'RTCM': return 'bg-blue-100 text-blue-800';
    case 'SPARTN': return 'bg-purple-100 text-purple-800';
    case 'SBAS': return 'bg-green-100 text-green-800';
    default: return 'bg-gray-100 text-gray-600';
    }
  };

  const getCorrectionStatusClass = () =>
  {
    const status = gnssState.gnssData.corrections.status.status;

    switch (status) 
    {
    case 'Valid': return 'bg-green-100 text-green-800';
    case 'Stale': return 'bg-yellow-100 text-yellow-700';
    case 'Invalid': return 'bg-red-100 text-red-700';
    default: return 'bg-gray-100 text-gray-600';
    }
  };
</script>