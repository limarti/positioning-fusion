<script setup>
import Card from './common/Card.vue'

const props = defineProps({
  imuData: {
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
        <div class="font-mono" :class="imuData.acceleration.x !== null ? 'text-red-600' : 'text-slate-400'">{{ imuData.acceleration.x !== null ? imuData.acceleration.x.toFixed(1) : '—' }}</div>
        <div class="font-mono" :class="imuData.acceleration.y !== null ? 'text-red-600' : 'text-slate-400'">{{ imuData.acceleration.y !== null ? imuData.acceleration.y.toFixed(1) : '—' }}</div>
        <div class="font-mono" :class="imuData.acceleration.z !== null ? 'text-red-600' : 'text-slate-400'">{{ imuData.acceleration.z !== null ? imuData.acceleration.z.toFixed(1) : '—' }}</div>
      </div>
      <div class="text-center">
        <div class="text-slate-500">Gyro</div>
        <div class="font-mono" :class="imuData.gyroscope.x !== null ? 'text-blue-600' : 'text-slate-400'">{{ imuData.gyroscope.x !== null ? imuData.gyroscope.x.toFixed(2) : '—' }}</div>
        <div class="font-mono" :class="imuData.gyroscope.y !== null ? 'text-blue-600' : 'text-slate-400'">{{ imuData.gyroscope.y !== null ? imuData.gyroscope.y.toFixed(2) : '—' }}</div>
        <div class="font-mono" :class="imuData.gyroscope.z !== null ? 'text-blue-600' : 'text-slate-400'">{{ imuData.gyroscope.z !== null ? imuData.gyroscope.z.toFixed(2) : '—' }}</div>
      </div>
      <div class="text-center">
        <div class="text-slate-500">Mag</div>
        <div class="font-mono" :class="imuData.magnetometer.x !== null ? 'text-green-600' : 'text-slate-400'">{{ imuData.magnetometer.x !== null ? imuData.magnetometer.x.toFixed(0) : '—' }}</div>
        <div class="font-mono" :class="imuData.magnetometer.y !== null ? 'text-green-600' : 'text-slate-400'">{{ imuData.magnetometer.y !== null ? imuData.magnetometer.y.toFixed(0) : '—' }}</div>
        <div class="font-mono" :class="imuData.magnetometer.z !== null ? 'text-green-600' : 'text-slate-400'">{{ imuData.magnetometer.z !== null ? imuData.magnetometer.z.toFixed(0) : '—' }}</div>
      </div>
    </div>
  </Card>
</template>