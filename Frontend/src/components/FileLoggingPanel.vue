<script setup>
const props = defineProps({
  fileLoggingStatus: {
    type: Object,
    required: true
  }
})

const formatFileSize = (bytes) => {
  if (bytes === null || bytes === undefined) return '—'
  if (bytes === 0) return '0 B'

  const k = 1024
  const sizes = ['B', 'KB', 'MB', 'GB']
  const i = Math.floor(Math.log(bytes) / Math.log(k))

  return parseFloat((bytes / Math.pow(k, i)).toFixed(1)) + ' ' + sizes[i]
}

const formatSpaceUsage = (used, total) => {
  if (!used || !total) return '—'
  const percentage = (used / total * 100).toFixed(1)
  return `${percentage}%`
}

const getStorageColor = (used, total) => {
  if (!used || !total) return 'text-slate-400'
  const percentage = used / total * 100
  if (percentage < 70) return 'text-green-500'
  if (percentage < 90) return 'text-yellow-500'
  return 'text-red-500'
}

const getDriveStatusColor = (available) => {
  return available ? 'text-black' : 'text-red-500'
}

const getFileStatusColor = (isActive) => {
  return isActive ? 'text-black' : 'text-slate-400'
}

const getFileStatusText = (isActive) => {
  return isActive ? 'Writing' : 'Idle'
}

</script>

<template>
  <div class="bg-white rounded-xl border border-slate-200 p-4">
    <div class="flex items-center space-x-2 mb-3">
      <div class="w-6 h-6 bg-purple-500 rounded-lg flex items-center justify-center">
        <svg class="w-4 h-4 text-white" fill="currentColor" viewBox="0 0 24 24">
          <path d="M14,2H6A2,2 0 0,0 4,4V20A2,2 0 0,0 6,22H18A2,2 0 0,0 20,20V8L14,2M18,20H6V4H13V9H18V20Z"/>
        </svg>
      </div>
      <div>
        <h3 class="font-bold text-slate-800">File Logging</h3>
        <div class="text-xs" :class="getDriveStatusColor(fileLoggingStatus.driveAvailable)">
          {{ fileLoggingStatus.driveAvailable ? 'Drive Connected' : 'Drive Disconnected' }}
        </div>
      </div>
    </div>

    <!-- Drive Status -->
    <div class="space-y-2 text-xs">
      <!-- Session and Storage Info -->
      <div class="space-y-1">
        <div class="flex justify-between">
          <span class="text-slate-500">Session:</span>
          <span :class="fileLoggingStatus.currentSession ? 'font-mono text-xs' : 'text-slate-400'">
            {{ fileLoggingStatus.currentSession || '—' }}
          </span>
        </div>
        <div class="flex justify-between">
          <span class="text-slate-500">Storage:</span>
          <span :class="getStorageColor(fileLoggingStatus.usedSpaceBytes, fileLoggingStatus.totalSpaceBytes)">
            {{ formatSpaceUsage(fileLoggingStatus.usedSpaceBytes, fileLoggingStatus.totalSpaceBytes) }} / {{ formatFileSize(fileLoggingStatus.totalSpaceBytes) }}
          </span>
        </div>
      </div>

      <!-- Active Files Section -->
      <div class="mt-3 pt-2 border-t border-slate-100">
        <div class="text-slate-600 font-medium mb-2">Active Files</div>
        <div v-if="fileLoggingStatus.activeFiles && fileLoggingStatus.activeFiles.length > 0" class="space-y-1">
          <div v-for="file in fileLoggingStatus.activeFiles" :key="file.fileName"
               class="flex justify-between items-center">
            <span class="text-slate-600">{{ file.fileName }}</span>
            <span class="text-slate-500 font-mono">{{ formatFileSize(file.fileSizeBytes) }}</span>
          </div>
        </div>
        <div v-else class="text-slate-400 text-center py-2">
          No active files
        </div>
      </div>
    </div>
  </div>
</template>