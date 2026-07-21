<template>
  <v-dialog :model-value="modelValue" max-width="520" @update:model-value="$emit('update:modelValue', $event)">
    <v-card>
      <v-card-title class="pt-4">Delivery targets</v-card-title>

      <v-card-text>
        <div class="text-body-2 text-medium-emphasis mb-4">
          Reference lines for this dashboard's tiles and charts. Leave a field blank to drop its
          target. Changes save together and apply for everyone.
        </div>

        <div v-if="error" class="text-error text-body-2 mb-3">{{ error }}</div>

        <v-text-field
          v-model="form.deploysPerWeek"
          label="Deploy frequency — deploys / week (at least)"
          type="number"
          density="comfortable"
          hide-details
          class="mb-3"
        />
        <v-text-field
          v-model="form.leadTimeHours"
          label="Change lead time — hours (at most)"
          type="number"
          density="comfortable"
          hide-details
          class="mb-3"
        />
        <v-text-field
          v-model="form.cycleTimeHours"
          label="Issue cycle time — hours (at most)"
          type="number"
          density="comfortable"
          hide-details
          class="mb-3"
        />
        <v-text-field
          v-model="form.reviewLatencyHours"
          label="Time to first review — hours (at most)"
          type="number"
          density="comfortable"
          hide-details
        />
      </v-card-text>

      <v-card-actions class="px-4 pb-4">
        <v-spacer />
        <v-btn variant="text" @click="close">Cancel</v-btn>
        <v-btn color="primary" variant="tonal" :loading="busy" @click="save">Save</v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>

<script setup lang="ts">
import { reactive, ref, watch } from 'vue';
import type { DeliveryTargets } from '../../types';
import { api } from '../../api';

const props = defineProps<{
  modelValue: boolean;
  targets: DeliveryTargets | null;
}>();
const emit = defineEmits(['update:modelValue', 'saved']);

const busy = ref(false);
const error = ref<string | null>(null);

// Bound to number inputs, so held as strings ('' = cleared → null on save).
const form = reactive({
  deploysPerWeek: '',
  leadTimeHours: '',
  cycleTimeHours: '',
  reviewLatencyHours: ''
});

const str = (n: number | null | undefined) => (n === null || n === undefined ? '' : String(n));

// Prefill from current targets each time the dialog opens.
watch(
  () => props.modelValue,
  (open) => {
    if (!open) return;
    error.value = null;
    form.deploysPerWeek = str(props.targets?.deploysPerWeek);
    form.leadTimeHours = str(props.targets?.leadTimeHours);
    form.cycleTimeHours = str(props.targets?.cycleTimeHours);
    form.reviewLatencyHours = str(props.targets?.reviewLatencyHours);
  }
);

function close() {
  emit('update:modelValue', false);
}

// Blank → null; otherwise the parsed number (invalid input also becomes null).
function num(v: string): number | null {
  const t = v.trim();
  if (t === '') return null;
  const n = Number(t);
  return Number.isFinite(n) ? n : null;
}

async function save() {
  busy.value = true;
  error.value = null;
  try {
    const payload: DeliveryTargets = {
      deploysPerWeek: num(form.deploysPerWeek),
      leadTimeHours: num(form.leadTimeHours),
      cycleTimeHours: num(form.cycleTimeHours),
      reviewLatencyHours: num(form.reviewLatencyHours)
    };
    const saved = await api.url('/dashboards/delivery/targets').json(payload).put().json<DeliveryTargets>();
    emit('saved', saved);
    close();
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e);
  } finally {
    busy.value = false;
  }
}
</script>
