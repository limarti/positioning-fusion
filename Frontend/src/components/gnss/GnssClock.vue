<template>
  <Card title="GNSS Clock">
    <div class="space-y-4">
      <!-- Main Clock Display -->
      <div class="text-center">
        <div class="text-2xl font-mono font-bold text-gray-800 mb-1">
          {{ formattedTime }}
        </div>
        <div class="text-sm text-gray-500">
          {{ formattedDate }}
        </div>
      </div>

      <!-- Time Status Indicators -->
      <div class="flex items-center justify-between pt-3 border-t border-gray-200">
        <div class="flex items-center space-x-3">
          <div>
            <div class="text-xs text-gray-500 mb-1">
              Time Valid
            </div>
            <div class="flex items-center space-x-1">
              <div class="w-2 h-2 rounded-full"
                   :class="timeValidStatus.color" />
              <span class="text-sm font-medium" :class="timeValidStatus.textColor">
                {{ timeValidStatus.label }}
              </span>
            </div>
          </div>
        </div>
      </div>
    </div>
  </Card>
</template>

<script setup>
  import { computed, ref, onMounted, onUnmounted } from 'vue';
  import Card from '../common/Card.vue';
  import { useGnssData } from '@/composables/useGnssData';

  // Get data from composable
  const { state: gnssState } = useGnssData();

  // Local time ref for real-time updates
  const currentTime = ref(Date.now());
  let timeInterval = null;

  // Update current time every second
  onMounted(() => 
  {
    timeInterval = setInterval(() => 
    {
      currentTime.value = Date.now();
    }, 1000);
  });

  onUnmounted(() => 
  {
    if (timeInterval) 
    {
      clearInterval(timeInterval);
    }
  });

  // Computed properties for display
  const gnssTime = computed(() => 
  {
    if (!gnssState.gnssData.gnssTimestamp || gnssState.gnssData.gnssTimestamp === 0) 
    {
      return null;
    }
    return new Date(gnssState.gnssData.gnssTimestamp);
  });

  const formattedTime = computed(() => 
  {
    if (!gnssTime.value) 
    {
      return '--:--:--';
    }

    return gnssTime.value.toLocaleTimeString('en-US', {
      timeZone: 'UTC',
      hour12: false,
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit'
    });
  });

  const formattedDate = computed(() => 
  {
    if (!gnssTime.value) 
    {
      return 'No GNSS time available';
    }

    return gnssTime.value.toLocaleDateString('en-US', {
      timeZone: 'UTC',
      weekday: 'short',
      year: 'numeric',
      month: 'short',
      day: 'numeric'
    }) + ' UTC';
  });

  const timeValidStatus = computed(() => 
  {
    const timeValid = gnssState.gnssData.timeValid;

    if (timeValid === null || timeValid === undefined) 
    {
      return {
        label: 'Unknown',
        color: 'bg-gray-400',
        textColor: 'text-gray-600'
      };
    }

    // Check time validity bits (based on UBlox NAV-PVT documentation)
    const dateValid = (timeValid & 0x01) !== 0;  // validDate bit
    const timeOfWeekValid = (timeValid & 0x02) !== 0;  // validTime bit
    const utcTimeValid = (timeValid & 0x04) !== 0;  // fullyResolved bit

    if (utcTimeValid && dateValid && timeOfWeekValid) 
    {
      return {
        label: 'Valid',
        color: 'bg-emerald-500',
        textColor: 'text-emerald-700'
      };
    }
    else if (dateValid && timeOfWeekValid) 
    {
      return {
        label: 'Partial',
        color: 'bg-amber-500',
        textColor: 'text-amber-700'
      };
    }
    else 
    {
      return {
        label: 'Invalid',
        color: 'bg-red-500',
        textColor: 'text-red-700'
      };
    }
  });
</script>