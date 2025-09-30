<template>
  <!-- IMU -->
  <Card title="IMU"
        subtitle=""
        :icon="`<svg fill='currentColor' viewBox='0 0 24 24'><path d='M12 2L15.5 8.5L22 12L15.5 15.5L12 22L8.5 15.5L2 12L8.5 8.5L12 2Z'/></svg>`"
        iconColor="bg-gray-500">
    <div class="space-y-4">
      <!-- Visual Representations -->
      <div class="grid grid-cols-3 gap-4">
        <!-- 3D Attitude Visualization -->
        <div class="bg-slate-50 rounded-lg p-4">
          <div class="text-xs text-slate-500 mb-2 text-center">3D Attitude</div>
          <div class="relative w-full aspect-square">
            <svg viewBox="0 0 200 200" class="w-full h-full">
              <!-- Horizon line -->
              <line x1="50" :y1="100 + pitch * 2" x2="150" :y2="100 + pitch * 2"
                    stroke="#94a3b8" stroke-width="2" />

              <!-- Aircraft symbol (rotated by roll) -->
              <g :transform="`rotate(${-roll}, 100, 100)`">
                <!-- Center dot -->
                <circle cx="100" cy="100" r="3" fill="#0f172a" />
                <!-- Wings -->
                <line x1="70" y1="100" x2="90" y2="100" stroke="#0f172a" stroke-width="3" />
                <line x1="110" y1="100" x2="130" y2="100" stroke="#0f172a" stroke-width="3" />
                <!-- Center bar -->
                <line x1="90" y1="100" x2="110" y2="100" stroke="#0f172a" stroke-width="4" />
              </g>
            </svg>
          </div>
          <div class="grid grid-cols-3 gap-1 text-xs mt-2">
            <div class="text-center">
              <div class="text-slate-400">Roll</div>
              <div class="font-mono text-blue-600">{{ roll.toFixed(1) }}°</div>
            </div>
            <div class="text-center">
              <div class="text-slate-400">Pitch</div>
              <div class="font-mono text-blue-600">{{ pitch.toFixed(1) }}°</div>
            </div>
            <div class="text-center">
              <div class="text-slate-400">Yaw</div>
              <div class="font-mono text-blue-600">{{ yaw.toFixed(1) }}°</div>
            </div>
          </div>
        </div>

        <!-- Compass/Heading -->
        <div class="bg-slate-50 rounded-lg p-4">
          <div class="text-xs text-slate-500 mb-2 text-center">Compass</div>
          <div class="relative w-full aspect-square">
            <svg viewBox="0 0 200 200" class="w-full h-full">
              <!-- Compass circle -->
              <circle cx="100" cy="100" r="80" fill="white" stroke="#cbd5e1" stroke-width="2" />

              <!-- Cardinal directions -->
              <text x="100" y="35" text-anchor="middle" class="text-sm font-semibold fill-slate-700">N</text>
              <text x="165" y="105" text-anchor="middle" class="text-sm fill-slate-500">E</text>
              <text x="100" y="175" text-anchor="middle" class="text-sm fill-slate-500">S</text>
              <text x="35" y="105" text-anchor="middle" class="text-sm fill-slate-500">W</text>

              <!-- Tick marks -->
              <g v-for="i in 36" :key="i">
                <line
                  :x1="100 + 75 * Math.sin(i * 10 * Math.PI / 180)"
                  :y1="100 - 75 * Math.cos(i * 10 * Math.PI / 180)"
                  :x2="100 + 80 * Math.sin(i * 10 * Math.PI / 180)"
                  :y2="100 - 80 * Math.cos(i * 10 * Math.PI / 180)"
                  :stroke="i % 9 === 0 ? '#475569' : '#cbd5e1'"
                  :stroke-width="i % 9 === 0 ? 2 : 1"
                />
              </g>

              <!-- Heading needle -->
              <g :transform="`rotate(${heading}, 100, 100)`">
                <path d="M 100 40 L 105 100 L 100 95 L 95 100 Z" fill="#dc2626" />
                <path d="M 100 160 L 105 100 L 100 105 L 95 100 Z" fill="#64748b" />
              </g>

              <!-- Center circle -->
              <circle cx="100" cy="100" r="8" fill="white" stroke="#0f172a" stroke-width="2" />
            </svg>
          </div>
          <div class="text-center text-xs mt-2">
            <div class="text-slate-400">Heading</div>
            <div class="font-mono text-green-600 text-lg">{{ heading.toFixed(1) }}°</div>
          </div>
        </div>

        <!-- Tilt/Stability Bubble Level -->
        <div class="bg-slate-50 rounded-lg p-4">
          <div class="text-xs text-slate-500 mb-2 text-center">Level</div>
          <div class="relative w-full aspect-square">
            <svg viewBox="0 0 200 200" class="w-full h-full">
              <!-- Outer circle (limit) -->
              <circle cx="100" cy="100" r="80" fill="white" stroke="#cbd5e1" stroke-width="2" />

              <!-- Crosshairs -->
              <line x1="100" y1="30" x2="100" y2="50" stroke="#cbd5e1" stroke-width="1" />
              <line x1="100" y1="150" x2="100" y2="170" stroke="#cbd5e1" stroke-width="1" />
              <line x1="30" y1="100" x2="50" y2="100" stroke="#cbd5e1" stroke-width="1" />
              <line x1="150" y1="100" x2="170" y2="100" stroke="#cbd5e1" stroke-width="1" />

              <!-- Center target circle -->
              <circle cx="100" cy="100" r="15" fill="none" stroke="#cbd5e1" stroke-width="1" stroke-dasharray="2,2" />

              <!-- Bubble (offset by tilt) -->
              <circle
                :cx="100 + tiltX * 60"
                :cy="100 + tiltY * 60"
                r="12"
                :fill="isLevel ? '#22c55e' : '#f59e0b'"
                opacity="0.8"
                stroke="white"
                stroke-width="2"
              />
            </svg>
          </div>
          <div class="text-center text-xs mt-2">
            <div class="text-slate-400">Tilt</div>
            <div class="font-mono" :class="isLevel ? 'text-green-600' : 'text-amber-600'">
              {{ isLevel ? 'Level' : `${tiltAngle.toFixed(1)}°` }}
            </div>
          </div>
        </div>
      </div>

      <!-- Raw Sensor Data -->
      <div class="border-t border-slate-200 pt-4">
        <div class="text-xs text-slate-500 mb-2">Raw Sensor Data</div>
        <div class="grid grid-cols-3 gap-2 text-sm">
          <div class="text-center">
            <div class="text-slate-500">
              Accel
            </div>
            <div class="font-mono" :class="systemState.imuData.acceleration.x !== null ? 'text-red-600' : 'text-slate-400'">
              {{ systemState.imuData.acceleration.x !== null ? systemState.imuData.acceleration.x.toFixed(1) : '—' }}
            </div>
            <div class="font-mono" :class="systemState.imuData.acceleration.y !== null ? 'text-red-600' : 'text-slate-400'">
              {{ systemState.imuData.acceleration.y !== null ? systemState.imuData.acceleration.y.toFixed(1) : '—' }}
            </div>
            <div class="font-mono" :class="systemState.imuData.acceleration.z !== null ? 'text-red-600' : 'text-slate-400'">
              {{ systemState.imuData.acceleration.z !== null ? systemState.imuData.acceleration.z.toFixed(1) : '—' }}
            </div>
          </div>
          <div class="text-center">
            <div class="text-slate-500">
              Gyro
            </div>
            <div class="font-mono" :class="systemState.imuData.gyroscope.x !== null ? 'text-blue-600' : 'text-slate-400'">
              {{ systemState.imuData.gyroscope.x !== null ? systemState.imuData.gyroscope.x.toFixed(2) : '—' }}
            </div>
            <div class="font-mono" :class="systemState.imuData.gyroscope.y !== null ? 'text-blue-600' : 'text-slate-400'">
              {{ systemState.imuData.gyroscope.y !== null ? systemState.imuData.gyroscope.y.toFixed(2) : '—' }}
            </div>
            <div class="font-mono" :class="systemState.imuData.gyroscope.z !== null ? 'text-blue-600' : 'text-slate-400'">
              {{ systemState.imuData.gyroscope.z !== null ? systemState.imuData.gyroscope.z.toFixed(2) : '—' }}
            </div>
          </div>
          <div class="text-center">
            <div class="text-slate-500">
              Mag
            </div>
            <div class="font-mono" :class="systemState.imuData.magnetometer.x !== null ? 'text-green-600' : 'text-slate-400'">
              {{ systemState.imuData.magnetometer.x !== null ? systemState.imuData.magnetometer.x.toFixed(0) : '—' }}
            </div>
            <div class="font-mono" :class="systemState.imuData.magnetometer.y !== null ? 'text-green-600' : 'text-slate-400'">
              {{ systemState.imuData.magnetometer.y !== null ? systemState.imuData.magnetometer.y.toFixed(0) : '—' }}
            </div>
            <div class="font-mono" :class="systemState.imuData.magnetometer.z !== null ? 'text-green-600' : 'text-slate-400'">
              {{ systemState.imuData.magnetometer.z !== null ? systemState.imuData.magnetometer.z.toFixed(0) : '—' }}
            </div>
          </div>
        </div>
      </div>
    </div>
  </Card>
</template>

<script setup>
  import Card from './common/Card.vue';
  import { useSystemData } from '@/composables/useSystemData';
  import { computed } from 'vue';

  // Get data from composable
  const { state: systemState } = useSystemData();

  // Compute roll, pitch, yaw from accelerometer and gyroscope
  const roll = computed(() => {
    const acc = systemState.imuData.acceleration;
    if (acc.x === null || acc.y === null || acc.z === null) return 0;
    return Math.atan2(acc.y, acc.z) * 180 / Math.PI;
  });

  const pitch = computed(() => {
    const acc = systemState.imuData.acceleration;
    if (acc.x === null || acc.y === null || acc.z === null) return 0;
    return Math.atan2(-acc.x, Math.sqrt(acc.y * acc.y + acc.z * acc.z)) * 180 / Math.PI;
  });

  const yaw = computed(() => {
    // Yaw is the same as magnetic heading for 3D attitude display
    // (gyroscope would require integration over time which isn't suitable for computed property)
    return heading.value;
  });

  // Compute heading from magnetometer with tilt compensation
  const heading = computed(() => {
    const mag = systemState.imuData.magnetometer;
    const acc = systemState.imuData.acceleration;

    if (mag.x === null || mag.y === null || mag.z === null ||
        acc.x === null || acc.y === null || acc.z === null) return 0;

    // Tilt compensated compass heading
    const rollRad = roll.value * Math.PI / 180;
    const pitchRad = pitch.value * Math.PI / 180;

    // Tilt compensation
    const magXComp = mag.x * Math.cos(pitchRad) + mag.z * Math.sin(pitchRad);
    const magYComp = mag.x * Math.sin(rollRad) * Math.sin(pitchRad) +
                     mag.y * Math.cos(rollRad) -
                     mag.z * Math.sin(rollRad) * Math.cos(pitchRad);

    let heading = Math.atan2(magYComp, magXComp) * 180 / Math.PI;
    if (heading < 0) heading += 360;

    return heading;
  });

  // Compute tilt for bubble level (using accelerometer only)
  const tiltX = computed(() => {
    // Use roll angle normalized to -1 to 1 range (assuming max ±45° display range)
    return Math.max(-1, Math.min(1, roll.value / 45));
  });

  const tiltY = computed(() => {
    // Use pitch angle normalized to -1 to 1 range (assuming max ±45° display range)
    return Math.max(-1, Math.min(1, pitch.value / 45));
  });

  const tiltAngle = computed(() => {
    // Overall tilt magnitude from level
    return Math.sqrt(roll.value * roll.value + pitch.value * pitch.value);
  });

  const isLevel = computed(() => {
    return tiltAngle.value < 2; // Within 2 degrees is considered level
  });
</script>