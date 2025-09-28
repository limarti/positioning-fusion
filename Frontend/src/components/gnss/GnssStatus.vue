<script setup>
import Card from '../common/Card.vue'
import { useGnssData } from '@/composables/useGnssData'

// Get data from composable
const { state: gnssState } = useGnssData()

const formatAccuracy = (meters) => {
  if (meters === null || meters === undefined) return '—'

  // Convert to millimeters
  const mm = meters * 1000

  // If >= 1000mm (1m), show in meters
  if (mm >= 1000) {
    return (mm / 1000).toFixed(1) + 'm'
  }
  // If >= 10mm (1cm), show in centimeters
  else if (mm >= 10) {
    return (mm / 10).toFixed(0) + 'cm'
  }
  // Otherwise show in millimeters
  else {
    return mm.toFixed(0) + 'mm'
  }
}

</script>

<template>
  <Card
    title="GNSS Status"
  >
    <div class="space-y-6">
      <!-- Position and Fix Status -->
      <div class="flex items-center justify-between">
        <div class="flex items-center space-x-4">
          <div>
            <div class="text-sm text-gray-600">Current Position</div>
            <div class="text-base font-mono text-gray-800">
              {{ gnssState.gnssData.latitude !== null && gnssState.gnssData.longitude !== null
                  ? `${gnssState.gnssData.latitude.toFixed(7)}°, ${gnssState.gnssData.longitude.toFixed(7)}°`
                  : 'Waiting for GNSS fix...' }}
            </div>
            <div v-if="gnssState.gnssData.altitude !== null" class="text-sm text-gray-600 font-mono mt-1">
              Altitude: {{ gnssState.gnssData.altitude.toFixed(2) }}m
            </div>
          </div>
        </div>
        <div class="text-right">
          <div class="text-sm text-gray-600">Fix Type</div>
          <div class="text-base font-bold text-gray-800">
            {{ gnssState.gnssData.fixType || 'No Fix' }}
          </div>
        </div>
      </div>
      
      <!-- Core Health Summary -->
      <div class="border-t border-gray-200 pt-4">
        <div class="grid grid-cols-2 md:grid-cols-6 gap-4 text-center text-sm">
          <div>
            <div class="text-sm text-gray-500 mb-1">hAcc</div>
            <div class="text-base font-bold" :class="gnssState.gnssData.hAcc !== null ? 'text-gray-800' : 'text-slate-400'">{{ formatAccuracy(gnssState.gnssData.hAcc) }}</div>
          </div>
          <div>
            <div class="text-sm text-gray-500 mb-1">vAcc</div>
            <div class="text-base font-bold" :class="gnssState.gnssData.vAcc !== null ? 'text-gray-800' : 'text-slate-400'">{{ formatAccuracy(gnssState.gnssData.vAcc) }}</div>
          </div>
          <div>
            <div class="text-sm text-gray-500 mb-1">HDOP</div>
            <div class="text-base font-bold" :class="gnssState.gnssData.hdop !== null ? 'text-gray-700' : 'text-slate-400'">{{ gnssState.gnssData.hdop !== null ? gnssState.gnssData.hdop.toFixed(2) : '—' }}</div>
          </div>
          <div>
            <div class="text-sm text-gray-500 mb-1">VDOP</div>
            <div class="text-base font-bold" :class="gnssState.gnssData.vdop !== null ? 'text-gray-700' : 'text-slate-400'">{{ gnssState.gnssData.vdop !== null ? gnssState.gnssData.vdop.toFixed(2) : '—' }}</div>
          </div>
          <div>
            <div class="text-sm text-gray-500 mb-1">PDOP</div>
            <div class="text-base font-bold" :class="gnssState.gnssData.pdop !== null ? 'text-gray-700' : 'text-slate-400'">{{ gnssState.gnssData.pdop !== null ? gnssState.gnssData.pdop.toFixed(2) : '—' }}</div>
          </div>
          <div>
            <div class="text-sm text-gray-500 mb-1">Satellites</div>
            <div class="text-base font-bold" :class="gnssState.gnssData.satellitesUsed !== null ? 'text-gray-800' : 'text-slate-400'">{{ gnssState.gnssData.satellitesUsed !== null ? gnssState.gnssData.satellitesUsed + '/' + gnssState.gnssData.satellitesTracked : '—' }}</div>
          </div>
        </div>
      </div>
    </div>
  </Card>
</template>