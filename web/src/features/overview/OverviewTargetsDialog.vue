<template>
  <v-dialog :model-value="modelValue" max-width="520" @update:model-value="$emit('update:modelValue', $event)">
    <v-card>
      <v-card-title class="pt-4">Overview targets</v-card-title>

      <v-card-text>
        <div class="text-body-2 text-medium-emphasis mb-4">
          Reference points for this dashboard. Leave a field blank to drop its target. Changes save
          together and apply for everyone.
        </div>

        <div v-if="error" class="text-error text-body-2 mb-3">{{ error }}</div>

        <v-text-field
          v-model="form.issueCompletionPctMin"
          label="Issue completion — at least (%)"
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
import type { OverviewTargets } from '../../types';
import { api } from '../../api';

const props = defineProps<{
  modelValue: boolean;
  targets: OverviewTargets | null;
}>();
const emit = defineEmits(['update:modelValue', 'saved']);

const busy = ref(false);
const error = ref<string | null>(null);

const form = reactive({ issueCompletionPctMin: '' });

const str = (n: number | null | undefined) => (n === null || n === undefined ? '' : String(n));

watch(
  () => props.modelValue,
  (open) => {
    if (!open) return;
    error.value = null;
    form.issueCompletionPctMin = str(props.targets?.issueCompletionPctMin);
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
    const payload: OverviewTargets = { issueCompletionPctMin: num(form.issueCompletionPctMin) };
    const saved = await api.url('/live/summary/targets').json(payload).put().json<OverviewTargets>();
    emit('saved', saved);
    close();
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e);
  } finally {
    busy.value = false;
  }
}
</script>
