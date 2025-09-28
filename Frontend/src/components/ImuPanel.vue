<script setup>
import Card from './common/Card.vue'
import { useSystemData } from '@/composables/useSystemData'

// Get data from composable
const { state: systemState } = useSystemData()
</script>

<template>
  <!-- IMU -->
  <Card 
    title="IMU" 
    subtitle=""
    :icon="`<svg fill='currentColor' viewBox='0 0 24 24'><path d='M12 2L15.5 8.5L22 12L15.5 15.5L12 22L8.5 15.5L2 12L8.5 8.5L12 2Z'/></svg>`"
    icon-color="bg-gray-500"
  >
    <div class="grid grid-cols-3 gap-2 text-sm">
      <div class="text-center">
        <div class="text-slate-500">Accel</div>
        <div class="font-mono" :class="systemState.imuData.acceleration.x !== null ? 'text-red-600' : 'text-slate-400'">{{ systemState.imuData.acceleration.x !== null ? systemState.imuData.acceleration.x.toFixed(1) : '—' }}</div>
        <div class="font-mono" :class="systemState.imuData.acceleration.y !== null ? 'text-red-600' : 'text-slate-400'">{{ systemState.imuData.acceleration.y !== null ? systemState.imuData.acceleration.y.toFixed(1) : '—' }}</div>
        <div class="font-mono" :class="systemState.imuData.acceleration.z !== null ? 'text-red-600' : 'text-slate-400'">{{ systemState.imuData.acceleration.z !== null ? systemState.imuData.acceleration.z.toFixed(1) : '—' }}</div>
      </div>
      <div class="text-center">
        <div class="text-slate-500">Gyro</div>
        <div class="font-mono" :class="systemState.imuData.gyroscope.x !== null ? 'text-blue-600' : 'text-slate-400'">{{ systemState.imuData.gyroscope.x !== null ? systemState.imuData.gyroscope.x.toFixed(2) : '—' }}</div>
        <div class="font-mono" :class="systemState.imuData.gyroscope.y !== null ? 'text-blue-600' : 'text-slate-400'">{{ systemState.imuData.gyroscope.y !== null ? systemState.imuData.gyroscope.y.toFixed(2) : '—' }}</div>
        <div class="font-mono" :class="systemState.imuData.gyroscope.z !== null ? 'text-blue-600' : 'text-slate-400'">{{ systemState.imuData.gyroscope.z !== null ? systemState.imuData.gyroscope.z.toFixed(2) : '—' }}</div>
      </div>
      <div class="text-center">
        <div class="text-slate-500">Mag</div>
        <div class="font-mono" :class="systemState.imuData.magnetometer.x !== null ? 'text-green-600' : 'text-slate-400'">{{ systemState.imuData.magnetometer.x !== null ? systemState.imuData.magnetometer.x.toFixed(0) : '—' }}</div>
        <div class="font-mono" :class="systemState.imuData.magnetometer.y !== null ? 'text-green-600' : 'text-slate-400'">{{ systemState.imuData.magnetometer.y !== null ? systemState.imuData.magnetometer.y.toFixed(0) : '—' }}</div>
        <div class="font-mono" :class="systemState.imuData.magnetometer.z !== null ? 'text-green-600' : 'text-slate-400'">{{ systemState.imuData.magnetometer.z !== null ? systemState.imuData.magnetometer.z.toFixed(0) : '—' }}</div>
      </div>
    </div>
  </Card>
</template>