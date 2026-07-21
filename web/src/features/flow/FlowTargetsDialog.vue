<template>
  <v-dialog :model-value="modelValue" max-width="520" @update:model-value="$emit('update:modelValue', $event)">
    <v-card>
      <v-card-title class="pt-4">Flow targets</v-card-title>

      <v-card-text>
        <div class="text-body-2 text-medium-emphasis mb-4">
          Ceilings for process-churn signals (lower is better). Leave a field blank to drop its
          target. Changes save together and apply for everyone.
        </div>

        <div v-if="error" class="text-error text-body-2 mb-3">{{ error }}</div>

        <v-text-field
          v-model="form.reopensMax"
          label="Reopens — at most"
          type="number"
          density="comfortable"
          hide-details
          class="mb-3"
        />
        <v-text-field
          v-model="form.backflowMax"
          label="Backflow (moves against the workflow) — at most"
          type="number"
          density="comfortable"
          hide-details
          class="mb-3"
        />
        <v-text-field
          v-model="form.reassignmentsMax"
          label="Reassignments — at most"
          type="number"
          density="comfortable"
          hide-details
          class="mb-3"
        />
        <v-text-field
          v-model="form.blockedHoursMax"
          label="Blocked time — at most (hours)"
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
import type { FlowTargets } from '../../types';
import { api } from '../../api';

const props = defineProps<{
  modelValue: boolean;
  targets: FlowTargets | null;
}>();
const emit = defineEmits(['update:modelValue', 'saved']);

const busy = ref(false);
const error = ref<string | null>(null);

const form = reactive({ reopensMax: '', backflowMax: '', reassignmentsMax: '', blockedHoursMax: '' });

const str = (n: number | null | undefined) => (n === null || n === undefined ? '' : String(n));

watch(
  () => props.modelValue,
  (open) => {
    if (!open) return;
    error.value = null;
    form.reopensMax = str(props.targets?.reopensMax);
    form.backflowMax = str(props.targets?.backflowMax);
    form.reassignmentsMax = str(props.targets?.reassignmentsMax);
    form.blockedHoursMax = str(props.targets?.blockedHoursMax);
  }
);

function close() {
  emit('update:modelValue', false);
}

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
    const payload: FlowTargets = {
      reopensMax: num(form.reopensMax),
      backflowMax: num(form.backflowMax),
      reassignmentsMax: num(form.reassignmentsMax),
      blockedHoursMax: num(form.blockedHoursMax)
    };
    const saved = await api.url('/live/issue-flow/targets').json(payload).put().json<FlowTargets>();
    emit('saved', saved);
    close();
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e);
  } finally {
    busy.value = false;
  }
}
</script>
