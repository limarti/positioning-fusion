<script setup>
import { ref, computed, onMounted, onUnmounted } from 'vue'
import Card from './common/Card.vue'

// Camera data
const cameraData = ref({
  timestamp: null,
  imageBase64: '',
  imageSizeBytes: 0,
  imageWidth: 0,
  imageHeight: 0,
  format: 'JPEG',
  captureTimeMs: 0,
  encodingTimeMs: 0,
  isConnected: false
})

const lastUpdateTime = ref(null)

// Computed properties
const imageSize = computed(() => {
  if (cameraData.value.imageSizeBytes === 0) return '—'
  const kb = cameraData.value.imageSizeBytes / 1024
  if (kb < 1024) {
    return `${kb.toFixed(1)} KB`
  } else {
    return `${(kb / 1024).toFixed(1)} MB`
  }
})

const resolution = computed(() => {
  if (cameraData.value.imageWidth === 0 || cameraData.value.imageHeight === 0) return '—'
  return `${cameraData.value.imageWidth}×${cameraData.value.imageHeight}`
})

const status = computed(() => {
  if (!cameraData.value.isConnected) return 'Disconnected'
  if (!lastUpdateTime.value) return 'Connecting...'
  
  const now = new Date()
  const lastUpdate = new Date(lastUpdateTime.value)
  const secondsAgo = Math.floor((now - lastUpdate) / 1000)
  
  if (secondsAgo < 10) return 'Connected'
  return `Last update ${secondsAgo}s ago`
})

const statusColor = computed(() => {
  if (!cameraData.value.isConnected) return 'text-red-400'
  if (!lastUpdateTime.value) return 'text-yellow-400'
  
  const now = new Date()
  const lastUpdate = new Date(lastUpdateTime.value)
  const secondsAgo = Math.floor((now - lastUpdate) / 1000)
  
  if (secondsAgo < 10) return 'text-green-400'
  return 'text-yellow-400'
})

const imageUrl = computed(() => {
  if (!cameraData.value.imageBase64 || !cameraData.value.isConnected) return null
  return `data:image/jpeg;base64,${cameraData.value.imageBase64}`
})

// SignalR connection will be handled by parent component
const handleCameraUpdate = (update) => {
  cameraData.value = update
  lastUpdateTime.value = new Date()
}

// Expose the handler for parent component
defineExpose({
  handleCameraUpdate
})
</script>

<template>
  <!-- Camera -->
  <Card 
    title="Camera" 
    :subtitle="status"
    :icon="`<svg fill='currentColor' viewBox='0 0 24 24'><path d='M12,15A2,2 0 0,1 10,13A2,2 0 0,1 12,11A2,2 0 0,1 14,13A2,2 0 0,1 12,15M22,6H19L17.83,4.5C17.42,3.87 16.75,3.5 16,3.5H8C7.25,3.5 6.58,3.87 6.17,4.5L5,6H2A2,2 0 0,0 0,8V18A2,2 0 0,0 2,20H22A2,2 0 0,0 24,18V8A2,2 0 0,0 22,6M12,17A4,4 0 0,0 16,13A4,4 0 0,0 12,9A4,4 0 0,0 8,13A4,4 0 0,0 12,17Z'/></svg>`"
    icon-color="bg-cyan-500"
  >
    <div class="space-y-4">
      <!-- Camera Image Display -->
      <div class="relative">
        <div v-if="imageUrl" class="aspect-video bg-gray-900 rounded-lg overflow-hidden">
          <img 
            :src="imageUrl" 
            :alt="`Camera frame ${resolution}`"
            class="w-full h-full object-contain"
          />
          <div class="absolute top-2 left-2 bg-black bg-opacity-50 text-white text-xs px-2 py-1 rounded">
            {{ resolution }}
          </div>
          <div class="absolute top-2 right-2 bg-black bg-opacity-50 text-white text-xs px-2 py-1 rounded">
            {{ imageSize }}
          </div>
        </div>
        <div v-else class="aspect-video bg-gray-800 rounded-lg flex items-center justify-center">
          <div class="text-center text-gray-500">
            <svg class="w-12 h-12 mx-auto mb-2 opacity-50" fill="currentColor" viewBox="0 0 24 24">
              <path d="M12,15A2,2 0 0,1 10,13A2,2 0 0,1 12,11A2,2 0 0,1 14,13A2,2 0 0,1 12,15M22,6H19L17.83,4.5C17.42,3.87 16.75,3.5 16,3.5H8C7.25,3.5 6.58,3.87 6.17,4.5L5,6H2A2,2 0 0,0 0,8V18A2,2 0 0,0 2,20H22A2,2 0 0,0 24,18V8A2,2 0 0,0 22,6M12,17A4,4 0 0,0 16,13A4,4 0 0,0 12,9A4,4 0 0,0 8,13A4,4 0 0,0 12,17Z"/>
            </svg>
            <p class="text-sm">No camera feed</p>
          </div>
        </div>
      </div>

      <!-- Camera Stats -->
      <div class="space-y-2 text-sm">
        <div class="flex justify-between">
          <span class="text-slate-500">Status:</span>
          <span :class="statusColor">{{ status }}</span>
        </div>
        <div class="flex justify-between">
          <span class="text-slate-500">Resolution:</span>
          <span class="text-slate-400">{{ resolution }}</span>
        </div>
        <div class="flex justify-between">
          <span class="text-slate-500">Image Size:</span>
          <span class="text-slate-400">{{ imageSize }}</span>
        </div>
        <div class="flex justify-between">
          <span class="text-slate-500">Format:</span>
          <span class="text-slate-400">{{ cameraData.format || '—' }}</span>
        </div>
        <div v-if="cameraData.isConnected && cameraData.captureTimeMs > 0" class="flex justify-between">
          <span class="text-slate-500">Capture:</span>
          <span class="text-slate-400">{{ cameraData.captureTimeMs.toFixed(1) }}ms</span>
        </div>
        <div v-if="cameraData.isConnected && cameraData.encodingTimeMs > 0" class="flex justify-between">
          <span class="text-slate-500">Encoding:</span>
          <span class="text-slate-400">{{ cameraData.encodingTimeMs.toFixed(1) }}ms</span>
        </div>
      </div>
    </div>
  </Card>
</template>