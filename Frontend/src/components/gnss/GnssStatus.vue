<template>
  <Card title="GNSS Status">
    <div class="space-y-6">
      <!-- Position and Fix Status -->
      <div class="flex items-center justify-between">
        <div class="flex items-center space-x-4">
          <div>
            <div class="text-sm text-gray-600">
              Current Position
            </div>
            <div class="text-base font-mono text-gray-800">
              {{ gnssState.gnssData.latitude !== null && gnssState.gnssData.longitude !== null
                ? `${gnssState.gnssData.latitude.toFixed(9)}°, ${gnssState.gnssData.longitude.toFixed(9)}°`
                : 'Waiting for GNSS fix...' }}
            </div>
            <div v-if="gnssState.gnssData.altitude !== null" class="text-sm text-gray-600 font-mono mt-1">
              Altitude: {{ gnssState.gnssData.altitude.toFixed(2) }}m
            </div>
            <!-- GNSS Clock - compact inline -->
            <div class="text-xs text-gray-500 font-mono mt-2 flex items-center space-x-2">
              <span>{{ formattedTime }} UTC</span>
              <div class="flex items-center space-x-1">
                <div class="w-1.5 h-1.5 rounded-full" :class="timeValidStatus.color" />
                <span :class="timeValidStatus.textColor">{{ timeValidStatus.label }}</span>
              </div>
            </div>
          </div>
        </div>
        <div class="text-right">
          <div class="text-sm text-gray-600">
            Fix Type
          </div>
          <div class="text-base font-bold text-gray-800">
            {{ gnssState.gnssData.fixType || 'No Fix' }}
          </div>
          <!-- Correction Status Indicator -->
          <div v-if="gnssState.gnssData.corrections.status.source !== 'None'" class="mt-2">
            <div class="text-xs text-gray-500 mb-1">
              Corrections
            </div>
            <div class="flex items-center justify-end space-x-1">
              <span class="text-xs font-medium px-2 py-1 rounded" :class="getCorrectionStatusClass()">
                {{ gnssState.gnssData.corrections.status.source }}
              </span>
              <span class="text-xs text-gray-500" :class="getCorrectionAgeClass()">
                {{ formatCorrectionAge() }}
              </span>
            </div>
          </div>
        </div>
      </div>

      <!-- Core Health Summary -->
      <div class="border-t border-gray-200 pt-4">
        <div class="grid grid-cols-2 md:grid-cols-6 gap-4 text-center text-sm">
          <div>
            <div class="text-sm text-gray-500 mb-1">
              hAcc
            </div>
            <div class="text-base font-bold" :class="gnssState.gnssData.hAcc !== null ? 'text-gray-800' : 'text-slate-400'">
              {{ formatAccuracy(gnssState.gnssData.hAcc) }}
            </div>
          </div>
          <div>
            <div class="text-sm text-gray-500 mb-1">
              vAcc
            </div>
            <div class="text-base font-bold" :class="gnssState.gnssData.vAcc !== null ? 'text-gray-800' : 'text-slate-400'">
              {{ formatAccuracy(gnssState.gnssData.vAcc) }}
            </div>
          </div>
          <div>
            <div class="text-sm text-gray-500 mb-1">
              HDOP
            </div>
            <div class="text-base font-bold" :class="gnssState.gnssData.hdop !== null ? 'text-gray-700' : 'text-slate-400'">
              {{ gnssState.gnssData.hdop !== null ? gnssState.gnssData.hdop.toFixed(2) : '—' }}
            </div>
          </div>
          <div>
            <div class="text-sm text-gray-500 mb-1">
              VDOP
            </div>
            <div class="text-base font-bold" :class="gnssState.gnssData.vdop !== null ? 'text-gray-700' : 'text-slate-400'">
              {{ gnssState.gnssData.vdop !== null ? gnssState.gnssData.vdop.toFixed(2) : '—' }}
            </div>
          </div>
          <div>
            <div class="text-sm text-gray-500 mb-1">
              PDOP
            </div>
            <div class="text-base font-bold" :class="gnssState.gnssData.pdop !== null ? 'text-gray-700' : 'text-slate-400'">
              {{ gnssState.gnssData.pdop !== null ? gnssState.gnssData.pdop.toFixed(2) : '—' }}
            </div>
          </div>
          <div>
            <div class="text-sm text-gray-500 mb-1">
              Satellites
            </div>
            <div class="text-base font-bold" :class="gnssState.gnssData.satellitesUsed !== null ? 'text-gray-800' : 'text-slate-400'">
              {{ gnssState.gnssData.satellitesUsed !== null ? gnssState.gnssData.satellitesUsed + '/' + gnssState.gnssData.satellitesTracked : '—' }}
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

  // GNSS Time computed properties
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

  const formatAccuracy = (meters) =>
  {
    if (meters === null || meters === undefined) return '—';

    // Convert to millimeters
    const mm = meters * 1000;

    // If >= 1000mm (1m), show in meters
    if (mm >= 1000)
    {
      return (mm / 1000).toFixed(1) + 'm';
    }
    // If >= 10mm (1cm), show in centimeters
    else if (mm >= 10)
    {
      return (mm / 10).toFixed(0) + 'cm';
    }
    // Otherwise show in millimeters
    else
    {
      return mm.toFixed(0) + 'mm';
    }
  };

  // Correction status formatting
  const formatCorrectionAge = () =>
  {
    const ageMs = gnssState.gnssData.corrections.status.age;
    if (ageMs === null || ageMs === undefined) return '—';

    // Convert milliseconds to seconds
    const ageSeconds = ageMs / 1000;

    if (ageSeconds < 60) 
    {
      return ageSeconds.toFixed(0) + 's';
    }
    else 
    {
      return (ageSeconds / 60).toFixed(0) + 'm';
    }
  };

  const getCorrectionAgeClass = () =>
  {
    const ageMs = gnssState.gnssData.corrections.status.age;
    if (ageMs === null || ageMs === undefined) return 'text-slate-400';

    const ageSeconds = ageMs / 1000;

    // Color code based on correction age
    if (ageSeconds <= 5) return 'text-green-600'; // Fresh
    else if (ageSeconds <= 30) return 'text-yellow-600'; // Aging
    else return 'text-red-600'; // Stale
  };

  const getCorrectionStatusClass = () =>
  {
    const status = gnssState.gnssData.corrections.status.status;
    const valid = gnssState.gnssData.corrections.status.valid;

    if (!valid) 
    {
      return 'bg-red-100 text-red-700';
    }

    switch (status) 
    {
    case 'Valid': return 'bg-green-100 text-green-700';
    case 'Stale': return 'bg-yellow-100 text-yellow-700';
    case 'Invalid': return 'bg-red-100 text-red-700';
    default: return 'bg-gray-100 text-gray-600';
    }
  };

</script>