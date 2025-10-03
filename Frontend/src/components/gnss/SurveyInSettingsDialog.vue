<template>
  <Dialog :show="show"
          title="Survey-In Settings"
          subtitle="Configure base station survey-in parameters"
          max-width="lg"
          @close="$emit('close')">
    <!-- Content -->
    <div v-if="!showConfirmation" class="space-y-6">
      <!-- Duration Setting -->
      <div>
        <label for="duration" class="block text-sm font-medium text-gray-700 mb-2">
          Survey Duration (seconds)
        </label>
        <input
          id="duration"
          v-model.number="localDuration"
          type="number"
          min="10"
          max="3600"
          step="1"
          class="w-full px-3 py-2 border rounded-md focus:ring-2 focus:ring-blue-500 focus:border-blue-500"
          :class="isDurationValid ? 'border-gray-300' : 'border-red-500'"
        />
        <p class="mt-1 text-xs text-gray-500">
          Minimum: 10s (testing), Typical: 300s (5 minutes), Maximum: 3600s (1 hour)
        </p>
        <p v-if="!isDurationValid" class="mt-1 text-xs text-red-600">
          Duration must be between 10 and 3600 seconds
        </p>
      </div>

      <!-- Accuracy Setting -->
      <div>
        <label for="accuracy" class="block text-sm font-medium text-gray-700 mb-2">
          Accuracy Limit (meters)
        </label>
        <input
          id="accuracy"
          v-model.number="localAccuracy"
          type="number"
          min="0.1"
          max="1000"
          step="0.1"
          class="w-full px-3 py-2 border rounded-md focus:ring-2 focus:ring-blue-500 focus:border-blue-500"
          :class="isAccuracyValid ? 'border-gray-300' : 'border-red-500'"
        />
        <p class="mt-1 text-xs text-gray-500">
          Lower = better accuracy, longer survey time. Typical: 1.0m for production, 100m for testing
        </p>
        <p v-if="!isAccuracyValid" class="mt-1 text-xs text-red-600">
          Accuracy must be between 0.1 and 1000 meters
        </p>
      </div>

      <!-- Reset Survey Button -->
      <div class="pt-4 border-t border-gray-200">
        <div class="flex items-start space-x-3">
          <div class="flex-1">
            <h4 class="text-sm font-medium text-gray-900">Reset Survey Process</h4>
            <p class="text-xs text-gray-500 mt-1">
              Restart the survey-in process with current settings. The receiver will recalculate the base station position.
            </p>
          </div>
          <button
            type="button"
            class="btn-secondary bg-red-50 text-red-700 hover:bg-red-100 border-red-200"
            @click="showConfirmation = true">
            Reset Survey
          </button>
        </div>
      </div>
    </div>

    <!-- Confirmation Panel -->
    <div v-else class="space-y-6">
      <div class="rounded-lg bg-red-50 border border-red-200 p-6">
        <div class="flex items-start space-x-3">
          <svg class="w-6 h-6 text-red-600 flex-shrink-0 mt-0.5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
          </svg>
          <div class="flex-1">
            <h3 class="text-lg font-semibold text-red-900 mb-3">
              Warning: Reset Survey-In Process
            </h3>
            <div class="text-sm text-red-800 space-y-2">
              <p class="font-medium">This action will:</p>
              <ul class="list-disc list-inside space-y-1 ml-2">
                <li>Interrupt RTK corrections immediately</li>
                <li>Reset the base station position calculation</li>
                <li>Cause rovers to lose RTK fix temporarily</li>
                <li>Change the broadcasted reference position</li>
              </ul>
              <p class="mt-4 font-medium">
                Are you sure you want to continue?
              </p>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Footer -->
    <template #footer>
      <!-- Normal Footer -->
      <div v-if="!showConfirmation" class="flex justify-end space-x-3">
        <button type="button"
                class="btn-secondary"
                @click="$emit('close')">
          Cancel
        </button>
        <button
          type="button"
          class="btn-primary"
          :disabled="!isFormValid || isSaving"
          @click="handleSave">
          <span v-if="!isSaving">Save Settings</span>
          <span v-else class="flex items-center">
            <svg class="animate-spin h-4 w-4 mr-2" fill="none" viewBox="0 0 24 24">
              <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4" />
              <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
            </svg>
            Saving...
          </span>
        </button>
      </div>

      <!-- Confirmation Footer -->
      <div v-else class="flex justify-end space-x-3">
        <button type="button"
                class="btn-secondary"
                :disabled="isResetting"
                @click="showConfirmation = false">
          Cancel
        </button>
        <button
          type="button"
          class="btn-primary bg-red-600 hover:bg-red-700 border-red-600"
          :disabled="isResetting"
          @click="confirmReset">
          <span v-if="!isResetting">Yes, Reset Survey</span>
          <span v-else class="flex items-center">
            <svg class="animate-spin h-4 w-4 mr-2" fill="none" viewBox="0 0 24 24">
              <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4" />
              <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
            </svg>
            Resetting...
          </span>
        </button>
      </div>
    </template>
  </Dialog>
</template>

<script setup>
import { ref, computed, watch } from 'vue';
import Dialog from '../common/Dialog.vue';
import { useGnssData } from '@/composables/useGnssData';
import { useSignalR } from '@/composables/useSignalR';

const props = defineProps({
  show: {
    type: Boolean,
    required: true
  }
});

const emit = defineEmits(['close']);

const { state, updateSettings, resetSurveyIn } = useGnssData();
const { signalrConnection } = useSignalR();

// Local form state
const localDuration = ref(null);
const localAccuracy = ref(null);
const isSaving = ref(false);
const isResetting = ref(false);
const showConfirmation = ref(false);

// Validation
const isDurationValid = computed(() => {
  return localDuration.value !== null &&
         localDuration.value >= 10 &&
         localDuration.value <= 3600;
});

const isAccuracyValid = computed(() => {
  return localAccuracy.value !== null &&
         localAccuracy.value >= 0.1 &&
         localAccuracy.value <= 1000;
});

const isFormValid = computed(() => {
  return isDurationValid.value && isAccuracyValid.value;
});

// Watch for dialog open and sync with state
watch(() => props.show, (newValue) => {
  if (newValue) {
    // Initialize local values from state
    localDuration.value = state.surveySettings.durationSeconds;
    localAccuracy.value = state.surveySettings.accuracyLimitMeters;
    // Reset confirmation state
    showConfirmation.value = false;
  }
});

// Save settings
const handleSave = async () => {
  if (!isFormValid.value) return;

  isSaving.value = true;
  try {
    const success = await updateSettings(
      signalrConnection.value,
      localDuration.value,
      localAccuracy.value
    );

    if (success) {
      emit('close');
    } else {
      console.error('Failed to save settings');
      // Could show error message to user
    }
  } catch (error) {
    console.error('Error saving settings:', error);
  } finally {
    isSaving.value = false;
  }
};

// Confirm and execute reset
const confirmReset = async () => {
  isResetting.value = true;
  try {
    const success = await resetSurveyIn(signalrConnection.value);

    if (success) {
      console.log('Survey-In reset triggered successfully');
      // Hide confirmation and show success
      showConfirmation.value = false;
    } else {
      console.error('Failed to reset survey - ensure in SEND mode');
      // Could show error message to user
    }
  } catch (error) {
    console.error('Error resetting survey:', error);
  } finally {
    isResetting.value = false;
  }
};
</script>
