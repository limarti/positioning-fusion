<template>
  <Card title="Message Rates">

    <div v-if="sortedMessageTypes.length === 0" class="text-center py-8 text-slate-500">
      No messages received
    </div>

    <div v-else class="space-y-1">
      <div v-for="message in sortedMessageTypes" 
           :key="message.type"
           class="flex items-center justify-between px-2 py-1 rounded"
           :class="getMessageColor(message.type)">
        <div class="flex items-center space-x-3">
          <div class="text-sm font-mono font-semibold">
            {{ message.type }}
          </div>
        </div>
        <div class="flex items-center space-x-2">
          <div class="text-sm font-bold" :class="getRateColor(message.rate)">
            {{ message.rate.toFixed(1) }}
          </div>
          <div class="text-xs text-slate-500">
            Hz
          </div>
        </div>
      </div>
    </div>
  </Card>
</template>

<script setup>
  import { computed } from 'vue';
  import Card from './common/Card.vue';
  import { useGnssData } from '@/composables/useGnssData';

  // Get data from composable
  const { state: gnssState } = useGnssData();

  // Sort message types for consistent display
  const sortedMessageTypes = computed(() => 
  {
    if (!gnssState.messageRates.messageRates) return [];
  
    return Object.entries(gnssState.messageRates.messageRates)
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([type, rate]) => ({
        type,
        rate: parseFloat(rate)
      }));
  });

  // Get color based on message type
  const getMessageColor = (messageType) => 
  {
    // NMEA messages
    if (messageType.startsWith('NMEA.')) return 'text-gray-800 bg-gray-100';

    // UBX messages
    if (messageType.includes('NAV')) return 'text-gray-800 bg-gray-50';
    if (messageType.includes('RXM')) return 'text-gray-800 bg-slate-100';
    if (messageType.includes('TIM')) return 'text-gray-800 bg-slate-50';
    if (messageType.includes('MON')) return 'text-gray-800 bg-gray-200';
    return 'text-gray-700 bg-gray-50';
  };

  // Get rate status color
  const getRateColor = (rate) => 
  {
    if (rate >= 9.0) return 'text-gray-800';
    if (rate >= 5.0) return 'text-gray-700';
    if (rate > 0) return 'text-gray-600';
    return 'text-slate-400';
  };
</script>