<script setup>
import { ref, reactive, watch, onMounted, onUnmounted } from 'vue'
import * as Plot from '@observablehq/plot'
import * as d3 from 'd3'

const props = defineProps({
  gnssData: {
    type: Object,
    required: true
  }
})

// Component state
const plotContainer = ref(null)
const maxPoints = ref(200)
const positionHistory = reactive([])
const referencePoint = ref(null)
const autoRangeEnabled = ref(true)
const zoomLevel = ref(1)
const spanMeters = ref(0)

// Current plot instance
let currentPlot = null

// Convert degrees to meters using simple approximation
const convertToMeters = (lat, lng, refLat, refLng) => {
  if (!refLat || !refLng) return { x: 0, y: 0 }
  
  const deltaLat = lat - refLat
  const deltaLng = lng - refLng
  
  const metersPerDegreeLat = 110540
  const metersPerDegreeLng = 111320 * Math.cos(refLat * Math.PI / 180)
  
  return {
    x: deltaLng * metersPerDegreeLng,
    y: deltaLat * metersPerDegreeLat
  }
}

// Get color based on fix type
const getPointColor = (fixType) => {
  if (fixType === 'RTK Fixed') return '#10b981'
  if (fixType === 'RTK Float') return '#f59e0b'
  if (fixType === 'GNSS Fixed') return '#3b82f6'
  return '#6b7280'
}

// Format span value for display
const formatSpan = (meters) => {
  if (meters < 0.001) return '0m'
  if (meters < 1) return (meters * 1000).toFixed(0) + 'mm'
  if (meters < 10) return meters.toFixed(2) + 'm'
  if (meters < 100) return meters.toFixed(1) + 'm'
  if (meters < 1000) return meters.toFixed(0) + 'm'
  return (meters / 1000).toFixed(1) + 'km'
}

// Create/update the plot
const updatePlot = () => {
  if (!plotContainer.value || positionHistory.length === 0) return
  
  // Remove existing plot
  if (currentPlot) {
    currentPlot.remove()
  }
  
  // Calculate base range from data
  const xExtent = d3.extent(positionHistory, d => d.x)
  const yExtent = d3.extent(positionHistory, d => d.y)
  
  // Add 10% padding
  const xPadding = Math.max(0.1, (xExtent[1] - xExtent[0]) * 0.1)
  const yPadding = Math.max(0.1, (yExtent[1] - yExtent[0]) * 0.1)
  
  let baseXDomain = [xExtent[0] - xPadding, xExtent[1] + xPadding]
  let baseYDomain = [yExtent[0] - yPadding, yExtent[1] + yPadding]
  
  // Ensure equal aspect ratio
  const xRange = baseXDomain[1] - baseXDomain[0]
  const yRange = baseYDomain[1] - baseYDomain[0]
  const maxRange = Math.max(xRange, yRange)
  
  const xCenter = (baseXDomain[0] + baseXDomain[1]) / 2
  const yCenter = (baseYDomain[0] + baseYDomain[1]) / 2
  
  baseXDomain = [xCenter - maxRange/2, xCenter + maxRange/2]
  baseYDomain = [yCenter - maxRange/2, yCenter + maxRange/2]
  
  // Apply zoom level
  let xDomain, yDomain
  if (autoRangeEnabled.value) {
    // Use base domain when auto-range is enabled
    xDomain = baseXDomain
    yDomain = baseYDomain
    // Update span only when auto-range is enabled
    spanMeters.value = maxRange
  } else {
    // Apply zoom level to domain using the frozen span value
    const zoomedRange = spanMeters.value
    xDomain = [xCenter - zoomedRange/2, xCenter + zoomedRange/2]
    yDomain = [yCenter - zoomedRange/2, yCenter + zoomedRange/2]
  }
  
  // Ensure both axes have exactly the same scale by using the same domain range
  const domainRange = Math.max(
    Math.abs(xDomain[1] - xDomain[0]),
    Math.abs(yDomain[1] - yDomain[0])
  )
  
  const finalXCenter = (xDomain[0] + xDomain[1]) / 2
  const finalYCenter = (yDomain[0] + yDomain[1]) / 2
  
  // Force both axes to use the same range
  const finalXDomain = [finalXCenter - domainRange/2, finalXCenter + domainRange/2]
  const finalYDomain = [finalYCenter - domainRange/2, finalYCenter + domainRange/2]

  // Create new plot
  currentPlot = Plot.plot({
    width: plotContainer.value.clientWidth,
    height: plotContainer.value.clientHeight,
    marginTop: 40,
    marginRight: 40,
    marginBottom: 50,
    marginLeft: 60,
    grid: true,
    aspectRatio: 1, // Force 1:1 aspect ratio
    x: {
      label: "East (meters)",
      domain: finalXDomain
    },
    y: {
      label: "North (meters)",
      domain: finalYDomain
    },
    marks: [
      Plot.dot(positionHistory, {
        x: "x",
        y: "y",
        fill: "color",
        stroke: "#ffffff",
        strokeWidth: 1,
        r: 4,
        title: d => `${d.fixType}\nEast: ${d.x.toFixed(2)}m\nNorth: ${d.y.toFixed(2)}m`
      }),
      Plot.crosshair(positionHistory, {x: "x", y: "y"})
    ]
  })
  
  plotContainer.value.appendChild(currentPlot)
}

// Watch for new GNSS position data
watch(() => [props.gnssData.latitude, props.gnssData.longitude, props.gnssData.fixType],
  ([lat, lng, fixType]) => {
    if (lat !== null && lng !== null) {
      if (!referencePoint.value) {
        referencePoint.value = { lat, lng }
      }

      const meters = convertToMeters(lat, lng, referencePoint.value.lat, referencePoint.value.lng)

      const newPoint = {
        x: meters.x,
        y: meters.y,
        lat: lat,
        lng: lng,
        fixType: fixType || 'Unknown',
        timestamp: new Date(),
        color: getPointColor(fixType)
      }

      positionHistory.push(newPoint)

      if (positionHistory.length > maxPoints.value) {
        positionHistory.splice(0, positionHistory.length - maxPoints.value)
      }

      updatePlot()
    }
  },
  { immediate: true }
)

// Control functions
const zoomIn = () => {
  zoomLevel.value = Math.min(zoomLevel.value * 1.5, 20)
  autoRangeEnabled.value = false
  // Update span for manual zoom
  spanMeters.value = spanMeters.value / 1.5
  updatePlot()
}

const zoomOut = () => {
  zoomLevel.value = Math.max(zoomLevel.value / 1.5, 0.1)
  autoRangeEnabled.value = false
  // Update span for manual zoom
  spanMeters.value = spanMeters.value * 1.5
  updatePlot()
}

const reset = () => {
  zoomLevel.value = 1
  autoRangeEnabled.value = true
  // Span will be updated in updatePlot() since auto-range is enabled
  updatePlot()
}

const clearHistory = () => {
  positionHistory.splice(0)
  referencePoint.value = null
  if (currentPlot) {
    currentPlot.remove()
    currentPlot = null
  }
}

const updateMaxPoints = (event) => {
  const newMax = parseInt(event.target.value)
  maxPoints.value = newMax
  
  if (positionHistory.length > newMax) {
    positionHistory.splice(0, positionHistory.length - newMax)
    updatePlot()
  }
}

// Handle auto-range toggle
watch(autoRangeEnabled, (enabled) => {
  if (enabled) {
    updatePlot()
  }
})

// Lifecycle
onMounted(() => {
  // Handle window resize
  const handleResize = () => {
    if (plotContainer.value && currentPlot) {
      updatePlot()
    }
  }
  
  window.addEventListener('resize', handleResize)
  
  // Cleanup function
  onUnmounted(() => {
    window.removeEventListener('resize', handleResize)
    if (currentPlot) {
      currentPlot.remove()
    }
  })
})
</script>

<template>
  <div class="bg-white rounded-xl border border-slate-200 p-4 break-inside-avoid mb-6">
    <!-- Header -->
    <div class="flex items-center justify-between mb-4">
      <div class="flex items-center space-x-3">
        <svg class="w-6 h-6 text-purple-600" fill="currentColor" viewBox="0 0 24 24">
          <path d="M12,2A2,2 0 0,1 14,4C14,4.74 13.6,5.39 13,5.73V7A1,1 0 0,1 12,8A1,1 0 0,1 11,7V5.73C10.4,5.39 10,4.74 10,4A2,2 0 0,1 12,2M21,9V7H15V9H21M21,11H15V13H21V11M21,15H15V17H21V15M12,10A3,3 0 0,1 15,13A3,3 0 0,1 12,16A3,3 0 0,1 9,13A3,3 0 0,1 12,10M12,11A2,2 0 0,0 10,13A2,2 0 0,0 12,15A2,2 0 0,0 14,13A2,2 0 0,0 12,11M3,9V7H9V9H3M3,11H9V13H3V11M3,15H9V17H3V15Z"/>
        </svg>
        <h2 class="text-lg font-bold text-slate-800">Position Plot</h2>
        <div class="text-sm text-slate-600">{{ positionHistory.length }} points</div>
      </div>
    </div>

    <!-- Controls -->
    <div class="flex flex-wrap items-center gap-3 mb-4 p-3 bg-slate-50 rounded-lg">
      <div class="flex items-center space-x-2">
        <label class="text-sm font-medium text-slate-700">Points:</label>
        <select 
          :value="maxPoints" 
          @change="updateMaxPoints"
          class="px-2 py-1 text-sm border border-slate-300 rounded focus:outline-none focus:ring-2 focus:ring-purple-500"
        >
          <option value="50">50</option>
          <option value="100">100</option>
          <option value="200">200</option>
          <option value="500">500</option>
          <option value="1000">1000</option>
        </select>
      </div>
      
      <div class="flex items-center space-x-2">
        <label class="text-sm font-medium text-slate-700">Auto Range:</label>
        <input 
          type="checkbox" 
          v-model="autoRangeEnabled"
          class="w-4 h-4 text-blue-600 bg-gray-100 border-gray-300 rounded focus:ring-blue-500 focus:ring-2"
        >
      </div>
      
      <div class="flex items-center space-x-1">
        <button 
          @click="zoomOut"
          class="w-8 h-8 flex items-center justify-center text-sm bg-blue-500 text-white rounded hover:bg-blue-600 focus:outline-none focus:ring-2 focus:ring-blue-500"
          :disabled="positionHistory.length === 0 || zoomLevel <= 0.1"
          title="Zoom Out"
        >
          −
        </button>
        <span class="text-xs text-slate-600 min-w-16 text-center">{{ formatSpan(spanMeters) }}</span>
        <button 
          @click="zoomIn"
          class="w-8 h-8 flex items-center justify-center text-sm bg-blue-500 text-white rounded hover:bg-blue-600 focus:outline-none focus:ring-2 focus:ring-blue-500"
          :disabled="positionHistory.length === 0 || zoomLevel >= 20"
          title="Zoom In"
        >
          +
        </button>
      </div>
      
      <button 
        @click="reset"
        class="px-3 py-1 text-sm bg-gray-500 text-white rounded hover:bg-gray-600 focus:outline-none focus:ring-2 focus:ring-gray-500"
        :disabled="positionHistory.length === 0"
      >
        Reset
      </button>
      
      <button 
        @click="clearHistory"
        class="px-3 py-1 text-sm bg-red-500 text-white rounded hover:bg-red-600 focus:outline-none focus:ring-2 focus:ring-red-500"
        :disabled="positionHistory.length === 0"
      >
        Clear
      </button>
      
      
      <div v-if="referencePoint" class="text-xs text-slate-500 ml-auto">
        Origin: {{ referencePoint.lat.toFixed(6) }}°, {{ referencePoint.lng.toFixed(6) }}°
      </div>
    </div>

    <!-- Plot Container -->
    <div class="w-full relative" style="aspect-ratio: 1; max-height: 500px;">
      <div ref="plotContainer" class="w-full h-full"></div>
      <div v-if="positionHistory.length === 0" class="absolute inset-0 flex items-center justify-center text-slate-500 bg-slate-50 rounded-lg border-2 border-dashed border-slate-300">
        <div class="text-center">
          <svg class="w-12 h-12 mx-auto mb-2 text-slate-400" fill="currentColor" viewBox="0 0 24 24">
            <path d="M12,2A2,2 0 0,1 14,4C14,4.74 13.6,5.39 13,5.73V7A1,1 0 0,1 12,8A1,1 0 0,1 11,7V5.73C10.4,5.39 10,4.74 10,4A2,2 0 0,1 12,2M21,9V7H15V9H21M21,11H15V13H21V11M21,15H15V17H21V15M12,10A3,3 0 0,1 15,13A3,3 0 0,1 12,16A3,3 0 0,1 9,13A3,3 0 0,1 12,10M12,11A2,2 0 0,0 10,13A2,2 0 0,0 12,15A2,2 0 0,0 14,13A2,2 0 0,0 12,11M3,9V7H9V9H3M3,11H9V13H3V11M3,15H9V17H3V15Z"/>
          </svg>
          <p class="text-sm">Waiting for GNSS position data...</p>
        </div>
      </div>
    </div>

    <!-- Legend -->
    <div class="flex flex-wrap items-center gap-4 mt-3 text-xs">
      <div class="flex items-center space-x-1">
        <div class="w-3 h-3 bg-emerald-500 rounded-full"></div>
        <span>RTK Fixed</span>
      </div>
      <div class="flex items-center space-x-1">
        <div class="w-3 h-3 bg-amber-500 rounded-full"></div>
        <span>RTK Float</span>
      </div>
      <div class="flex items-center space-x-1">
        <div class="w-3 h-3 bg-blue-500 rounded-full"></div>
        <span>GNSS Fixed</span>
      </div>
      <div class="flex items-center space-x-1">
        <div class="w-3 h-3 bg-gray-500 rounded-full"></div>
        <span>Other</span>
      </div>
      <div class="text-slate-500 ml-auto">
        Click +/- to zoom
      </div>
    </div>
  </div>
</template>