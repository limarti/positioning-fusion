<template>
  <Dialog :show="show"
          title="RTK Corrections Mode"
          subtitle="Select the operating mode for RTK corrections"
          @close="$emit('close')">
    <!-- Content -->
    <div class="space-y-3">
      <RadioOption v-for="option in modeOptions"
                   :key="option.value"
                   v-model="localSelectedMode"
                   :value="option.value"
                   :label="option.label"
                   :description="option.description" />
    </div>

    <!-- Footer -->
    <template #footer>
      <div class="flex justify-end space-x-3">
        <button type="button"
                class="btn-secondary"
                @click="$emit('close')">
          Cancel
        </button>
        <button :disabled="localSelectedMode === currentMode"
                class="btn-primary"
                @click="handleSave">
          Apply Mode
        </button>
      </div>
    </template>
  </Dialog>
</template>

<script setup>
import { ref, watch } from 'vue';
import Dialog from '../common/Dialog.vue';
import RadioOption from '../common/RadioOption.vue';

const props = defineProps({
  show: {
    type: Boolean,
    required: true
  },
  currentMode: {
    type: String,
    required: true
  },
  modeOptions: {
    type: Array,
    required: true
  }
});

const emit = defineEmits(['close', 'change']);

const localSelectedMode = ref(props.currentMode);

// Reset local mode when dialog opens or currentMode changes
watch(() => props.show, (newValue) => {
  if (newValue) {
    localSelectedMode.value = props.currentMode;
  }
});

watch(() => props.currentMode, (newValue) => {
  localSelectedMode.value = newValue;
});

const handleSave = () => {
  emit('change', localSelectedMode.value);
  emit('close');
};
</script>
