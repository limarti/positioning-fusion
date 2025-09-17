<script setup>
import { computed } from 'vue'

const props = defineProps({
  satellites: {
    type: Array,
    required: true
  }
})

const getConstellationColor = (constellation) => {
  switch (constellation) {
    case 'GPS':
      return 'from-blue-400 to-blue-600'
    case 'GLONASS':
      return 'from-red-400 to-red-600'
    case 'Galileo':
      return 'from-purple-400 to-purple-600'
    case 'BeiDou':
      return 'from-yellow-400 to-yellow-600'
    default:
      return 'from-gray-400 to-gray-600'
  }
}

const getConstellationTextColor = (constellation) => {
  switch (constellation) {
    case 'GPS':
      return 'text-blue-600'
    case 'GLONASS':
      return 'text-red-600'
    case 'Galileo':
      return 'text-purple-600'
    case 'BeiDou':
      return 'text-yellow-600'
    default:
      return 'text-gray-600'
  }
}

// Calculate bar width as percentage (max dB-Hz around 50-55)
const getBarWidth = (cn0) => {
  const maxCn0 = 55
  const minCn0 = 10
  return Math.max(5, Math.min(100, ((cn0 - minCn0) / (maxCn0 - minCn0)) * 100))
}

// Sort satellites by signal strength (highest first), then by constellation
const sortedSatellites = computed(() => {
  return [...props.satellites].sort((a, b) => {
    // Primary sort: signal strength (descending)
    if (a.cn0 !== b.cn0) {
      return b.cn0 - a.cn0
    }
    // Secondary sort: constellation order
    if (a.constellation !== b.constellation) {
      const order = ['GPS', 'GLONASS', 'Galileo', 'BeiDou']
      return order.indexOf(a.constellation) - order.indexOf(b.constellation)
    }
    // Tertiary sort: satellite ID
    return a.svid - b.svid
  })
})
</script>

<template>
  <div class="w-full">
    <!-- Satellite List -->
    <div class="bg-slate-50 rounded-xl overflow-hidden">
      <div class="max-h-48 overflow-y-auto">
        <TransitionGroup name="satellite-list" tag="div" class="space-y-0 p-0.5">
          <div
            v-for="sat in sortedSatellites"
            :key="`${sat.constellation}-${sat.svid}`"
            class="flex items-center space-x-1.5 px-1.5 py-0.5 bg-white rounded hover:bg-slate-100 transition-colors"
          >
            <!-- Satellite Info -->
            <div class="min-w-0 w-12">
              <span class="font-mono font-semibold text-sm text-slate-800">{{ sat.svid }}</span>
            </div>

            <!-- Signal Strength Bar -->
            <div class="flex-1 relative">
              <div class="h-4 bg-slate-200 rounded-full overflow-hidden">
                <div
                  class="h-full bg-gradient-to-r rounded-full transition-all duration-300"
                  :class="[
                    getConstellationColor(sat.constellation),
                    sat.used ? 'opacity-100' : 'opacity-60'
                  ]"
                  :style="{ width: getBarWidth(sat.cn0) + '%' }"
                ></div>
              </div>

              <!-- Signal Value Overlay -->
              <div class="absolute inset-0 flex items-center justify-center">
                <span
                  class="font-mono font-semibold text-xs text-white drop-shadow-sm"
                  style="text-shadow: 1px 1px 2px rgba(0,0,0,0.7)"
                >
                  {{ sat.cn0 }}
                </span>
              </div>

              <!-- Usage indicator -->
              <div
                v-if="sat.used"
                class="absolute -top-0.5 -right-0.5 w-2 h-2 bg-emerald-400 border border-white rounded-full animate-pulse"
              ></div>
            </div>

            <!-- Position Info -->
            <div class="text-right min-w-0 w-16 text-xs text-slate-600 font-mono">
              <span>{{ sat.elevation }}°/{{ sat.azimuth }}°</span>
            </div>
          </div>
        </TransitionGroup>
      </div>
    </div>
  </div>
</template>

<style scoped>
.satellite-list-move,
.satellite-list-enter-active,
.satellite-list-leave-active {
  transition: all 0.5s ease;
}

.satellite-list-enter-from,
.satellite-list-leave-to {
  opacity: 0;
  transform: translateX(30px);
}

.satellite-list-leave-active {
  position: absolute;
}
</style>