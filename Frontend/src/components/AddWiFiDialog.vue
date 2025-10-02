<template>
  <Dialog :show="show"
          title="Add Network"
          subtitle="Enter network credentials to connect"
          @close="handleClose">
    <!-- Content -->
    <div class="space-y-4">
      <div>
        <label class="block text-xs font-medium text-gray-500 uppercase tracking-wide mb-2">Network Name (SSID)</label>
        <input v-model="networkConfig.ssid"
               type="text"
               class="w-full px-4 py-3 border border-gray-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-gray-900 focus:border-transparent transition-all duration-200"
               placeholder="Enter network name"
               :disabled="isConnecting">
      </div>

      <div>
        <label class="block text-xs font-medium text-gray-500 uppercase tracking-wide mb-2">Password</label>
        <div class="relative">
          <input v-model="networkConfig.password"
                 :type="showPassword ? 'text' : 'password'"
                 class="w-full px-4 py-3 pr-12 border border-gray-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-gray-900 focus:border-transparent transition-all duration-200"
                 placeholder="Enter network password"
                 :disabled="isConnecting">
          <button type="button"
                  class="absolute inset-y-0 right-0 pr-4 flex items-center btn-icon"
                  @click="showPassword = !showPassword">
            <svg v-if="showPassword" class="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z" />
            </svg>
            <svg v-else class="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M13.875 18.825A10.05 10.05 0 0112 19c-4.478 0-8.268-2.943-9.543-7a9.97 9.97 0 011.563-3.029m5.858.908a3 3 0 114.243 4.243M9.878 9.878l4.242 4.242M9.878 9.878L8.115 8.114m0 0L6.937 6.937m0 0a8.977 8.977 0 00-1.942-.845m1.942.845A8.977 8.977 0 0012 5c2.14 0 4.135.601 5.828 1.635l-1.937 1.937m0 0a3 3 0 00-4.243 4.243l1.937-1.937z" />
            </svg>
          </button>
        </div>
      </div>
    </div>

    <!-- Footer -->
    <template #footer>
      <div class="flex justify-end space-x-3">
        <button type="button"
                :disabled="isConnecting"
                class="btn-secondary"
                @click="handleClose">
          Cancel
        </button>
        <button :disabled="isConnecting || !isFormValid"
                class="btn-primary"
                @click="handleSubmit">
          <div v-if="isConnecting" class="flex items-center">
            <svg class="animate-spin -ml-1 mr-2 h-4 w-4 text-white" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
              <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4" />
              <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
            </svg>
            Connecting...
          </div>
          <span v-else>Connect to Network</span>
        </button>
      </div>
    </template>
  </Dialog>
</template>

<script setup>
import { ref, computed, watch } from 'vue';
import Dialog from './common/Dialog.vue';

const props = defineProps({
  show: {
    type: Boolean,
    required: true
  },
  isConnecting: {
    type: Boolean,
    default: false
  }
});

const emit = defineEmits(['close', 'submit']);

const networkConfig = ref({
  ssid: '',
  password: ''
});

const showPassword = ref(false);

const isFormValid = computed(() => {
  return networkConfig.value.ssid.trim() !== '' &&
         networkConfig.value.password.trim() !== '';
});

const handleClose = () => {
  // Reset form when closing
  networkConfig.value = {
    ssid: '',
    password: ''
  };
  showPassword.value = false;
  emit('close');
};

const handleSubmit = () => {
  if (isFormValid.value) {
    emit('submit', { ...networkConfig.value });
  }
};

// Reset form when dialog is closed
watch(() => props.show, (newValue) => {
  if (!newValue) {
    networkConfig.value = {
      ssid: '',
      password: ''
    };
    showPassword.value = false;
  }
});
</script>
