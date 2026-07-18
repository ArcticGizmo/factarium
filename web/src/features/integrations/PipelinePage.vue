<template>
  <BasePage title="Pipeline" subtitle="Transform & aggregate">
    <template #actions>
      <v-btn
        v-if="isDev"
        size="small"
        variant="tonal"
        color="red"
        :loading="busy"
        prepend-icon="mdi-delete-alert"
        @click="resetDatabase"
      >
        Clear database (debug)
      </v-btn>
      <v-btn size="small" variant="text" @click="load">Refresh</v-btn>
    </template>

    <div v-if="error" class="text-error mb-2">{{ error }}</div>

    <div class="text-body-2 text-medium-emphasis mb-3">
      After integrations replicate raw data, the pipeline transforms it into canonical entities and aggregates the daily
      metrics behind the dashboards. It runs on its own schedule; trigger it here to process what's been synced so far.
    </div>

    <div class="section-title">Steps</div>
    <div class="d-flex align-center flex-wrap ga-2 mb-4">
      <v-chip
        v-for="s in steps"
        :key="s.name"
        :color="statusColor[s.lastStatus] || 'grey'"
        variant="flat"
        size="small"
      >
        {{ s.name }} · {{ s.lastStatus }} · {{ s.lastItemsProcessed }} @ {{ fmt(s.lastRunAt) }}
      </v-chip>
      <span v-if="steps.length === 0" class="text-caption text-medium-emphasis">No runs yet.</span>
      <v-btn size="small" color="primary" variant="tonal" :loading="busy" @click="runPipeline">
        Run pipeline now
      </v-btn>
    </div>
  </BasePage>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue';
import BasePage from '../../components/BasePage.vue';
import type { PipelineStep } from '../../types';
import { formatDateTime as fmt } from '../../utils/datetime';

const steps = ref<PipelineStep[]>([]);
const error = ref<string | null>(null);
const busy = ref(false);
const isDev = ref(false);

async function load() {
  try {
    steps.value = await fetch('/api/pipeline').then((r) => r.json());
    error.value = null;
  } catch (e) {
    error.value = String(e);
  }
}

async function runPipeline() {
  busy.value = true;
  try {
    await fetch('/api/pipeline/run', { method: 'POST' });
    await load();
  } finally {
    busy.value = false;
  }
}

async function resetDatabase() {
  if (
    !window.confirm(
      'Clear ALL synced data (integrations, raw records, canonical, metrics, people)? This cannot be undone.'
    )
  ) {
    return;
  }
  busy.value = true;
  try {
    await fetch('/api/debug/reset', { method: 'POST' });
    await load();
  } finally {
    busy.value = false;
  }
}

const statusColor: Record<string, string> = {
  Success: 'green',
  Failed: 'red',
  Running: 'blue',
  Never: 'grey',
  success: 'green',
  failed: 'red',
  never: 'grey'
};

onMounted(async () => {
  await load();
  try {
    const health = await fetch('/api/health').then((r) => r.json());
    isDev.value = health.environment === 'Development';
  } catch {
    isDev.value = false;
  }
});
</script>

<style scoped>
.section-title {
  font-size: 0.9rem;
  font-weight: 600;
  margin: 4px 0 8px;
}
</style>
