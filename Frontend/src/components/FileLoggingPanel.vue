<template>
  <Card title="File Logging"
        :icon="`<svg fill='currentColor' viewBox='0 0 24 24'><path d='M14,2H6A2,2 0 0,0 4,4V20A2,2 0 0,0 6,22H18A2,2 0 0,0 20,20V8L14,2M18,20H6V4H13V9H18V20Z'/></svg>`"
        iconColor="bg-gray-500">

    <!-- Drive Warning -->
    <div v-if="!systemState.fileLoggingStatus.driveAvailable" class="bg-red-50 border border-red-200 rounded-lg p-3 mb-3">
      <div class="flex items-center space-x-2">
        <svg class="w-4 h-4 text-red-500" fill="currentColor" viewBox="0 0 20 20">
          <path fill-rule="evenodd" d="M8.257 3.099c.765-1.36 2.722-1.36 3.486 0l5.58 9.92c.75 1.334-.213 2.98-1.742 2.98H4.42c-1.53 0-2.493-1.646-1.743-2.98l5.58-9.92zM11 13a1 1 0 11-2 0 1 1 0 012 0zm-1-8a1 1 0 00-1 1v3a1 1 0 002 0V6a1 1 0 00-1-1z" clip-rule="evenodd" />
        </svg>
        <span class="text-red-700 text-sm font-medium">No USB drive detected</span>
      </div>
    </div>

    <!-- Drive Status -->
    <div class="space-y-2 text-sm">
      <!-- Session and Storage Info -->
      <div class="space-y-1">
        <div class="flex justify-between">
          <span class="text-slate-500">Session:</span>
          <span :class="systemState.fileLoggingStatus.currentSession ? 'font-mono text-sm' : 'text-slate-400'">
            {{ systemState.fileLoggingStatus.currentSession || '—' }}
          </span>
        </div>
        <div class="flex justify-between">
          <span class="text-slate-500">Storage:</span>
          <span :class="getStorageColor(systemState.fileLoggingStatus.usedSpaceBytes, systemState.fileLoggingStatus.totalSpaceBytes)">
            {{ formatSpaceUsage(systemState.fileLoggingStatus.usedSpaceBytes, systemState.fileLoggingStatus.totalSpaceBytes) }} / {{ formatFileSize(systemState.fileLoggingStatus.totalSpaceBytes) }}
          </span>
        </div>
      </div>

      <!-- Active Files Section -->
      <div class="mt-3 pt-2 border-t border-slate-100">
        <div class="text-slate-600 font-medium mb-2 text-sm">
          Active Files
        </div>
        <div v-if="systemState.fileLoggingStatus.activeFiles && systemState.fileLoggingStatus.activeFiles.length > 0" class="space-y-1">
          <div v-for="file in systemState.fileLoggingStatus.activeFiles"
               :key="file.fileName"
               class="flex justify-between items-center">
            <span class="text-slate-600 text-sm">{{ file.fileName }}</span>
            <span class="text-slate-500 font-mono text-sm">{{ formatFileSize(file.fileSizeBytes) }}</span>
          </div>
        </div>
        <div v-else class="text-slate-400 text-center py-2 text-sm">
          No active files
        </div>
      </div>
    </div>
  </Card>
</template>

<script setup>
  import Card from './common/Card.vue';
  import { useSystemData } from '@/composables/useSystemData';

  // Get data from composable
  const { state: systemState } = useSystemData();

  const formatFileSize = (bytes) => 
  {
    if (bytes === null || bytes === undefined) return '—';
    if (bytes === 0) return '0 B';

    const k = 1024;
    const sizes = ['B', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));

    return parseFloat((bytes / Math.pow(k, i)).toFixed(1)) + ' ' + sizes[i];
  };

  const formatSpaceUsage = (used, total) => 
  {
    if (!used || !total) return '—';
    const percentage = (used / total * 100).toFixed(1);
    return `${percentage}%`;
  };

  const getStorageColor = (used, total) => 
  {
    if (!used || !total) return 'text-slate-400';
    const percentage = used / total * 100;
    if (percentage < 70) return 'text-green-500';
    if (percentage < 90) return 'text-yellow-500';
    return 'text-red-500';
  };

  const getDriveStatusColor = (available) => 
  {
    return available ? 'text-black' : 'text-red-500';
  };

  const getFileStatusColor = (isActive) => 
  {
    return isActive ? 'text-black' : 'text-slate-400';
  };

  const getFileStatusText = (isActive) => 
  {
    return isActive ? 'Writing' : 'Idle';
  };

</script>