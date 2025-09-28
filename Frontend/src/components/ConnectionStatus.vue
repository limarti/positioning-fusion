<template>
  <div class="flex items-center space-x-2">
    <div class="relative">
      <div :class="[
        'w-3 h-3 rounded-full transition-all duration-300',
        connectionStatus === 'Connected' ? 'bg-green-500 shadow-lg shadow-green-500/50' :
        connectionStatus === 'Connecting' ? 'bg-yellow-500 shadow-lg shadow-yellow-500/50' :
        connectionStatus === 'Reconnecting' ? 'bg-orange-500 shadow-lg shadow-orange-500/50' :
        'bg-red-500 shadow-lg shadow-red-500/50'
      ]" />
      <div v-if="connectionStatus === 'Connecting' || connectionStatus === 'Reconnecting'"
           class="absolute inset-0 w-3 h-3 rounded-full animate-ping"
           :class="connectionStatus === 'Connecting' ? 'bg-yellow-500' : 'bg-orange-500'" />
    </div>
    <span :class="[
      'text-sm font-medium transition-colors duration-300',
      connectionStatus === 'Connected' ? 'text-green-600' :
      connectionStatus === 'Connecting' ? 'text-yellow-600' :
      connectionStatus === 'Reconnecting' ? 'text-orange-600' :
      'text-red-600'
    ]">
      {{ connectionStatus }}
    </span>
    <div v-if="showRetryInfo && (connectionStatus === 'Reconnecting' || connectionStatus === 'Disconnected')"
         class="text-xs text-slate-500">
      ({{ retryText }})
    </div>
  </div>
</template>

<script setup>
  import { computed } from 'vue';

  const props = defineProps({
    connectionStatus: {
      type: String,
      required: true,
      validator: (value) => ['Connected', 'Connecting', 'Reconnecting', 'Disconnected'].includes(value)
    },
    retryAttempt: {
      type: Number,
      default: 0
    },
    nextRetryIn: {
      type: Number,
      default: 0
    },
    showRetryInfo: {
      type: Boolean,
      default: true
    }
  });

  const retryText = computed(() => 
  {
    if (props.connectionStatus === 'Reconnecting') 
    {
      return `Attempt ${props.retryAttempt}`;
    }
    else if (props.connectionStatus === 'Disconnected' && props.nextRetryIn > 0) 
    {
      return `Retry in ${props.nextRetryIn}s`;
    }
    return '';
  });
</script>