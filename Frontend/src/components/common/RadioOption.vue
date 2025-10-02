<template>
  <label class="relative cursor-pointer">
    <input v-model="internalValue"
           :value="value"
           type="radio"
           class="sr-only"
           @change="$emit('update:modelValue', value)">
    <div class="p-4 rounded-lg border-2 transition-all duration-200"
         :class="isSelected ? 'border-gray-900 bg-gray-50' : 'border-gray-200 hover:border-gray-300'">
      <div class="flex items-center justify-between">
        <div class="flex-1">
          <div class="font-medium text-gray-900">{{ label }}</div>
          <div v-if="description" class="text-xs text-gray-500 mt-1">
            {{ description }}
          </div>
        </div>
        <div class="w-4 h-4 rounded-full border-2 flex items-center justify-center flex-shrink-0 ml-3"
             :class="isSelected ? 'border-gray-900' : 'border-gray-300'">
          <div v-if="isSelected" class="w-2 h-2 rounded-full bg-gray-900" />
        </div>
      </div>
    </div>
  </label>
</template>

<script setup>
import { computed } from 'vue';

const props = defineProps({
  modelValue: {
    type: [String, Number, Boolean],
    required: true
  },
  value: {
    type: [String, Number, Boolean],
    required: true
  },
  label: {
    type: String,
    required: true
  },
  description: {
    type: String,
    default: ''
  }
});

const emit = defineEmits(['update:modelValue']);

const internalValue = computed({
  get() {
    return props.modelValue;
  },
  set(value) {
    emit('update:modelValue', value);
  }
});

const isSelected = computed(() => props.modelValue === props.value);
</script>
