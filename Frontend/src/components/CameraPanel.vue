<template>
  <!-- Camera -->
  <Card title="Camera" 
        :subtitle="status"
        :icon="`<svg fill='currentColor' viewBox='0 0 24 24'><path d='M12,15A2,2 0 0,1 10,13A2,2 0 0,1 12,11A2,2 0 0,1 14,13A2,2 0 0,1 12,15M22,6H19L17.83,4.5C17.42,3.87 16.75,3.5 16,3.5H8C7.25,3.5 6.58,3.87 6.17,4.5L5,6H2A2,2 0 0,0 0,8V18A2,2 0 0,0 2,20H22A2,2 0 0,0 24,18V8A2,2 0 0,0 22,6M12,17A4,4 0 0,0 16,13A4,4 0 0,0 12,9A4,4 0 0,0 8,13A4,4 0 0,0 12,17Z'/></svg>`"
        iconColor="bg-gray-500">
    <div class="space-y-4">
      <!-- Camera Image Display -->
      <div class="relative">
        <div v-if="imageUrl" class="bg-gray-900 rounded-lg overflow-hidden group cursor-pointer" :style="previewStyle" @click="openLargePreview">
          <img :src="imageUrl"
               :alt="`Camera frame ${resolution}`"
               class="w-full h-full object-contain transition-transform group-hover:scale-105"
               @load="() => console.log('Small preview image loaded')"
               @error="() => console.log('Small preview image error')">
          <div class="absolute top-2 left-2 bg-black/50 text-white text-xs px-2 py-1 rounded">
            {{ resolution }}
          </div>
          <div class="absolute top-2 right-2 bg-black/50 text-white text-xs px-2 py-1 rounded">
            {{ imageSize }}
          </div>
          <!-- Expand button overlay - only visible on hover -->
          <div class="absolute inset-0 bg-black/0 group-hover:bg-black/10 transition-all duration-200 flex items-center justify-center pointer-events-none group-hover:pointer-events-auto">
            <div class="bg-black/60 text-white p-3 rounded-full opacity-0 group-hover:opacity-100 transition-opacity duration-200">
              <svg class="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 8V4m0 0h4M4 4l5 5m11-1V4m0 0h-4m4 0l-5 5M4 16v4m0 0h4m-4 0l5-5m11 5l-5-5m5 5v-4m0 4h-4" />
              </svg>
            </div>
          </div>
        </div>
        <div v-else class="bg-gray-800 rounded-lg flex items-center justify-center" :style="previewStyle">
          <div class="text-center text-gray-500">
            <svg class="w-12 h-12 mx-auto mb-2 opacity-50" fill="currentColor" viewBox="0 0 24 24">
              <path d="M12,15A2,2 0 0,1 10,13A2,2 0 0,1 12,11A2,2 0 0,1 14,13A2,2 0 0,1 12,15M22,6H19L17.83,4.5C17.42,3.87 16.75,3.5 16,3.5H8C7.25,3.5 6.58,3.87 6.17,4.5L5,6H2A2,2 0 0,0 0,8V18A2,2 0 0,0 2,20H22A2,2 0 0,0 24,18V8A2,2 0 0,0 22,6M12,17A4,4 0 0,0 16,13A4,4 0 0,0 12,9A4,4 0 0,0 8,13A4,4 0 0,0 12,17Z" />
            </svg>
            <p class="text-sm">
              No camera feed
            </p>
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

  <!-- Large Preview Dialog -->
  <Teleport to="body">
    <div v-if="showLargePreview" 
         class="fixed inset-0 z-50 flex items-center justify-center bg-black/75 backdrop-blur-sm"
         @click="closeLargePreview">
      <div class="relative w-[80vw] h-[80vh] bg-gray-900 rounded-lg overflow-hidden shadow-2xl">
        <!-- Close button -->
        <button class="absolute top-4 right-4 z-10 bg-black/50 hover:bg-black/70 text-white p-2 rounded-full transition-all duration-200"
                @click="closeLargePreview">
          <svg class="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" />
          </svg>
        </button>
        
        <!-- Large image -->
        <div class="w-full h-full flex items-center justify-center p-4">
          <img :src="imageUrl" 
               :alt="`Large camera preview ${resolution}`"
               class="max-w-full max-h-full object-contain"
               @click.stop>
        </div>
        
        <!-- Info overlay -->
        <div class="absolute bottom-4 left-4 bg-black/50 text-white px-4 py-2 rounded-lg">
          <div class="text-sm space-y-1">
            <div><strong>Resolution:</strong> {{ resolution }}</div>
            <div><strong>Size:</strong> {{ imageSize }}</div>
            <div><strong>Format:</strong> {{ cameraData.format }}</div>
            <div class="text-xs text-gray-300 mt-2">
              Click outside or press ESC to close
            </div>
          </div>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<script setup>
  import { ref, computed, onMounted, onUnmounted } from 'vue';
  import Card from './common/Card.vue';

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
  });

  const lastUpdateTime = ref(null);

  // Computed properties
  const imageSize = computed(() => 
  {
    if (cameraData.value.imageSizeBytes === 0) return '—';
    const kb = cameraData.value.imageSizeBytes / 1024;
    if (kb < 1024) 
    {
      return `${kb.toFixed(1)} KB`;
    }
    else 
    {
      return `${(kb / 1024).toFixed(1)} MB`;
    }
  });

  const resolution = computed(() => 
  {
    if (cameraData.value.imageWidth === 0 || cameraData.value.imageHeight === 0) return '—';
    return `${cameraData.value.imageWidth}×${cameraData.value.imageHeight}`;
  });

  const status = computed(() => 
  {
    if (!cameraData.value.isConnected) return 'Disconnected';
    if (!lastUpdateTime.value) return 'Connecting...';
  
    const now = new Date();
    const lastUpdate = new Date(lastUpdateTime.value);
    const secondsAgo = Math.floor((now - lastUpdate) / 1000);
  
    if (secondsAgo < 10) return 'Connected';
    return `Last update ${secondsAgo}s ago`;
  });

  const statusColor = computed(() => 
  {
    if (!cameraData.value.isConnected) return 'text-red-400';
    if (!lastUpdateTime.value) return 'text-yellow-400';
  
    const now = new Date();
    const lastUpdate = new Date(lastUpdateTime.value);
    const secondsAgo = Math.floor((now - lastUpdate) / 1000);
  
    if (secondsAgo < 10) return 'text-green-400';
    return 'text-yellow-400';
  });

  const imageUrl = computed(() => 
  {
    console.log('Camera debug:', {
      isConnected: cameraData.value.isConnected,
      hasImageData: !!cameraData.value.imageBase64,
      imageSize: cameraData.value.imageSizeBytes,
      timestamp: cameraData.value.timestamp
    });

    if (!cameraData.value.imageBase64 || !cameraData.value.isConnected) 
    {
      console.log('No image URL - missing data or disconnected');
      return null;
    }

    const dataUrl = `data:image/jpeg;base64,${cameraData.value.imageBase64}`;
    console.log('Generated image URL, base64 length:', cameraData.value.imageBase64.length);
    return dataUrl;
  });

  const aspectRatio = computed(() => 
  {
    if (cameraData.value.imageWidth > 0 && cameraData.value.imageHeight > 0) 
    {
      return cameraData.value.imageWidth / cameraData.value.imageHeight;
    }
    // Default to 16:9 aspect ratio when no image data is available
    return 16 / 9;
  });

  const previewStyle = computed(() => 
  {
    // Calculate height based on aspect ratio for responsive width
    // Using aspect-ratio CSS property for modern browser support
    return {
      aspectRatio: aspectRatio.value.toString()
    };
  });

  // Large preview dialog
  const showLargePreview = ref(false);

  const openLargePreview = () => 
  {
    if (imageUrl.value) 
    {
      showLargePreview.value = true;
    }
  };

  const closeLargePreview = () => 
  {
    showLargePreview.value = false;
  };

  // Handle escape key to close dialog
  const handleKeyDown = (event) => 
  {
    if (event.key === 'Escape' && showLargePreview.value) 
    {
      closeLargePreview();
    }
  };

  onMounted(() => 
  {
    document.addEventListener('keydown', handleKeyDown);
  });

  onUnmounted(() => 
  {
    document.removeEventListener('keydown', handleKeyDown);
  });

  // SignalR connection will be handled by parent component
  const handleCameraUpdate = (update) => 
  {
    console.log('Camera update received:', {
      isConnected: update.isConnected,
      imageSize: update.imageSizeBytes,
      format: update.format,
      resolution: `${update.imageWidth}x${update.imageHeight}`,
      hasBase64: !!update.imageBase64,
      base64Length: update.imageBase64?.length || 0
    });
  
    cameraData.value = update;
    lastUpdateTime.value = new Date();
  };

  // Expose the handler for parent component
  defineExpose({
    handleCameraUpdate
  });
</script>